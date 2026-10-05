using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
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
    private AppearanceOptions? _activeAppearance;
    private DesktopAppearance.PointerMove? _pointerMove;
    private PlacementRecovery? _placementRecovery;
    private bool _isResizing;
    private const double RolledUpHeight = 30; // 28-DIP header + 2-DIP outer border.

    public MainWindow(StateStore store, AppState state, string? startupNotice)
    {
        _store = store;
        _state = state;
        _state.Normalize();
        _startupNotice = startupNotice;
        Theme.Apply(state.LightTheme);
        InitializeComponent();
        Title = BuildIdentity.DisplayName;
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
        if (_state.PreferredDesktopPosition is null)
            DesktopAppearance.EnsureVisible(this, !_state.Left.HasValue);
        _placementRecovery = new PlacementRecovery(this, _state.PreferredDesktopPosition,
            () => _pointerMove is not null || _isResizing, RestorePreferredSize);
        if (_state.PreferredDesktopPosition is null) SaveWindowPosition(rememberPosition: true);
        else _placementRecovery.Schedule();
        NoteList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(NoteList_ScrollChanged));
        UpdateScrollButtons();
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
        menu.Items.Add("주 모니터로 가져오기", null, (_, _) => Dispatcher.Invoke(() =>
        {
            ShowWidget();
            _placementRecovery?.BringToPrimary();
            SaveWindowPosition(rememberPosition: true);
        }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(ExitApp));
        _tray = new Forms.NotifyIcon
        {
            Text = BuildIdentity.DisplayName, Icon = _trayIcon ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu, Visible = true
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWidget);
    }

    private void RestorePreferredSize()
    {
        Width = _state.Width;
        _expandedHeight = _state.Height;
        Height = _state.IsRolledUp ? RolledUpHeight : _expandedHeight;
    }

    public void ShowWidget()
    {
        if (_closed) return;
        Show();
        WindowState = WindowState.Normal;
        _placementRecovery?.Schedule();
        Activate();
        if (_settingsWindow is not null) _settingsWindow.Activate();
    }

    private void HideWidget()
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        SaveWindowPosition();
        Hide();
    }

    private void CaptureBounds(AppState state, bool rememberPosition = false)
    {
        // Refresh, settings, hide and exit must never save an OS-imposed temporary position.
        if (rememberPosition && (_placementRecovery?.RememberUserPosition() ?? false))
        {
            _state.Left = Left; _state.Top = Top;
            _state.Width = Width;
            _state.Height = _state.IsRolledUp ? _expandedHeight : Height;
            _expandedHeight = _state.Height;
        }
        state.Left = _state.Left; state.Top = _state.Top;
        state.Width = _state.Width; state.Height = _state.Height;
        state.PreferredDesktopPosition = _placementRecovery?.Preferred ?? _state.PreferredDesktopPosition;
    }

    private void SaveWindowPosition(bool rememberPosition = false)
    {
        CaptureBounds(_state, rememberPosition);
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
        _activeAppearance = preview;
        UpdateResponsiveText();
        NoteList.FontFamily = MemoAppearance.Font(preview.FontFamilyName);
        NoteList.FontWeight = preview.Bold ? FontWeights.Bold : FontWeights.Normal;
        NoteList.FontStyle = preview.Italic ? FontStyles.Italic : FontStyles.Normal;
        NoteList.Foreground = MemoAppearance.Brush(preview.TextColor);
        Resources["BubbleBrush"] = MemoAppearance.Brush(preview.BubbleColor, preview.BubbleBackgroundPercent);
        Resources["BubbleEdgeBrush"] = MemoAppearance.Brush(preview.TextColor, (int)Math.Round(preview.BubbleBackgroundPercent * 0.18));
        // A single neutral header stays readable independently of memo/glass opacity.
        Resources["HeaderBrush"] = MemoAppearance.HeaderBackground(preview.HeaderBackgroundPercent);
        Resources["HeaderEdgeBrush"] = MemoAppearance.Brush("#FFFFFF", (int)Math.Round(preview.HeaderBackgroundPercent * 0.55));
        Resources["HeaderDividerBrush"] = MemoAppearance.Brush("#000000", (int)Math.Round(preview.HeaderBackgroundPercent * 0.14));
        Resources["BarTextBrush"] = MemoAppearance.Brush("#33414B");
        Resources["BarHoverBrush"] = MemoAppearance.Brush("#000000", 9);
        Resources["MemoShape"] = preview.Shape;
        Resources["MemoTail"] = preview.TailSide;
        Resources["BubbleAlignment"] = MemoAppearance.Alignment(preview);
        Resources["BubbleSpacing"] = new Thickness(0, 0, 0, preview.BubbleGap);
        Surface.Background = MemoAppearance.Brush(MemoAppearance.GlassColor(preview.GlassColor, preview.LightTheme), preview.BackgroundPercent);
        Surface.BorderBrush = MemoAppearance.Brush("#FFFFFF", preview.OuterBorderPercent);
        GlassReflection.Opacity = preview.GlassEffect ? preview.BackgroundPercent / 100.0 : 0;
    }

    private double GetMemoFontSize(AppearanceOptions appearance) => MemoSizing.FontSize(
        appearance.FontSize, appearance.ScaleTextWithWindow, Width,
        _state.IsRolledUp ? _expandedHeight : Height, appearance.TextReferenceWidth, appearance.TextReferenceHeight);

    private void UpdateResponsiveText()
    {
        if (_activeAppearance is null || NoteList is null) return;
        double size = GetMemoFontSize(_activeAppearance);
        if (Math.Abs(NoteList.FontSize - size) > 0.01) NoteList.FontSize = size;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_closed) return;
        UpdateResponsiveText();
        UpdateHeaderContent();
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
            ? "설정에서 시트 공유 주소를 한 번 등록하면\n버튼 한 번으로 메모를 불러올 수 있습니다."
            : "구글 시트의 A열에 메모를 적고\n불러오기를 눌러 주세요.";
        UpdateHeaderContent();
        UpdateScrollButtons();
    }

    private void UpdateHeaderContent()
    {
        if (TimeText is null || TitleBadge is null) return;
        double width = ActualWidth > 0 ? ActualWidth : Width;
        TimeText.Visibility = width < 235 ? Visibility.Collapsed : Visibility.Visible;
        TimeText.Text = _state.LastFetched is { } time
            ? time.ToLocalTime().ToString(width < 290 ? "HH:mm" : "MM.dd HH:mm") : "갱신 없음";
        string fetchedDetail = _state.LastFetched is { } fetched
            ? $"최근 갱신 {fetched.ToLocalTime():yyyy.MM.dd HH:mm:ss}" : "최근 갱신 기록 없음";
        TimeText.ToolTip = fetchedDetail;
        TitleBadge.ToolTip = $"{fetchedDetail}\n끌어서 이동 · 우클릭으로 설정 / 구글 시트 열기";
        AutomationProperties.SetName(TimeText, fetchedDetail);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (_closed) return;
        RefreshButton.IsEnabled = !busy;
        SettingsMenuItem.IsEnabled = !busy;
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
        _settingsWindow = new SettingsWindow(_state, ApplySettingsAsync, PreviewAppearance, GetMemoFontSize) { Owner = this };
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
                next.SharedLinkSetupRequired = false;
                next.LastFetched = DateTimeOffset.Now; next.HasSnapshot = true;
            }
            else if (request.RemoveConnection)
            {
                sourceChanged = true;
                next.Source = null; next.Notes = []; next.LastFetched = null; next.HasSnapshot = false;
                next.SharedLinkSetupRequired = false;
            }
            request.Appearance.ApplyTo(next);
            next.AlwaysOnTop = request.AlwaysOnTop;
            next.LockPosition = request.LockPosition;
            next.RefreshOnStart = request.RefreshOnStart;
            CaptureBounds(next);
            next.Normalize();
            WindowsStartup.Registration.Save(request.StartWithWindows, Environment.ProcessPath,
                () => _store.Save(next, sourceChanged));
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

    private void WidgetMenu_Opened(object sender, RoutedEventArgs e)
    {
        SettingsMenuItem.IsEnabled = !_busy;
        CopyMenuItem.IsEnabled = NoteList.SelectedItem is string;
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
        // Native resizing/maximizing stays off; the existing custom resize handles still work.
        ResizeMode = ResizeMode.NoResize;
        ResizeHandles.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyRollState()
    {
        NoteArea.Visibility = _state.IsRolledUp ? Visibility.Collapsed : Visibility.Visible;
        TitleBar.CornerRadius = _state.IsRolledUp ? new CornerRadius(13) : new CornerRadius(13, 13, 0, 0);
        TitleBar.BorderThickness = _state.IsRolledUp ? new Thickness(0) : new Thickness(0, 0, 0, 1);
        MinHeight = _state.IsRolledUp ? RolledUpHeight : 280;
        Height = _state.IsRolledUp ? RolledUpHeight : Math.Max(280, _expandedHeight);
        RollToggleIcon.Data = Geometry.Parse(_state.IsRolledUp
            ? "M4,15 L14,15 M4,6 L9,11 L14,6"
            : "M4,3 L14,3 M4,12 L9,7 L14,12");
        RollToggleButton.ToolTip = _state.IsRolledUp ? "롤다운 · 말풍선 펼치기" : "롤업 · 말풍선 접기";
        AutomationProperties.SetName(RollToggleButton, _state.IsRolledUp ? "말풍선 펼치기" : "말풍선 접기");
        ApplyResizeState();
        UpdateScrollButtons();
    }

    private void RollToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_state.IsRolledUp)
        {
            _state.IsRolledUp = false;
            ApplyRollState();
            DesktopAppearance.EnsureVisible(this);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!_closed && !_state.IsRolledUp && FindScrollViewer(NoteList) is { } scroll)
                    scroll.ScrollToVerticalOffset(Math.Clamp(_rolledScrollOffset, 0, scroll.ScrollableHeight));
                UpdateScrollButtons();
            }));
        }
        else
        {
            _expandedHeight = Height;
            _rolledScrollOffset = FindScrollViewer(NoteList)?.VerticalOffset ?? 0;
            _state.IsRolledUp = true;
            ApplyRollState();
        }
        SaveWindowPosition();
    }

    private void ShowNotice(string message, bool error = false)
    {
        if (_closed) return;
        // The header timestamp shows the last successful fetch, including after a failure.
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

    private void NoteList_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateScrollButtons();

    private void UpdateScrollButtons()
    {
        if (_closed) return;
        var scroll = FindScrollViewer(NoteList);
        if (_state.IsRolledUp || _state.Notes.Count == 0 || scroll is null || scroll.ViewportHeight <= 0 || scroll.ScrollableHeight <= 0.5)
        {
            ScrollUpButton.IsEnabled = ScrollDownButton.IsEnabled = false;
            return;
        }
        ScrollUpButton.IsEnabled = scroll.VerticalOffset > 0.5;
        ScrollDownButton.IsEnabled = scroll.VerticalOffset < scroll.ScrollableHeight - 0.5;
    }

    private void ScrollNotes(int direction)
    {
        if (_state.IsRolledUp || FindScrollViewer(NoteList) is not { } scroll) return;
        double step = Math.Max(24, NoteList.FontSize * 2);
        scroll.ScrollToVerticalOffset(Math.Clamp(scroll.VerticalOffset + direction * step, 0, scroll.ScrollableHeight));
    }

    private void ScrollUp_Click(object sender, RoutedEventArgs e) => ScrollNotes(-1);
    private void ScrollDown_Click(object sender, RoutedEventArgs e) => ScrollNotes(1);

    private void Resize_Started(object sender, DragStartedEventArgs e)
    {
        _isResizing = true;
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
        _isResizing = false;
        if (e.Canceled)
        {
            Left = _resizeBounds.Left; Top = _resizeBounds.Top;
            Width = _resizeBounds.Width; Height = _resizeBounds.Height;
            return;
        }
        DesktopAppearance.EnsureVisible(this);
        SaveWindowPosition(rememberPosition: true);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_state.LockPosition) return;
        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node is not null)
        {
            if (node is ButtonBase) return; // Includes the scroll RepeatButtons in the header.
            if (node is Window) break;
            node = VisualTreeHelper.GetParent(node);
        }
        if (e.ButtonState == MouseButtonState.Pressed && _pointerMove is null)
        {
            var move = DesktopAppearance.BeginPointerMove(this);
            if (move is not null && TitleBar.CaptureMouse())
            {
                _pointerMove = move;
                e.Handled = true;
            }
        }
    }

    private void Header_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pointerMove is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndPointerMove(); return; }
        _pointerMove.Update();
        e.Handled = true;
    }

    private void Header_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pointerMove is null) return;
        _pointerMove.Update();
        EndPointerMove();
        e.Handled = true;
    }

    private void Header_LostMouseCapture(object sender, MouseEventArgs e) => EndPointerMove(cancel: true);

    private void EndPointerMove(bool cancel = false)
    {
        var move = _pointerMove;
        if (move is null) return;
        _pointerMove = null; // Releasing capture raises LostMouseCapture synchronously.
        if (cancel) move.Restore();
        if (TitleBar.IsMouseCaptured) TitleBar.ReleaseMouseCapture();
        if (_closed || cancel || !move.HasMoved) return;
        DesktopAppearance.EnsureVisible(this);
        SaveWindowPosition(rememberPosition: true);
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _pointerMove is not null) { e.Handled = true; EndPointerMove(cancel: true); }
        else if (e.Key == Key.F5) { e.Handled = true; await RefreshAsync(); }
        else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; CopySelected(); }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Settings_Click(object sender, RoutedEventArgs e) { WidgetMenu.IsOpen = false; OpenSettings(); }
    private void Sheets_Click(object sender, RoutedEventArgs e) { WidgetMenu.IsOpen = false; OpenSheets(); }
    private void Hide_Click(object sender, RoutedEventArgs e) => HideWidget();
    private void Copy_Click(object sender, RoutedEventArgs e) => CopySelected();

    private void ExitApp() { _exit = true; Close(); }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        EndPointerMove();
        if (!_exit) { e.Cancel = true; HideWidget(); return; }
        SaveWindowPosition();
    }
    private void Window_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _lifetime.Cancel();
        _placementRecovery?.Dispose();
        _tray?.Dispose(); _trayIcon?.Dispose(); _client.Dispose();
        Application.Current.Shutdown();
    }
}
