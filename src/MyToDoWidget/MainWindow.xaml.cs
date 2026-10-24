using System.ComponentModel;
using Microsoft.Win32;
using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using MyToDoWidget.Services;
using Forms = System.Windows.Forms;

namespace MyToDoWidget;

public partial class MainWindow : Window
{
    private readonly LocalWorkspace _workspace;
    private readonly SyncService _sync;
    private readonly AppsScriptClient _client = new();
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
    private MemoItem? _contextNote;
    private readonly Dictionary<string, MemoCard> _cards = [];
    private readonly DispatcherTimer _syncTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private DateTimeOffset _nextSync = DateTimeOffset.MaxValue;
    private int _failures;
    private string? _syncError;
    private MemoKind? _visibleTab;
    private bool _updatingView, _sessionLocked, _suspended;
    private MemoEditorWindow? _editorWindow;
    private readonly EditSyncQueue _editSyncRequests = new();
    private bool _editSyncScheduled;
    private ReminderWindow? _reminderWindow;
    private const double RolledUpHeight = 30; // 28-DIP header + 2-DIP outer border.

    public MainWindow(StateStore store, AppState state, string? startupNotice)
    {
        _workspace = new LocalWorkspace(store, state);
        _sync = new SyncService(_workspace, _client);
        _state = _workspace.Snapshot();
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
        _workspace.Changed += Workspace_Changed;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeTray();
        if (_state.PreferredDesktopPosition is null)
            DesktopAppearance.EnsureVisible(this, !_state.Left.HasValue);
        _placementRecovery = new PlacementRecovery(this, _state.PreferredDesktopPosition,
            () => _pointerMove is not null || _isResizing || _draggingCard, RestorePreferredSize);
        if (_state.PreferredDesktopPosition is null) SaveWindowPosition(rememberPosition: true);
        else _placementRecovery.Schedule();
        NoteList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(NoteList_ScrollChanged));
        LocationChanged += (_, _) => RequestContentHeight();
        UpdateScrollButtons();
        if (_startupNotice is not null) ShowNotice(_startupNotice, true);
        SystemEvents.PowerModeChanged += PowerMode_Changed;
        SystemEvents.SessionSwitch += Session_Changed;
        _syncTimer.Tick += async (_, _) => { if (!_sessionLocked && !_suspended && SyncPolicy.IsDue(DateTimeOffset.UtcNow, _state.HasConnection, _state.SyncIntervalSeconds, _nextSync)) await RefreshAsync(false); };
        _reminderTimer.Tick += (_, _) => { CheckReminders(); UpdateCardColors(); };
        _syncTimer.Start(); _reminderTimer.Start();
        RequestContentHeight();
        // One startup request, even when periodic polling is manual or the old startup flag is off.
        if (SyncPolicy.ShouldRefreshOnStart(_state.HasConnection, _state.SyncIntervalSeconds, _state.RefreshOnStart))
            await RefreshAsync(false);
        else ScheduleNextSync();
        if (!_state.WelcomeShown)
        {
            ChangeLocal(next => next.WelcomeShown = true);
            OpenSettings();
        }

        CheckReminders();
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
        var menu = new Forms.ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = false };
        menu.Items.Add("위젯 표시 / 숨기기", null, (_, _) => Dispatcher.Invoke(() => { if (IsVisible) HideWidget(); else ShowWidget(); }));
        _trayRefresh = new Forms.ToolStripMenuItem("지금 동기화", null, (_, _) => Dispatcher.InvokeAsync(async () => await RefreshAsync()));
        menu.Items.Add(_trayRefresh);
        menu.Items.Add("새 메모 / 일정", null, (_, _) => Dispatcher.Invoke(() => OpenEditor(null)));
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
        Height = _state.IsRolledUp ? RolledUpHeight : _expandedHeight;
        RequestContentHeight();
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
            // Automatic content fitting must not change the font scale reference height.
        }
        state.Left = _state.Left; state.Top = _state.Top;
        state.Width = _state.Width; state.Height = _state.Height;
        state.PreferredDesktopPosition = _placementRecovery?.Preferred ?? _state.PreferredDesktopPosition;
    }

    private void SaveWindowPosition(bool rememberPosition = false)
    {
        CaptureBounds(_state, rememberPosition);
        try
        {
            _workspace.Update(next =>
            {
                next.Left = _state.Left; next.Top = _state.Top; next.Width = _state.Width; next.Height = _state.Height;
                next.PreferredDesktopPosition = _state.PreferredDesktopPosition; next.IsRolledUp = _state.IsRolledUp;
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { ShowNotice("위치와 설정을 저장하지 못했습니다. 사용자 폴더의 쓰기 권한을 확인해 주세요.", true); }
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
        Resources["BubbleOpacity"] = preview.BubbleBackgroundPercent;
        Resources["TagTextBrush"] = MemoAppearance.Brush(preview.TagTextColor);
        UpdateCardColors();
        Resources["BubbleEdgeBrush"] = MemoAppearance.Brush(preview.TextColor, (int)Math.Round(preview.BubbleBackgroundPercent * 0.18));
        // A single neutral header stays readable independently of memo/glass opacity.
        Resources["HeaderBrush"] = MemoAppearance.HeaderBackground(preview.HeaderBackgroundPercent);
        Resources["HeaderEdgeBrush"] = MemoAppearance.Brush("#FFFFFF", (int)Math.Round(preview.HeaderBackgroundPercent * 0.55));
        Resources["HeaderDividerBrush"] = MemoAppearance.Brush("#000000", (int)Math.Round(preview.HeaderBackgroundPercent * 0.14));
        Resources["BarTextBrush"] = MemoAppearance.Brush("#33414B");
        Resources["BarHoverBrush"] = MemoAppearance.Brush("#000000", 9);
        Resources["MemoShape"] = preview.Shape;
        Resources["MemoTail"] = preview.TailSide;
        UpdateBubbleLayout();
        Resources["BubbleSpacing"] = new Thickness(0, 0, 0, preview.BubbleGap);
        Surface.Background = MemoAppearance.Brush(MemoAppearance.GlassColor(preview.GlassColor, preview.LightTheme), preview.BackgroundPercent);
        Surface.BorderBrush = MemoAppearance.Brush("#FFFFFF", preview.OuterBorderPercent);
        GlassReflection.Opacity = preview.GlassEffect ? preview.BackgroundPercent / 100.0 : 0;
        RequestContentHeight();
    }

    private void UpdateBubbleLayout()
    {
        Resources["BubbleAlignment"] = HorizontalAlignment.Stretch;
        Resources["FitMemoContentWidth"] = false;
    }

    private void UpdateCardColors()
    {
        if (_activeAppearance is not { } a) return;
        foreach (var card in _cards.Values) card.ApplyColors(a.BubbleColor, a.UrgentHighlight, a.UrgentDays, a.UrgentColor, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
    }

    private double GetMemoFontSize(AppearanceOptions appearance) => MemoSizing.FontSize(
        appearance.FontSize, appearance.ScaleTextWithWindow, Width,
        _state.Height, appearance.TextReferenceWidth, appearance.TextReferenceHeight);

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
        if (e.WidthChanged) RequestContentHeight();
    }

    private void Workspace_Changed()
    {
        if (_closed) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(Workspace_Changed)); return; }
        bool previousRoll = _state.IsRolledUp;
        _state = _workspace.Snapshot();
        if (previousRoll != _state.IsRolledUp) ApplyRollState();
        RenderNotes();
        _reminderWindow?.Prune(_state.Items);
        if (!_state.NotificationsEnabled) _reminderWindow?.Close();
    }

    private void RenderNotes(bool resetScroll = false)
    {
        UpdateBubbleLayout();
        bool tabChanged = _visibleTab != _state.ActiveTab;
        double offset = resetScroll ? 0 : tabChanged ? _state.ScrollOffsets.GetValueOrDefault(_state.ActiveTab)
            : _state.IsRolledUp ? _rolledScrollOffset : FindScrollViewer(NoteList)?.VerticalOffset ?? 0;
        if (tabChanged && _state.IsRolledUp) _rolledScrollOffset = offset;
        string? selectedId = tabChanged ? null : (NoteList.SelectedItem as MemoCard)?.Id;
        string query = _state.SearchText.Trim();
        var visible = MemoOrdering.ForTab(_state.Items, _state.ActiveTab, _state.ItemOrders.GetValueOrDefault(_state.ActiveTab))
            .Where(i => (_state.ShowCompleted || !i.IsCompleted)
                && (query.Length == 0 || TaggedText.Matches(i, query)));
        var cards = visible.Select(item =>
        {
            int level = _state.ItemViewLevels.GetValueOrDefault(item.Id, _state.PreviewLines);
            if (!_cards.TryGetValue(item.Id, out var card)) _cards[item.Id] = card = new MemoCard(item, level);
            else card.Update(item, level);
            if (_activeAppearance is { } a) card.ApplyColors(a.BubbleColor, a.UrgentHighlight, a.UrgentDays, a.UrgentColor, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
            return card;
        }).ToList();
        if (NoteList.ItemsSource is not IReadOnlyList<MemoCard> old || !old.Select(c => c.Id).SequenceEqual(cards.Select(c => c.Id)))
        {
            NoteList.ItemsSource = cards;
            if (selectedId is not null) NoteList.SelectedItem = cards.FirstOrDefault(c => c.Id == selectedId);
        }
        // Small lists are fully measured so expanded cards have an exact natural height.
        VirtualizingPanel.SetIsVirtualizing(NoteList, cards.Count > 40);
        _visibleTab = _state.ActiveTab;
        int generation = ++_renderGeneration;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_closed || generation != _renderGeneration) return;
            NoteList.UpdateLayout();
            RequestContentHeight();
            if (FindScrollViewer(NoteList) is { } scroll) scroll.ScrollToVerticalOffset(Math.Clamp(offset, 0, scroll.ScrollableHeight));
            UpdateScrollButtons();
        }));
        _updatingView = true;
        try
        {
            SearchBox.Text = _state.SearchText; CompletedCheck.IsChecked = _state.ShowCompleted;
            if (_state.SearchText.Length > 0) SearchPanel.Visibility = Visibility.Visible;
        }
        finally { _updatingView = false; }
        UpdateModeSelection();
        var memoCount = _state.Items.Count(i => !i.IsDeleted && !i.IsCompleted && i.Kind == MemoKind.Memo);
        var scheduleCount = _state.Items.Count(i => !i.IsDeleted && !i.IsCompleted && i.Kind == MemoKind.Schedule);
        MemoModeButton.ToolTip = $"M · 메모 · 미완료 {memoCount}개";
        int approaching = _state.Items.Count(i => !i.IsDeleted && !i.IsCompleted && i.Kind == MemoKind.Schedule
            && CardColors.IsApproaching(i, _activeAppearance?.UrgentDays ?? _state.UrgentDays, DateTimeOffset.UtcNow, TimeZoneInfo.Local));
        ScheduleModeButton.ToolTip = $"S · 일정 · 미완료 {scheduleCount}개"
            + (approaching > 0 ? $"\n오늘부터 {_activeAppearance?.UrgentDays ?? _state.UrgentDays}일 이내 {approaching}개" : "");
        EmptyPanel.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptySettingsButton.Visibility = _state.HasConnection ? Visibility.Collapsed : Visibility.Visible;
        EmptyTitle.Text = query.Length > 0 ? "검색 결과가 없습니다" : _state.HasConnection ? "표시할 항목이 없습니다" : "이 PC에서 메모를 시작하세요";
        EmptyAddButton.Content = _state.HasConnection ? "새 메모 / 일정" : "이 PC에 새 메모 / 일정";
        LocalModeHint.Visibility = _state.HasConnection ? Visibility.Collapsed : Visibility.Visible;
        AddButton.ToolTip = _state.HasConnection ? "새 메모 / 일정 · 기기에 저장 후 동기화 (Ctrl+N)" : "이 PC에 새 메모 / 일정 · 클라우드 연결 전에는 다른 PC와 공유되지 않습니다 (Ctrl+N)";
        EmptyText.Text = _state.HasConnection ? "새 항목을 입력하거나 완료 항목 표시를 켜 보세요."
            : "연결 전 메모는 이 PC에만 저장됩니다.\n다른 PC에서도 보려면 아래 클라우드 연결을 선택하세요.";
        UpdateHeaderContent(); UpdateScrollButtons();
    }

    private void UpdateHeaderContent()
    {
        if (TimeText is null || TitleBadge is null) return;
        double width = ActualWidth > 0 ? ActualWidth : Width;
        Resources["CardActionWidth"] = width < 260 ? 16.0 : 18.0;
        Resources["CardDateWidth"] = Math.Clamp((width - 100) * 0.4, 12, 110);
        // Reserve the title, M/S switch, roll and hide buttons even at the 180-DIP minimum.
        // Less frequent actions remain available through the menu and keyboard.
        foreach (var button in new[] { AddButton, RefreshButton, SearchToggleButton, RollToggleButton, HideButton })
            button.Width = width < 230 ? 18 : 20;
        AddButton.Visibility = width >= 200 ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.Visibility = width >= 225 ? Visibility.Visible : Visibility.Collapsed;
        SearchToggleButton.Visibility = width >= 250 ? Visibility.Visible : Visibility.Collapsed;
        var scrollVisibility = width >= 290 ? Visibility.Visible : Visibility.Collapsed;
        ScrollUpButton.Visibility = ScrollDownButton.Visibility = ScrollDivider.Visibility = scrollVisibility;
        TimeText.Visibility = width < 320 ? Visibility.Collapsed : Visibility.Visible;
        TimeText.Text = _busy ? "동기화 중" : !_state.HasConnection ? "기기 저장" : _state.PendingCount > 0 ? $"대기 {_state.PendingCount}"
            : _state.LastSynced is { } time ? time.ToLocalTime().ToString(width < 365 ? "HH:mm" : "MM.dd HH:mm") : "연결됨";
        string detail = _state.LastSynced is { } last ? $"최근 동기화 {last.ToLocalTime():yyyy.MM.dd HH:mm:ss}" : "동기화 기록 없음";
        if (_state.PendingCount > 0) detail += $"\n기기에 저장됨 · 미전송 변경 {_state.PendingCount}개";
        detail += _state.HasConnection ? "\n지금 동기화: F5 · 우클릭 → 지금 동기화" : "\n클라우드 연결 전: 이 PC에만 저장됩니다.";
        if (_state.HasConnection && _state.SyncIntervalSeconds > 0 && _nextSync != DateTimeOffset.MaxValue) detail += $"\n다음 자동 동기화 {_nextSync.ToLocalTime():HH:mm}";
        if (_syncError is not null) detail += "\n" + _syncError;
        TimeText.ToolTip = detail; TitleBadge.ToolTip = detail + "\n끌어서 이동 · 우클릭으로 설정";
        AutomationProperties.SetName(TimeText, detail);
        if (!_busy)
        {
            RefreshButton.ToolTip = "지금 동기화 (F5)";
            SyncMenuItem.ToolTip = RefreshButton.ToolTip;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy; if (_closed) return;
        RefreshButton.IsEnabled = SyncMenuItem.IsEnabled = !busy; if (_trayRefresh is not null) _trayRefresh.Enabled = !busy;
        RefreshButton.ToolTip = busy ? "동기화 중…" : "지금 동기화 (F5)";
        SyncMenuItem.ToolTip = RefreshButton.ToolTip;
        UpdateHeaderContent();
    }

    private void ScheduleNextSync()
    {
        _nextSync = SyncPolicy.NextAttempt(DateTimeOffset.UtcNow, _state.HasConnection, _state.SyncIntervalSeconds, _failures);
        UpdateHeaderContent();
    }

    private void RequestEditSync()
    {
        _editSyncRequests.Request();
        PumpEditSync();
    }

    private void PumpEditSync()
    {
        if (_closed || _editSyncScheduled || !_editSyncRequests.HasPending) return;
        _editSyncScheduled = true;
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            _editSyncScheduled = false;
            // Keep the request when a current sync, locked session or open editor blocks it.
            if (!_editSyncRequests.TryTake(!_busy && !_closed && !_suspended && !_sessionLocked
                && _editorWindow is null && _state.HasConnection)) return;
            await RefreshAsync(false);
        }));
    }

    private async Task RefreshAsync(bool manual = true)
    {
        if (_busy || _closed || _suspended || _sessionLocked) return;
        if (!_state.HasConnection) { if (manual) OpenSettings(); ScheduleNextSync(); return; }
        // This attempt also covers saved edits waiting behind a manual/periodic sync.
        _editSyncRequests.TryTake(true);
        SetBusy(true);
        bool succeeded = false;
        try
        {
            var outcome = await _sync.SyncAsync(_lifetime.Token);
            succeeded = true;
            _syncError = null; _failures = 0;
            ScheduleNextSync();
            if (outcome.Conflicts > 0 && manual) ShowNotice($"동시에 수정한 내용 {outcome.Conflicts}개를 별도 보관했습니다. ‘충돌 보관본’을 확인해 주세요.");
            CheckReminders();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _syncError = ex is MemoException ? ex.Message : "동기화하지 못했습니다. 메모와 미전송 변경은 기기에 유지됩니다.";
            _failures = Math.Min(_failures + 1, 5);
            ScheduleNextSync();
            if (manual) ShowNotice(_syncError, true);
        }
        finally
        {
            // SyncService can include edits saved during transmission in this same batch.
            if (succeeded)
            {
                if (_workspace.Snapshot().PendingCount == 0) _editSyncRequests.TryTake(true);
                else _editSyncRequests.Request();
            }
            SetBusy(false); PumpEditSync();
        }
    }

    private void OpenSettings() => OpenSettingsCore(false);

    private void OpenSettingsCore(bool backup)
    {
        if (_closed) return;
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        ShowWidget();
        _settingsWindow = new SettingsWindow(_state, ApplySettingsAsync, PreviewAppearance, GetMemoFontSize, _workspace.Snapshot, ImportBackup,
            step => _workspace.Update(next => next.SetupCompletedSteps = step)) { Owner = this };
        if (backup) _settingsWindow.SelectBackup();
        try { _settingsWindow.ShowDialog(); }
        finally { _settingsWindow = null; ApplyAppearance(); }
    }

    private int ImportBackup(MemoBackup backup)
    {
        int count = _workspace.Import(backup.Items, backup.Settings);
        if (backup.Settings is not null)
        {
            ApplyAppearance(); RenderNotes(); ScheduleNextSync();
        }
        return count;
    }

    private async Task<string?> ApplySettingsAsync(SettingsRequest request)
    {
        try
        {
            bool connectionChanged = false;
            if (request.RemoveConnection) await _sync.DisconnectAsync(_lifetime.Token);
            else if (!string.IsNullOrWhiteSpace(request.NewAddress) || !string.IsNullOrWhiteSpace(request.NewKey))
            {
                if (!string.IsNullOrWhiteSpace(request.NewAddress) && string.IsNullOrWhiteSpace(request.NewKey))
                    return "새 주소를 등록할 때는 연결 키도 함께 입력해 주세요.";
                string endpoint = string.IsNullOrWhiteSpace(request.NewAddress) ? _state.Endpoint ?? "" : request.NewAddress;
                string key = request.NewKey.Trim();
                await _sync.ConnectAsync(endpoint, key, false, _lifetime.Token, request.ExpectedDatasetId, count =>
                    MessageBox.Show(_settingsWindow ?? (Window)this,
                        $"다른 시트로 연결을 바꿉니다.\n현재 메모·일정 {count}개를 새 시트에 복사해 연결할까요?\n기존 시트의 내용은 유지합니다.",
                        "MyToDo2 · 시트 변경", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);
                connectionChanged = true;
            }
            _ = NoteApps.LaunchTarget(request.NoteApp);
            WindowsStartup.Registration.Save(request.StartWithWindows, Environment.ProcessPath, () => _workspace.Update(next =>
            {
                next.TemplateCopyUrl = null;
                if (connectionChanged) next.SetupCompletedSteps = 3;
                request.Appearance.ApplyTo(next); next.AlwaysOnTop = request.AlwaysOnTop; next.LockPosition = request.LockPosition;
                next.NoteApp = request.NoteApp;
                next.PreviewLines = request.PreviewLines; next.SyncIntervalSeconds = request.SyncIntervalSeconds;
                next.NotificationsEnabled = request.NotificationsEnabled;
                CaptureBounds(next);
            }));
            _failures = 0; ApplyAppearance(); RenderNotes(); ScheduleNextSync();
            // Saving new connection information is an explicit first-sync request, including manual-only mode.
            if (connectionChanged)
            {
                await RefreshAsync(false);
                if (_syncError is not null) return "연결 정보는 저장했지만 첫 동기화를 마치지 못했습니다. 메모는 기기에 유지됩니다.\n" + _syncError;
            }
            return null;
        }
        catch (MemoException ex) { return ex.Message; }
        catch (OperationCanceledException) { return "연결 확인이 취소되었습니다."; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return "설정을 저장하지 못했습니다. 메모는 보존되어 있습니다."; }
    }

    private static void OpenSheets()
    {
        try { Process.Start(new ProcessStartInfo(AppsScriptConnection.SheetsHome) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { MessageBox.Show("기본 웹 브라우저를 열지 못했습니다. Windows의 기본 앱 설정을 확인해 주세요.", "MyToDo2", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private void CopySelected() => CopyNote((NoteList.SelectedItem as MemoCard)?.Item);

    private bool CopyNote(MemoItem? note)
    {
        if (note is null) return false;
        try
        {
            Clipboard.SetText(note.Body);
            WidgetMenu.IsOpen = false;
            string app = NoteApps.Label(_state.NoteApp);
            MessageBox.Show(this, $"전체 본문을 복사했습니다.\n{app}에서 Ctrl+V로 붙여넣으세요.", "MyToDo2", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        { ShowNotice("클립보드가 사용 중입니다. 다시 시도해 주세요.", true); return false; }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        NoteList.UnselectAll();
        // Avoid reselecting the old item when this window regains logical focus.
        FocusManager.SetFocusedElement(this, null);
    }

    private void Widget_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // Capture before the popup opens; deactivation may clear the visible selection.
        _contextNote = (NoteList.SelectedItem as MemoCard)?.Item.Copy();
    }

    private void NoteList_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (CardContainer(e.OriginalSource as DependencyObject, ignoreActions: false) is { } item)
            item.IsSelected = true;
    }

    private void WidgetMenu_Opened(object sender, RoutedEventArgs e)
    {
        SettingsMenuItem.IsEnabled = true;
        CopyMenuItem.IsEnabled = EditMenuItem.IsEnabled = CompleteMenuItem.IsEnabled = MoveMenuItem.IsEnabled = DeleteMenuItem.IsEnabled = _contextNote is not null;
        CompleteMenuItem.Header = _contextNote?.IsCompleted == true ? "완료 취소" : "완료";
        ResetOrderMenuItem.IsEnabled = _state.ItemOrders.GetValueOrDefault(_state.ActiveTab)?.Count > 0;
        MoveMenuItem.Header = _contextNote?.Kind == MemoKind.Memo ? "일정 탭으로 이동" : "메모 탭으로 이동";
        CompletedFilterMenuItem.Header = _state.ShowCompleted ? "완료 항목 숨기기" : "완료 항목 표시";
        string name = NoteApps.Label(_state.NoteApp);
        NoteAppMenuItem.Header = _contextNote is null ? $"{name} 열기" : $"메모 복사 · {name} 열기";
    }

    private void WidgetMenu_Closed(object sender, RoutedEventArgs e)
    {
        // MenuItem.Click can follow popup closure; release the snapshot afterwards.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (!WidgetMenu.IsOpen) _contextNote = null;
        }));
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
        MinHeight = _state.IsRolledUp ? RolledUpHeight : WidgetHeight.Minimum;
        Height = _state.IsRolledUp ? RolledUpHeight : Math.Max(WidgetHeight.Minimum, _expandedHeight);
        if (!_state.IsRolledUp) RequestContentHeight();
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
        e.Handled = true;
        bool rolled = !_state.IsRolledUp;
        if (rolled)
        {
            _expandedHeight = Height;
            _rolledScrollOffset = FindScrollViewer(NoteList)?.VerticalOffset ?? 0;
        }
        try
        {
            _workspace.Update(next => next.IsRolledUp = rolled);
            ApplyRollState();
            if (!rolled)
            {
                DesktopAppearance.EnsureVisible(this);
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (!_closed && !_state.IsRolledUp && FindScrollViewer(NoteList) is { } scroll)
                        scroll.ScrollToVerticalOffset(Math.Clamp(_rolledScrollOffset, 0, scroll.ScrollableHeight));
                    UpdateScrollButtons();
                }));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ShowNotice("접힘 상태를 저장하지 못했습니다.", true); }
    }

    private void ShowNotice(string message, bool error = false)
    {
        if (_closed) return;
        // The header timestamp shows the last successful fetch, including after a failure.
        if (IsVisible && !_exit)
            MessageBox.Show(_settingsWindow ?? (Window)this, message, "MyToDo2", MessageBoxButton.OK,
                error ? MessageBoxImage.Warning : MessageBoxImage.Information);
        else _tray?.ShowBalloonTip(4000, "MyToDo2", message, error ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
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

    private void NoteList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateScrollButtons();
        if (Math.Abs(e.ExtentHeightChange) > 0.5 || Math.Abs(e.ViewportWidthChange) > 0.5) RequestContentHeight();
    }

    private void UpdateScrollButtons()
    {
        if (_closed) return;
        var scroll = FindScrollViewer(NoteList);
        if (_state.IsRolledUp || NoteList.Items.Count == 0 || scroll is null || scroll.ViewportHeight <= 0 || scroll.ScrollableHeight <= 0.5)
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
        if (edge.Contains('W'))
        {
            Width = Math.Clamp(_resizeBounds.Width - dx, MinWidth, MaxWidth);
            Left = _resizeBounds.Right - Width;
        }
        else if (edge.Contains('E')) Width = Math.Clamp(_resizeBounds.Width + dx, MinWidth, MaxWidth);
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
        RequestContentHeight();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_state.LockPosition) return;
        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node is not null)
        {
            if (node is ButtonBase) return; // Includes the scroll RepeatButtons in the header.
            if (node is Window) break;
            node = InteractionParent(node);
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
        RequestContentHeight();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _pointerMove is not null) { e.Handled = true; EndPointerMove(cancel: true); }
        else if (e.Key == Key.F5) { e.Handled = true; await RefreshAsync(); }
        else if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; ShowSearch(); }
        else if (e.Key == Key.N && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; OpenEditor(null); }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not ButtonBase && NoteList.SelectedItem is MemoCard card) { e.Handled = true; OpenEditor(card.Item); }
        else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.FocusedElement is not TextBoxBase) { e.Handled = true; CopySelected(); }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Settings_Click(object sender, RoutedEventArgs e) { WidgetMenu.IsOpen = false; OpenSettings(); }
    private void Sheets_Click(object sender, RoutedEventArgs e) { WidgetMenu.IsOpen = false; OpenSheets(); }
    private void Hide_Click(object sender, RoutedEventArgs e) => HideWidget();
    private void Copy_Click(object sender, RoutedEventArgs e) => CopyNote(_contextNote);

    private void NoteApp_Click(object sender, RoutedEventArgs e)
    {
        MemoItem? note = _contextNote;
        string target;
        try { target = NoteApps.LaunchTarget(_state.NoteApp); }
        catch (MemoException ex) { ShowNotice(ex.Message, true); return; }
        if (note is not null && !CopyNote(note)) return;
        WidgetMenu.IsOpen = false;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowNotice((note is null ? "" : "메모는 복사했습니다. ") +
                "메모 웹사이트를 열지 못했습니다. Windows의 기본 웹 브라우저 설정을 확인해 주세요.", true);
        }
    }

    private void ExitApp()
    {
        if (_editorWindow is not null)
        {
            _editorWindow.Close();
            if (_editorWindow is { IsVisible: true }) return;
        }
        _exit = true; Close();
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        EndPointerMove();
        if (!_exit) { e.Cancel = true; HideWidget(); return; }
        SaveWindowPosition();
    }
    private void Window_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _syncTimer.Stop(); _reminderTimer.Stop();
        SystemEvents.PowerModeChanged -= PowerMode_Changed; SystemEvents.SessionSwitch -= Session_Changed;
        _workspace.Changed -= Workspace_Changed;
        _reminderWindow?.Close(); _editorWindow?.Close();
        _lifetime.Cancel();
        _placementRecovery?.Dispose();
        _tray?.Dispose(); _trayIcon?.Dispose(); _client.Dispose();
        Application.Current.Shutdown();
    }

    private void ChangeLocal(Action<AppState> edit)
    {
        try { _workspace.Update(edit); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ShowNotice(ex is MemoException ? ex.Message : "변경 내용을 저장하지 못했습니다.", true); }
    }
    private void UpdateModeSelection()
    {
        bool updating = _updatingView;
        _updatingView = true;
        try
        {
            MemoModeButton.IsChecked = _state.ActiveTab == MemoKind.Memo;
            ScheduleModeButton.IsChecked = _state.ActiveTab == MemoKind.Schedule;
        }
        finally { _updatingView = updating; }
    }
    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_updatingView || sender is not RadioButton { Tag: string tag }) return;
        var tab = tag == "Schedule" ? MemoKind.Schedule : MemoKind.Memo;
        if (tab == _state.ActiveTab) return;
        double offset = _state.IsRolledUp ? _rolledScrollOffset : FindScrollViewer(NoteList)?.VerticalOffset ?? 0;
        ChangeLocal(next => { next.ScrollOffsets[next.ActiveTab] = offset; next.ActiveTab = tab; });
        // A failed local save must also restore the visible switch selection.
        UpdateModeSelection();
    }
    private void SearchToggle_Click(object sender, RoutedEventArgs e)
    {
        if (SearchPanel.Visibility != Visibility.Visible || _state.IsRolledUp) { ShowSearch(); return; }
        SearchPanel.Visibility = Visibility.Collapsed;
        RequestContentHeight();
    }
    private void SearchMenu_Click(object sender, RoutedEventArgs e) { WidgetMenu.IsOpen = false; ShowSearch(); }
    private void ShowSearch()
    {
        if (_state.IsRolledUp) RollToggle_Click(this, new RoutedEventArgs());
        if (_state.IsRolledUp) return;
        SearchPanel.Visibility = Visibility.Visible;
        SearchBox.Focus(); SearchBox.SelectAll();
        RequestContentHeight();
    }
    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updatingView || _visibleTab is null) return;
        ChangeLocal(next => next.SearchText = SearchBox.Text);
    }
    private void CompletedFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingView || _visibleTab is null) return;
        ChangeLocal(next => next.ShowCompleted = CompletedCheck.IsChecked == true);
    }
    private void CompletedFilter_Click(object sender, RoutedEventArgs e) => ChangeLocal(next => next.ShowCompleted = !next.ShowCompleted);
    private void BubbleExpand_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: MemoCard card }) return;
        var container = NoteList.ItemContainerGenerator.ContainerFromItem(card) as FrameworkElement;
        double? oldY = container?.TranslatePoint(new Point(0, 0), NoteList).Y;
        ChangeLocal(next => next.ItemViewLevels[card.Id] = card.NextViewLevel);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (oldY is not { } y || _closed || container is null || FindScrollViewer(NoteList) is not { } scroll) return;
            NoteList.UpdateLayout();
            RequestContentHeight();
            double difference = container.TranslatePoint(new Point(0, 0), NoteList).Y - y;
            scroll.ScrollToVerticalOffset(Math.Clamp(scroll.VerticalOffset + difference, 0, scroll.ScrollableHeight));
        }));
        e.Handled = true;
    }
    private void Tag_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: string tag }) return;
        ChangeLocal(next => next.SearchText = "#" + tag);
        SearchPanel.Visibility = Visibility.Visible;
        SearchBox.Focus();
    }
    private void OriginalLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MemoCard card } || !MemoItem.SafeWebUrl(card.Link)) return;
        try { Process.Start(new ProcessStartInfo(card.Link!) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ShowNotice("원문 링크를 열지 못했습니다.", true); }
        e.Handled = true;
    }
    private void OpenEditor(MemoItem? original)
    {
        if (_closed) return;
        if (_editorWindow is not null) { _editorWindow.Activate(); return; }
        ShowWidget();
        var latest = original is null ? new MemoItem { Kind = _state.ActiveTab }
            : _workspace.Snapshot().Items.FirstOrDefault(i => i.Id == original.Id && !i.IsDeleted);
        if (latest is null) { ShowNotice("이미 삭제된 항목입니다."); return; }
        long expected = latest.Version;
        bool savedInEditor = false;
        _editorWindow = new MemoEditorWindow(latest, draft =>
        {
            try { var saved = _workspace.SaveItem(draft, expected); savedInEditor = true; if (saved.Id != draft.Id) ShowNotice("최신 변경과 겹쳐 입력한 내용을 별도 보관본으로 저장했습니다."); return null; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return ex is MemoException ? ex.Message : "메모를 저장하지 못했습니다. 입력 내용은 유지됩니다."; }
        }, original is null, _activeAppearance?.TagTextColor ?? _state.TagTextColor) { Owner = this };
        try { _editorWindow.ShowDialog(); }
        finally { _editorWindow = null; if (savedInEditor) RequestEditSync(); else PumpEditSync(); }
    }
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApp();
    private void Add_Click(object sender, RoutedEventArgs e) => OpenEditor(null);
    private void Edit_Click(object sender, RoutedEventArgs e) { if (_contextNote is not null) OpenEditor(_contextNote); }
    private void BubbleEdit_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: MemoCard card }) OpenEditor(card.Item);
    }
    private void BubbleDelete_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: MemoCard card }) DeleteItem(card.Item);
    }
    private void BubbleComplete_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: MemoCard card }) return;
        var item = _state.Items.FirstOrDefault(i => i.Id == card.Id && !i.IsDeleted);
        if (item is not null) CommitAction(() => _workspace.SetCompleted(item.Id, !item.IsCompleted));
    }
    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        var item = _state.Items.FirstOrDefault(i => i.Id == _contextNote?.Id && !i.IsDeleted);
        if (item is null) return;
        CommitAction(() => _workspace.SetCompleted(item.Id, !item.IsCompleted));
    }
    private void MoveKind_Click(object sender, RoutedEventArgs e)
    {
        var item = _state.Items.FirstOrDefault(i => i.Id == _contextNote?.Id && !i.IsDeleted)?.Copy();
        if (item is null) return;
        item.Kind = item.Kind == MemoKind.Memo ? MemoKind.Schedule : MemoKind.Memo;
        CommitAction(() => _workspace.SaveItem(item, item.Version));
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_contextNote is not { } item) return;
        DeleteItem(item);
    }
    private void DeleteItem(MemoItem item)
    {
        if (MessageBox.Show(this, "위젯에서 이 항목을 숨길까요?\n연결된 시트에는 본문과 보관 상태가 남습니다. 연결 전에는 이 PC에 보관합니다.", "MyToDo2", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        CommitAction(() => _workspace.Delete(item.Id));
    }
    private void CommitAction(Action action)
    {
        try { action(); RequestEditSync(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ShowNotice(ex is MemoException ? ex.Message : "변경 내용을 저장하지 못했습니다.", true); }
    }
    private void Backup_Click(object sender, RoutedEventArgs e) { OpenSettingsCore(true); }
    private void PowerMode_Changed(object sender, PowerModeChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_closed) return; _suspended = e.Mode == PowerModes.Suspend;
            if (e.Mode == PowerModes.Resume) { CheckReminders(); PumpEditSync(); }
        }));
    }
    private void Session_Changed(object sender, SessionSwitchEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_closed) return;
            if (e.Reason == SessionSwitchReason.SessionLock) _sessionLocked = true;
            if (e.Reason == SessionSwitchReason.SessionUnlock) { _sessionLocked = false; CheckReminders(); PumpEditSync(); }
        }));
    }
    private void CheckReminders()
    {
        if (_closed || _sessionLocked || _suspended) return;
        var due = _workspace.DueReminders(DateTimeOffset.UtcNow);
        if (due.Count == 0) return;
        try
        {
            if (_reminderWindow is null)
            {
                _reminderWindow = new ReminderWindow(due, id => { var item = _state.Items.FirstOrDefault(i => i.Id == id && !i.IsDeleted); if (item is not null) OpenEditor(item); },
                    id => CommitAction(() => _workspace.SetCompleted(id, true)),
                    (id, until) => CommitAction(() => _workspace.Snooze(id, until)));
                _reminderWindow.Closed += (_, _) => _reminderWindow = null;
                _reminderWindow.Show();
            }
            else _reminderWindow.Add(due);
            _workspace.MarkRemindersDelivered(due);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { _syncError = "알림 표시 또는 기록을 저장하지 못했습니다."; UpdateHeaderContent(); }
    }

}
