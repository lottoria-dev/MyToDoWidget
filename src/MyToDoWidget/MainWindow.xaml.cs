using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Win32;
using MyToDoWidget.Services;
using Forms = System.Windows.Forms;

namespace MyToDoWidget;

public partial class MainWindow : Window
{
    private readonly StateStore _store;
    private readonly SheetClient _client = new();
    private readonly CancellationTokenSource _lifetime = new();
    private AppState _state;
    private readonly string? _startupNotice;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private Forms.ToolStripMenuItem? _trayRefresh;
    private SettingsWindow? _settingsWindow;
    private bool _busy, _exit, _closed;
    private int _renderGeneration;
    private Rect _resizeBounds;
    private Point _resizePointer;
    private DpiScale _resizeDpi;
    private double _expandedHeight;
    private double _rolledScrollOffset;
    private const double RolledUpHeight = 62; // 24 title + 20 footer + 16 padding + 2 border.

    public MainWindow(StateStore store, AppState state, string? startupNotice)
    {
        _store = store;
        _state = state;
        _startupNotice = startupNotice;
        Theme.Apply(state.LightTheme);
        InitializeComponent();
        Width = state.Width;
        Height = state.Height;
        _expandedHeight = state.Height;
        if (state.Left.HasValue && state.Top.HasValue) { Left = state.Left.Value; Top = state.Top.Value; }
        ApplyAppearance();
        ApplyRollState();
        RenderNotes();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeTray();
        DesktopAppearance.EnsureVisible(this, !_state.Left.HasValue);
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        if (_startupNotice is not null) ShowNotice(_startupNotice, true);
        if (_state.RefreshOnStart && _state.Source is not null) await RefreshAsync();
    }

    private void InitializeTray()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        if (resource is not null)
        {
            using (resource.Stream)
            using (var original = new System.Drawing.Icon(resource.Stream))
                _trayIcon = (System.Drawing.Icon)original.Clone();
        }
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("위젯 표시 / 숨기기", null, (_, _) => Dispatcher.Invoke(() => { if (IsVisible) HideWidget(); else ShowWidget(); }));
        _trayRefresh = new Forms.ToolStripMenuItem("메모 불러오기", null, (_, _) => Dispatcher.InvokeAsync(async () => await RefreshAsync()));
        menu.Items.Add(_trayRefresh);
        menu.Items.Add("구글 시트 열기", null, (_, _) => Dispatcher.Invoke(OpenSheets));
        menu.Items.Add("설정", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add("주 모니터로 가져오기", null, (_, _) => Dispatcher.Invoke(() => { ShowWidget(); DesktopAppearance.EnsureVisible(this, true); }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(ExitApp));
        _tray = new Forms.NotifyIcon
        {
            Text = "MyToDo", Icon = _trayIcon ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu, Visible = true
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWidget);
    }

    private void DisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (!_closed) Dispatcher.BeginInvoke(new Action(() => { if (!_closed) DesktopAppearance.EnsureVisible(this); }));
    }

    public void ShowWidget()
    {
        if (_closed) return;
        Show();
        WindowState = WindowState.Normal;
        DesktopAppearance.EnsureVisible(this);
        Activate();
        if (_settingsWindow is not null) _settingsWindow.Activate();
    }

    private void HideWidget()
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        SaveWindowPosition();
        Hide();
    }

    private void CaptureBounds(AppState state)
    {
        state.Left = Left; state.Top = Top;
        state.Width = Width;
        state.Height = _state.IsRolledUp ? _expandedHeight : Height;
    }

    private void SaveWindowPosition()
    {
        CaptureBounds(_state);
        try { _store.Save(_state); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { ShowNotice("설정을 저장하지 못했습니다. 사용자 폴더의 쓰기 권한을 확인해 주세요.", true); }
    }

    private void ApplyAppearance()
    {
        Topmost = _state.AlwaysOnTop;
        ApplyResizeState();
        PreviewAppearance(AppearanceOptions.FromState(_state));
    }

    private void PreviewAppearance(AppearanceOptions preview)
    {
        // Change only visuals. The saved state is untouched until Save succeeds.
        Theme.Apply(preview.LightTheme);
        NoteList.FontSize = preview.FontSize;
        NoteList.FontFamily = MemoAppearance.Font(preview.FontFamilyName);
        NoteList.FontWeight = preview.Bold ? FontWeights.Bold : FontWeights.Normal;
        NoteList.FontStyle = preview.Italic ? FontStyles.Italic : FontStyles.Normal;
        NoteList.Foreground = MemoAppearance.Brush(preview.TextColor);
        Resources["BubbleBrush"] = MemoAppearance.Brush(preview.BubbleColor, preview.BubbleBackgroundPercent);
        Resources["BubbleEdgeBrush"] = MemoAppearance.Brush(preview.TextColor, (int)Math.Round(preview.BubbleBackgroundPercent * 0.18));
        Resources["MemoShape"] = preview.Shape;
        Resources["MemoTail"] = preview.TailSide;
        Resources["BubbleAlignment"] = preview.TailSide == BubbleTail.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        byte alpha = (byte)Math.Round(Math.Clamp(preview.BackgroundPercent, 0, 100) * 255.0 / 100);
        var color = preview.LightTheme ? Color.FromArgb(alpha, 240, 244, 248) : Color.FromArgb(alpha, 26, 34, 47);
        Surface.Background = new SolidColorBrush(color);
        Surface.BorderBrush = MemoAppearance.Brush("#FFFFFF", (int)Math.Round(preview.BackgroundPercent * 0.35));
        GlassReflection.Opacity = preview.GlassEffect ? preview.BackgroundPercent / 100.0 : 0;
    }

    private void RenderNotes(bool resetScroll = false)
    {
        if (resetScroll) _rolledScrollOffset = 0;
        // An unchanged refresh or a settings change must not jump back to the first memo.
        bool changed = NoteList.ItemsSource is not IReadOnlyList<string> oldNotes || !oldNotes.SequenceEqual(_state.Notes);
        if (changed || resetScroll)
        {
            double offset = resetScroll ? 0 : _state.IsRolledUp ? _rolledScrollOffset : FindScrollViewer(NoteList)?.VerticalOffset ?? 0;
            string? selected = resetScroll ? null : NoteList.SelectedItem as string;
            NoteList.ItemsSource = _state.Notes;
            if (selected is not null && _state.Notes.Contains(selected)) NoteList.SelectedItem = selected;
            int generation = ++_renderGeneration;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_closed || generation != _renderGeneration) return;
                NoteList.UpdateLayout();
                if (FindScrollViewer(NoteList) is { } scroll)
                    scroll.ScrollToVerticalOffset(Math.Clamp(offset, 0, scroll.ScrollableHeight));
            }));
        }
        EmptyPanel.Visibility = _state.Notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptySettingsButton.Visibility = _state.Source is null ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = _state.Source is null ? "메모를 연결해 주세요" : "표시할 메모가 없습니다";
        EmptyText.Text = _state.Source is null
            ? "설정에서 CSV 게시 주소를 한 번 등록하면\n버튼 한 번으로 메모를 불러올 수 있습니다."
            : "구글 시트의 A열에 메모를 적고\n불러오기를 눌러 주세요.";
        TimeText.Text = _state.LastFetched is { } time
            ? $"최근 갱신 {time.ToLocalTime():yyyy.MM.dd HH:mm:ss}" : "최근 갱신 기록 없음";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (_closed) return;
        RefreshButton.IsEnabled = !busy;
        SettingsButton.IsEnabled = !busy;
        EmptySettingsButton.IsEnabled = !busy;
        if (_trayRefresh is not null) _trayRefresh.Enabled = !busy;
        RefreshButton.ToolTip = busy ? "새로고침 중…" : "새로고침 (F5)";
    }

    private async Task RefreshAsync()
    {
        if (_busy || _closed) return;
        if (_state.Source is null) { OpenSettings(); return; }
        SetBusy(true);
        try
        {
            var notes = await _client.FetchAsync(_state.Source, _lifetime.Token);
            var next = _state.Copy();
            next.Notes = notes; next.LastFetched = DateTimeOffset.Now; next.HasSnapshot = true;
            CaptureBounds(next);
            _store.Save(next);
            _state = next;
            RenderNotes();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (MemoException ex) { ShowNotice(ex.Message + (_state.HasSnapshot ? " 기존 메모를 유지합니다." : ""), true); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { ShowNotice("메모를 불러오거나 저장하지 못했습니다. 기존 메모를 유지합니다.", true); }
        finally { SetBusy(false); }
    }

    private void OpenSettings()
    {
        if (_busy || _closed) return;
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        ShowWidget();
        _settingsWindow = new SettingsWindow(_state, ApplySettingsAsync, PreviewAppearance) { Owner = this };
        try { _settingsWindow.ShowDialog(); }
        finally { _settingsWindow = null; ApplyAppearance(); }
    }

    private async Task<string?> ApplySettingsAsync(SettingsRequest request)
    {
        if (_busy) return "불러오기가 끝난 뒤 다시 시도해 주세요.";
        var next = _state.Copy();
        bool sourceChanged = false;
        SetBusy(true);
        try
        {
            if (!string.IsNullOrWhiteSpace(request.NewAddress))
            {
                string source = SheetSource.Normalize(request.NewAddress).AbsoluteUri;
                var notes = await _client.FetchAsync(source, _lifetime.Token);
                sourceChanged = source != _state.Source;
                next.Source = source; next.Notes = notes;
                next.LastFetched = DateTimeOffset.Now; next.HasSnapshot = true;
            }
            else if (request.RemoveConnection)
            {
                sourceChanged = true;
                next.Source = null; next.Notes = []; next.LastFetched = null; next.HasSnapshot = false;
            }
            request.Appearance.ApplyTo(next);
            next.AlwaysOnTop = request.AlwaysOnTop;
            next.LockPosition = request.LockPosition;
            next.RefreshOnStart = request.RefreshOnStart;
            CaptureBounds(next);
            next.Normalize();
            _store.Save(next, sourceChanged);
            _state = next;
            ApplyAppearance();
            RenderNotes(resetScroll: sourceChanged);
            return null;
        }
        catch (MemoException ex) { return ex.Message; }
        catch (OperationCanceledException) { return "작업이 취소되었습니다."; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { return "설정을 저장하지 못했습니다. 기존 설정과 메모는 유지됩니다."; }
        finally { SetBusy(false); }
    }

    private static void OpenSheets()
    {
        try { Process.Start(new ProcessStartInfo(SheetSource.SheetsHome) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { MessageBox.Show("기본 웹 브라우저를 열지 못했습니다. Windows의 기본 앱 설정을 확인해 주세요.", "MyToDo", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private void CopySelected()
    {
        if (NoteList.SelectedItem is not string note) return;
        try { Clipboard.SetText(note); }
        catch (System.Runtime.InteropServices.ExternalException) { ShowNotice("클립보드가 사용 중입니다. 다시 시도해 주세요.", true); }
    }

    private void NoteList_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject item
            && ItemsControl.ContainerFromElement(NoteList, item) is ListBoxItem listItem)
            listItem.IsSelected = true;
    }

    private void NoteList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node is not null && node != NoteList)
        {
            if (node is Controls.SpeechBubble)
            {
                e.Handled = true;
                OpenSheets();
                return;
            }
            node = VisualTreeHelper.GetParent(node);
        }
    }

    private void ApplyResizeState()
    {
        bool locked = _state.LockPosition || _state.IsRolledUp;
        ResizeMode = locked ? ResizeMode.NoResize : ResizeMode.CanResize;
        ResizeHandles.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyRollState()
    {
        NoteArea.Visibility = _state.IsRolledUp ? Visibility.Collapsed : Visibility.Visible;
        MinHeight = _state.IsRolledUp ? RolledUpHeight : 280;
        Height = _state.IsRolledUp ? RolledUpHeight : Math.Max(280, _expandedHeight);
        RollUpButton.IsEnabled = !_state.IsRolledUp;
        RollDownButton.IsEnabled = _state.IsRolledUp;
        ApplyResizeState();
    }

    private void RollUp_Click(object sender, RoutedEventArgs e)
    {
        if (_state.IsRolledUp) return;
        _expandedHeight = Height;
        _rolledScrollOffset = FindScrollViewer(NoteList)?.VerticalOffset ?? 0;
        _state.IsRolledUp = true;
        ApplyRollState();
        SaveWindowPosition();
    }

    private void RollDown_Click(object sender, RoutedEventArgs e)
    {
        if (!_state.IsRolledUp) return;
        _state.IsRolledUp = false;
        ApplyRollState();
        DesktopAppearance.EnsureVisible(this);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!_closed && FindScrollViewer(NoteList) is { } scroll)
                scroll.ScrollToVerticalOffset(Math.Clamp(_rolledScrollOffset, 0, scroll.ScrollableHeight));
        }));
        SaveWindowPosition();
    }

    private void ShowNotice(string message, bool error = false)
    {
        if (_closed) return;
        // The footer is reserved for the last successful fetch, including after a failure.
        if (IsVisible && !_exit)
            MessageBox.Show(_settingsWindow ?? (Window)this, message, "MyToDo", MessageBoxButton.OK,
                error ? MessageBoxImage.Warning : MessageBoxImage.Information);
        else _tray?.ShowBalloonTip(4000, "MyToDo", message, error ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer scroll) return scroll;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    private void NoteList_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindScrollViewer(NoteList) is not { } scroll) return;
        int lines = SystemParameters.WheelScrollLines;
        // Pixel scrolling lets one tall bubble be read gradually. Honor Windows wheel settings
        // and retain fractional movement from high-resolution mouse wheels.
        double step = lines < 0 ? scroll.ViewportHeight : lines * Math.Max(16, NoteList.FontSize * 1.5);
        double offset = scroll.VerticalOffset - e.Delta / 120.0 * step;
        scroll.ScrollToVerticalOffset(Math.Clamp(offset, 0, scroll.ScrollableHeight));
        e.Handled = true;
    }

    private void Resize_Started(object sender, DragStartedEventArgs e)
    {
        _resizeBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
        _resizePointer = PointToScreen(Mouse.GetPosition(this));
        _resizeDpi = VisualTreeHelper.GetDpi(this);
    }

    private void Resize_Delta(object sender, DragDeltaEventArgs e)
    {
        if (_state.LockPosition || _state.IsRolledUp || sender is not Thumb { Tag: string edge }) return;
        Point pointer = PointToScreen(Mouse.GetPosition(this));
        double dx = (pointer.X - _resizePointer.X) / _resizeDpi.DpiScaleX;
        double dy = (pointer.Y - _resizePointer.Y) / _resizeDpi.DpiScaleY;
        if (edge.Contains('W'))
        {
            Width = Math.Clamp(_resizeBounds.Width - dx, MinWidth, MaxWidth);
            Left = _resizeBounds.Right - Width;
        }
        else if (edge.Contains('E')) Width = Math.Clamp(_resizeBounds.Width + dx, MinWidth, MaxWidth);
        if (edge.Contains('N'))
        {
            Height = Math.Clamp(_resizeBounds.Height - dy, MinHeight, MaxHeight);
            Top = _resizeBounds.Bottom - Height;
        }
        else if (edge.Contains('S')) Height = Math.Clamp(_resizeBounds.Height + dy, MinHeight, MaxHeight);
        e.Handled = true;
    }

    private void Resize_Completed(object sender, DragCompletedEventArgs e)
    {
        if (e.Canceled)
        {
            Left = _resizeBounds.Left; Top = _resizeBounds.Top;
            Width = _resizeBounds.Width; Height = _resizeBounds.Height;
        }
        DesktopAppearance.EnsureVisible(this);
        SaveWindowPosition();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_state.LockPosition) return;
        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node is not null)
        {
            if (node is Button) return;
            if (node is Window) break;
            node = VisualTreeHelper.GetParent(node);
        }
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); SaveWindowPosition(); }
            catch (InvalidOperationException) { }
        }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { e.Handled = true; await RefreshAsync(); }
        else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; CopySelected(); }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private void Sheets_Click(object sender, RoutedEventArgs e) => OpenSheets();
    private void Hide_Click(object sender, RoutedEventArgs e) => HideWidget();
    private void Copy_Click(object sender, RoutedEventArgs e) => CopySelected();

    private void ExitApp() { _exit = true; Close(); }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_exit) { e.Cancel = true; HideWidget(); return; }
        SaveWindowPosition();
    }
    private void Window_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _lifetime.Cancel();
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        _tray?.Dispose(); _trayIcon?.Dispose(); _client.Dispose();
        Application.Current.Shutdown();
    }
}
