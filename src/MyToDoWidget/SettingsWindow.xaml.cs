using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Interop;
using MyToDoWidget.Services;
using Forms = System.Windows.Forms;

namespace MyToDoWidget;

public sealed record SettingsRequest(string NewAddress, string NewKey, bool RemoveConnection, AppearanceOptions Appearance,
    bool AlwaysOnTop, bool LockPosition, bool? StartWithWindows,
    NoteApp NoteApp, int PreviewLines, int SyncIntervalSeconds, bool NotificationsEnabled, string? ExpectedDatasetId);

public partial class SettingsWindow : Window
{
    private readonly Func<AppState> _current;
    private readonly Func<MemoBackup, int> _import;
    private readonly Func<SettingsRequest, Task<string?>> _apply;
    private readonly Action<AppearanceOptions> _preview;
    private readonly Func<AppearanceOptions, double> _displayFontSize;
    private readonly double _textReferenceWidth, _textReferenceHeight;
    private readonly bool _hasConnection;
    private readonly Action<int> _saveSetupProgress;
    private readonly SetupGuideProgress _setup;
    private readonly bool _widgetLightTheme;
    private bool _remove, _saving, _ready, _updatingColors;
    private string _backgroundColor, _textColor, _tagColor, _urgentColor;
    private string? _glassColor;
    private int[]? _customColors;
    private sealed record Choice<T>(T Value, string Label);

    public SettingsWindow(AppState state, Func<SettingsRequest, Task<string?>> apply, Action<AppearanceOptions> preview,
        Func<AppearanceOptions, double> displayFontSize, Func<AppState> current, Func<MemoBackup, int> import, Action<int> saveSetupProgress)
    {
        _apply = apply; _preview = preview; _current = current; _import = import;
        _displayFontSize = displayFontSize;
        _textReferenceWidth = state.TextReferenceWidth; _textReferenceHeight = state.TextReferenceHeight;
        _hasConnection = state.HasConnection;
        _saveSetupProgress = saveSetupProgress;
        _setup = new SetupGuideProgress(state.SetupCompletedSteps, state.HasConnection);
        _widgetLightTheme = state.LightTheme;
        _backgroundColor = state.BubbleColor; _textColor = state.MemoTextColor;
        _tagColor = state.TagTextColor; _urgentColor = state.UrgentColor;
        _glassColor = state.GlassColor;
        InitializeComponent();
        Theme.ApplySettings(Resources);
        _ = new DetachedWindowMovement(this);
        AboutVersion.Text = "Version " + BuildIdentity.Version;
        ExistingCodeRadio.IsChecked = _setup.UseExistingCode;
        NewSetupRadio.IsChecked = !_setup.UseExistingCode;
        RenderSetupGuide();
        ConnectionStatus.Text = _hasConnection ? "시트 연결됨 · 코드는 등록 여부만 표시" : "처음 연결 · 아래 네 단계만 진행하세요";
        RemoveButton.IsEnabled = _hasConnection;
        PreviewLinesCombo.SelectedValue = state.PreviewLines.ToString();
        SyncIntervalCombo.ItemsSource = SyncPolicy.PresetSeconds.Select(v => new Choice<int>(v, v == 0 ? "정기 동기화 수동" : v == SyncPolicy.DefaultIntervalSeconds ? "30분 (기본·권장)" : $"{v / 60}분")).ToList();
        SyncIntervalCombo.SelectedValue = SyncPolicy.NormalizeInterval(state.SyncIntervalSeconds);
        NotificationsCheck.IsChecked = state.NotificationsEnabled;
        ShapeCombo.ItemsSource = new[]
        {
            new Choice<BubbleShape>(BubbleShape.Rounded, "둥근 사각형"),
            new Choice<BubbleShape>(BubbleShape.Square, "사각형"),
            new Choice<BubbleShape>(BubbleShape.Droplet, "물방울형"),
            new Choice<BubbleShape>(BubbleShape.Capsule, "캡슐형"),
            new Choice<BubbleShape>(BubbleShape.CutCorner, "모서리 컷")
        };
        TailCombo.ItemsSource = new[]
        {
            new Choice<BubbleTail>(BubbleTail.Left, "왼쪽"),
            new Choice<BubbleTail>(BubbleTail.Right, "오른쪽"),
            new Choice<BubbleTail>(BubbleTail.None, "없음")
        };
        ShapeCombo.SelectedValue = state.BubbleShape;
        TailCombo.SelectedValue = state.TailSide;
        GapSlider.Value = state.BubbleGap;
        ThemeList.ItemsSource = MemoAppearance.Presets;
        GlassThemeList.ItemsSource = MemoAppearance.GlassPresets;
        BackgroundHex.Text = _backgroundColor; TextHex.Text = _textColor;
        TagHex.Text = _tagColor; UrgentHex.Text = _urgentColor;
        FontCombo.ItemsSource = new[] { new FontOption(state.MemoFontFamily, state.MemoFontFamily, "현재 설정", 0) };
        FontCombo.SelectedValue = state.MemoFontFamily;
        FontCombo.IsEnabled = false;
        FontHint.Text = "설치된 글꼴을 확인하는 중…";
        FontSlider.Value = state.FontSize;
        ScaleTextCheck.IsChecked = state.ScaleTextWithWindow;
        GlassOpacitySlider.Value = state.BackgroundPercent;
        HeaderOpacitySlider.Value = state.HeaderBackgroundPercent;
        BorderOpacitySlider.Value = state.OuterBorderPercent;
        BubbleOpacitySlider.Value = state.BubbleBackgroundPercent;
        BoldCheck.IsChecked = state.MemoBold;
        ItalicCheck.IsChecked = state.MemoItalic;
        GlassCheck.IsChecked = state.GlassEffect;
        UrgentCheck.IsChecked = state.UrgentHighlight;
        UrgentDaysCombo.ItemsSource = new[] { 0, 1, 3, 7, 14, 30 }.Append(state.UrgentDays).Distinct().Order().Select(v => new Choice<int>(v, v == 0 ? "오늘만" : v + "일 이내")).ToList();
        UrgentDaysCombo.SelectedValue = state.UrgentDays;
        TopmostCheck.IsChecked = state.AlwaysOnTop;
        LockCheck.IsChecked = state.LockPosition;
        NoteAppCombo.ItemsSource = NoteApps.Choices;
        NoteAppCombo.SelectedValue = state.NoteApp;
        try { StartWithWindowsCheck.IsChecked = WindowsStartup.Registration.IsRegistered; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            StartWithWindowsCheck.IsEnabled = false;
            StartWithWindowsHint.Visibility = Visibility.Visible;
            StartWithWindowsHint.Text = "자동 실행 상태를 읽지 못했습니다. 다른 설정은 저장할 수 있습니다.";
        }
        EditorPanel.SelectedItem = _hasConnection ? BubbleTab : GuideTab;
        _ready = true;
        MatchTheme();
        SyncGlassEditor();
        UpdateLabels();
        PreviewAppearance();
        Loaded += async (_, _) =>
        {
            var screen = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
            var dpi = VisualTreeHelper.GetDpi(this);
            double availableHeight = Math.Max(300, screen.WorkingArea.Height / dpi.DpiScaleY - 24);
            MinHeight = Math.Min(MinHeight, availableHeight);
            MaxHeight = availableHeight;
            Height = Math.Min(Height, MaxHeight);
            await Dispatcher.InvokeAsync(() =>
            {
                if (IsVisible && Owner is { } owner) DesktopAppearance.PlaceSettingsBeside(this, owner);
            });
            await LoadFontsAsync(state.MemoFontFamily);
        };
    }

    private async Task LoadFontsAsync(string savedFamily)
    {
        try
        {
            var fonts = await Task.Run(() => FontCatalog.Choices(savedFamily));
            if (!IsVisible) return;
            var view = new ListCollectionView(fonts.ToList());
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FontOption.Group)));
            _ready = false;
            try
            {
                FontCombo.ItemsSource = view;
                FontCombo.SelectedValue = fonts.First(f => string.Equals(f.FamilyName, savedFamily, StringComparison.OrdinalIgnoreCase)).FamilyName;
            }
            finally { _ready = true; }
            FontHint.Text = "한글 고정폭 추천 → 한글 지원 → 영문 중심 순서입니다. 영문 중심 글꼴의 한글은 대체 글꼴로 표시됩니다.";
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or InvalidOperationException
            or NotSupportedException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            if (IsVisible) FontHint.Text = "글꼴 목록을 읽지 못했습니다. 현재 글꼴을 유지합니다.";
        }
        finally { if (IsVisible) FontCombo.IsEnabled = true; }
    }

    private AppearanceOptions ReadAppearance() => new(FontSlider.Value, (int)Math.Round(GlassOpacitySlider.Value),
        _widgetLightTheme, GlassCheck.IsChecked == true,
        TailCombo.SelectedValue is BubbleTail tail ? tail : BubbleTail.Left,
        ShapeCombo.SelectedValue is BubbleShape shape ? shape : BubbleShape.Rounded,
        _backgroundColor, (int)Math.Round(BubbleOpacitySlider.Value), _textColor,
        FontCombo.SelectedValue as string ?? MemoStyle.DefaultFont, BoldCheck.IsChecked == true, ItalicCheck.IsChecked == true, _glassColor,
        ScaleTextCheck.IsChecked == true, _textReferenceWidth, _textReferenceHeight,
        (int)Math.Round(HeaderOpacitySlider.Value), (int)Math.Round(BorderOpacitySlider.Value),
        BubbleAlign.Left, BubbleWidthMode.Widget, (int)Math.Round(GapSlider.Value), _tagColor,
        UrgentCheck.IsChecked == true, UrgentDaysCombo.SelectedValue is int days ? days : 3, _urgentColor);

    private void UpdateLabels()
    {
        if (!_ready) return;
        FontValue.Text = $"{FontSlider.Value:0}";
        GlassOpacityValue.Text = $"{GlassOpacitySlider.Value:0}%";
        BubbleOpacityValue.Text = $"{BubbleOpacitySlider.Value:0}%";
        HeaderOpacityValue.Text = $"{HeaderOpacitySlider.Value:0}%";
        BorderOpacityValue.Text = $"{BorderOpacitySlider.Value:0}%";
        GapValue.Text = $"{GapSlider.Value:0}";
    }

    private void PreviewAppearance()
    {
        if (!_ready || _saving || _updatingColors) return;
        var appearance = ReadAppearance();
        _preview(appearance);
        FontScaleHint.Text = appearance.ScaleTextWithWindow
            ? $"현재 표시 크기 {_displayFontSize(appearance):0.#} · 창의 가로·세로 크기에 따라 변합니다."
            : $"고정 크기 {appearance.FontSize:0} · 창 크기를 바꿔도 글자 크기를 유지합니다.";
        BackgroundSwatch.Background = MemoAppearance.Brush(_backgroundColor);
        TextSwatch.Background = MemoAppearance.Brush(_textColor);
        TagSwatch.Background = MemoAppearance.Brush(_tagColor);
        UrgentSwatch.Background = MemoAppearance.Brush(_urgentColor);
        UrgentDaysCombo.IsEnabled = UrgentHex.IsEnabled = UrgentCheck.IsChecked == true;
    }

    private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { UpdateLabels(); PreviewAppearance(); }
    private void Appearance_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        PreviewAppearance();
    }
    private void Choice_Changed(object sender, SelectionChangedEventArgs e) => PreviewAppearance();

    private void Settings_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!BuildIdentity.IsTest || e.Key != System.Windows.Input.Key.F12 || _saving) return;
        DeveloperTab.Visibility = Visibility.Visible;
        EditorPanel.SelectedItem = DeveloperTab; e.Handled = true;
    }
    private void RenderSetupGuide()
    {
        SetupStepsPanel.Visibility = _setup.UseExistingCode ? Visibility.Collapsed : Visibility.Visible;
        SetupProgressText.Text = _setup.UseExistingCode
            ? "보관한 전용 코드를 바로 연결합니다. 새 시트를 만들 필요가 없습니다."
            : $"현재 {_setup.ActiveStep}/4단계 · 브라우저에서 작업을 마친 뒤 완료 버튼을 누르세요.";
        var steps = new[] { SetupStep1, SetupStep2, SetupStep3 };
        var labels = new[] { SetupStep1State, SetupStep2State, SetupStep3State };
        for (int i = 0; i < steps.Length; i++)
        {
            bool enabled = _setup.IsStepEnabled(i + 1);
            steps[i].IsEnabled = enabled;
            steps[i].Opacity = enabled ? 1 : 0.65;
            steps[i].IsExpanded = _setup.ActiveStep == i + 1;
            labels[i].Text = _setup.CompletedSteps > i ? "완료 · 다시 보기" : enabled ? "진행 중" : "이전 단계 대기";
        }
        ConfirmSheetButton.IsEnabled = _setup.CanConfirmSheet;
        CopyInstallerButton.IsEnabled = _setup.CanCopyScript;
        ConfirmInstallButton.IsEnabled = _setup.CanConfirmInstall;
        ConfirmDeployButton.IsEnabled = _setup.CanConfirmDeploy;
        ConnectionEntryPanel.IsEnabled = _setup.CanEnterConnection;
        ConnectionCodeHelp.IsExpanded = !_setup.UseExistingCode;
        SetupConnectionHeader.Text = _setup.UseExistingCode ? "전용 코드로 연결" : "4  전용 코드 만들고 연결";
        UpdateConnectButton();
    }
    private void UpdateConnectButton()
    {
        if (ConnectButton is null) return;
        bool valid = false;
        try { _ = TemplateConnectionCode.Parse(ConnectionCodeBox.Password); valid = true; }
        catch (MemoException) { }
        ConnectButton.IsEnabled = !_saving && _setup.CanEnterConnection && valid;
    }
    private void GuideMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _setup.UseExistingCode = ExistingCodeRadio.IsChecked == true;
        GuideFeedback.Text = "";
        RenderSetupGuide();
    }
    private void NewSheet_Click(object sender, RoutedEventArgs e)
    {
        if (!OpenBrowser(InstallerScript.NewSheetUrl)) return;
        _setup.NoteSheetPageOpened();
        RenderSetupGuide();
        GuideFeedback.Text = "브라우저에 내 새 시트가 열리면 ‘시트가 열렸어요’를 누르세요.";
    }
    private void CopyInstaller_Click(object sender, RoutedEventArgs e)
    {
        if (!_setup.CanCopyScript) return;
        try
        {
            Clipboard.SetText(InstallerScript.Read());
            _setup.NoteScriptCopied();
            RenderSetupGuide();
            GuideFeedback.Text = "설치 코드를 복사했습니다. Apps Script의 Code.gs 전체를 바꾸어 붙여넣고 저장하세요.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { ErrorText.Text = "설치 코드를 복사하지 못했습니다. 다시 눌러 주세요."; }
    }
    private void SaveSetupStep()
    {
        try { _saveSetupProgress(_setup.CompletedSteps); ErrorText.Text = ""; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { ErrorText.Text = "진행 상태를 저장하지 못했습니다. 작업 내용은 유지됩니다. 완료 버튼을 다시 눌러 주세요."; }
        GuideFeedback.Text = "";
        RenderSetupGuide();
        FrameworkElement target = _setup.ActiveStep switch { 1 => SetupStep1, 2 => SetupStep2, 3 => SetupStep3, _ => ConnectionEntryPanel };
        target.UpdateLayout(); target.BringIntoView();
    }
    private void ConfirmSheet_Click(object sender, RoutedEventArgs e) { if (_setup.ConfirmSheet()) SaveSetupStep(); }
    private void ConfirmInstall_Click(object sender, RoutedEventArgs e) { if (_setup.ConfirmInstall()) SaveSetupStep(); }
    private void ConfirmDeploy_Click(object sender, RoutedEventArgs e) { if (_setup.ConfirmDeploy()) SaveSetupStep(); }
    private void ResetGuide_Click(object sender, RoutedEventArgs e)
    {
        _setup.Reset();
        NewSetupRadio.IsChecked = true;
        SaveSetupStep();
        GuideFeedback.Text = "안내 진행만 처음으로 돌렸습니다. 메모와 연결정보는 그대로 유지됩니다.";
    }
    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (!_setup.CanEnterConnection) return;
        if (string.IsNullOrWhiteSpace(ConnectionCodeBox.Password))
        { ErrorText.Text = "전용 코드를 붙여넣으세요."; ConnectionCodeBox.Focus(); return; }
        Save_Click(sender, e);
    }
    private void GoToConnection_Click(object sender, RoutedEventArgs e)
    {
        EditorPanel.SelectedItem = GuideTab;
        ConnectionCodeBox.Focus();
    }

    private void SettingsResize_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        Width = Math.Clamp(ActualWidth + e.HorizontalChange, MinWidth, double.IsFinite(MaxWidth) ? MaxWidth : 2000);
        Height = Math.Clamp(ActualHeight + e.VerticalChange, MinHeight, MaxHeight);
        e.Handled = true;
    }
    private void AboutWebsite_Click(object sender, RoutedEventArgs e) => OpenBrowser("https://mathtime.kr/mytodo.html");
    private void AboutEmail_Click(object sender, RoutedEventArgs e) => OpenBrowser("mailto:mathtime.ai@gmail.com");

    private void ShowGuide_Click(object sender, RoutedEventArgs e) => EditorPanel.SelectedItem = GuideTab;
    private void GuideSheets_Click(object sender, RoutedEventArgs e) => OpenBrowser(AppsScriptConnection.SheetsHome);

    private bool OpenBrowser(string address)
    {
        try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); return true; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { MessageBox.Show(this, "링크를 열지 못했습니다. Windows의 기본 앱 설정을 확인해 주세요.", "MyToDo2", MessageBoxButton.OK, MessageBoxImage.Information); return false; }
    }

    private void SetConnectionInput(string value)
    {
        if (!_setup.CanEnterConnection) throw new MemoException("앞 단계의 작업을 마치거나 ‘전용 코드가 있어요’를 선택하세요.");
        _ = TemplateConnectionCode.Parse(value);
        NewAddressBox.Clear(); NewKeyBox.Clear();
        ConnectionCodeBox.Password = value.Trim(); _remove = false;
        ConnectionStatus.Text = "전용 코드 입력됨 · 연결하기를 누르세요";
        ErrorText.Text = "코드를 입력했습니다. 연결하기를 누르세요.";
    }
    private void PasteConnection_Click(object sender, RoutedEventArgs e)
    {
        try { SetConnectionInput(Clipboard.GetText()); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { ErrorText.Text = ex is MemoException ? ex.Message : "클립보드를 읽지 못했습니다."; }
    }
    private void LoadConnectionFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "MyToDo2 연결 정보 불러오기",
            Filter = "MyToDo2 연결 정보 (*.mytodo-connection)|*.mytodo-connection|텍스트 (*.txt)|*.txt", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 4096) throw new MemoException("연결 정보 파일이 너무 큽니다.");
            SetConnectionInput(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { ErrorText.Text = ex is MemoException ? ex.Message : "연결 정보 파일을 읽지 못했습니다."; }
    }
    private void ConnectionCode_Changed(object sender, RoutedEventArgs e)
    {
        UpdateConnectButton();
        if (ConnectionStatus is null) return;
        if (ConnectionCodeBox.Password.Length > 0)
        { _remove = false; ConnectionStatus.Text = "새 연결 정보 입력됨 · 저장 시 확인"; }
        else Address_Changed(sender, e);
        UpdateAddressComparison();
    }
    public void SelectBackup() => EditorPanel.SelectedItem = BackupTab;

    private void ExportJson_Click(object sender, RoutedEventArgs e) => ExportBackup(false);
    private void ExportCsv_Click(object sender, RoutedEventArgs e) => ExportBackup(true);
    private void ExportBackup(bool csv)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "메모 백업 저장", FileName = $"MyToDo2-{DateTime.Now:yyyyMMdd-HHmm}.{(csv ? "csv" : "json")}",
            Filter = csv ? "CSV (*.csv)|*.csv" : "JSON (*.json)|*.json", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var state = _current(); string text = csv ? MemoBackup.ExportCsv(state.Items) : MemoBackup.ExportJson(state);
            File.WriteAllText(dialog.FileName, text, new System.Text.UTF8Encoding(csv));
            BackupFeedback.Text = "저장했습니다. 연결 키는 포함하지 않았습니다.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { BackupFeedback.Text = "백업을 저장하지 못했습니다. 저장 위치와 권한을 확인해 주세요."; }
    }
    private void ImportJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "MyToDo2 백업 가져오기", Filter = "JSON (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 64 * 1024 * 1024) throw new MemoException("백업 파일이 너무 큽니다.");
            var backup = MemoBackup.ReadDocument(File.ReadAllText(dialog.FileName));
            int count = _import(backup);
            if (backup.Settings is not null)
            {
                MessageBox.Show(this, $"메모·일정 {count}개를 불러오고 위젯 설정을 복원했습니다.", "MyToDo2", MessageBoxButton.OK, MessageBoxImage.Information);
                // Close the old editor so its unsaved controls cannot overwrite restored preferences.
                DialogResult = true;
            }
            else BackupFeedback.Text = $"{count}개를 추가했습니다. 같은 내용은 건너뛰었습니다.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { BackupFeedback.Text = ex is MemoException ? ex.Message : "백업을 가져오지 못했습니다. 기존 메모는 유지됩니다."; }
    }

    private void MatchTheme()
    {
        _updatingColors = true;
        ThemeList.SelectedItem = MemoAppearance.Presets.FirstOrDefault(t => t.Background == _backgroundColor && t.Foreground == _textColor);
        _updatingColors = false;
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updatingColors || ThemeList.SelectedItem is not PastelTheme theme) return;
        _updatingColors = true;
        _backgroundColor = theme.Background; _textColor = theme.Foreground;
        BackgroundHex.Text = _backgroundColor; TextHex.Text = _textColor;
        _updatingColors = false;
        ErrorText.Text = "";
        PreviewAppearance();
    }

    private void ColorText_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _updatingColors) return;
        if (!MemoStyle.TryHexColor(BackgroundHex.Text, out var background) || !MemoStyle.TryHexColor(TextHex.Text, out var text))
        {
            ErrorText.Text = "색상은 #RRGGBB 형식의 6자리 16진수로 입력하세요.";
            return;
        }
        _backgroundColor = background; _textColor = text;
        ErrorText.Text = "";
        MatchTheme();
        PreviewAppearance();
    }

    private sealed class DialogOwner(IntPtr handle) : Forms.IWin32Window { public IntPtr Handle => handle; }
    private void PickColor(TextBox target, string current)
    {
        var color = (Color)ColorConverter.ConvertFromString(current);
        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true, AnyColor = true,
            Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B)
        };
        if (_customColors is not null) dialog.CustomColors = _customColors;
        if (dialog.ShowDialog(new DialogOwner(new WindowInteropHelper(this).Handle)) == Forms.DialogResult.OK)
            target.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        _customColors = dialog.CustomColors;
    }
    private void BackgroundColor_Click(object sender, RoutedEventArgs e) => PickColor(BackgroundHex, _backgroundColor);
    private void TextColor_Click(object sender, RoutedEventArgs e) => PickColor(TextHex, _textColor);
    private void TagColor_Click(object sender, RoutedEventArgs e) => PickColor(TagHex, _tagColor);
    private void UrgentColor_Click(object sender, RoutedEventArgs e) => PickColor(UrgentHex, _urgentColor);
    private void ExtraColor_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _updatingColors) return;
        if (!MemoStyle.TryHexColor(TagHex.Text, out var tag) || !MemoStyle.TryHexColor(UrgentHex.Text, out var urgent))
        { ErrorText.Text = "색상은 #RRGGBB 형식으로 입력하세요."; return; }
        _tagColor = tag; _urgentColor = urgent; ErrorText.Text = ""; PreviewAppearance();
    }
    private void GlassColor_Click(object sender, RoutedEventArgs e) =>
        PickColor(GlassHex, MemoAppearance.GlassColor(_glassColor, _widgetLightTheme));

    private void SyncGlassEditor()
    {
        _updatingColors = true;
        try
        {
            string color = MemoAppearance.GlassColor(_glassColor, _widgetLightTheme);
            GlassHex.Text = color;
            GlassSwatch.Background = MemoAppearance.Brush(color);
            GlassThemeList.SelectedItem = MemoAppearance.GlassPresets.FirstOrDefault(t => t.Background == color);
            GlassColorHint.Text = _glassColor is null ? "유리판 기본색 사용 중 · 색상 칸을 누르면 직접 선택합니다."
                : "사용자 색상 사용 중 · 직접 선택한 유리판 색상을 저장합니다.";
        }
        finally { _updatingColors = false; }
    }

    private void GlassTheme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updatingColors || GlassThemeList.SelectedItem is not PastelTheme theme) return;
        _glassColor = theme.Background;
        SyncGlassEditor();
        ErrorText.Text = "";
        PreviewAppearance();
    }

    private void GlassColorText_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _updatingColors) return;
        if (!MemoStyle.TryHexColor(GlassHex.Text, out var color))
        {
            ErrorText.Text = "유리판 색상은 #RRGGBB 형식의 6자리 16진수로 입력하세요.";
            return;
        }
        _glassColor = color;
        _updatingColors = true;
        try { GlassThemeList.SelectedItem = MemoAppearance.GlassPresets.FirstOrDefault(t => t.Background == color); }
        finally { _updatingColors = false; }
        GlassSwatch.Background = MemoAppearance.Brush(color);
        GlassColorHint.Text = "사용자 색상 사용 중 · 직접 선택한 유리판 색상을 저장합니다.";
        ErrorText.Text = "";
        PreviewAppearance();
    }

    private void GlassReset_Click(object sender, RoutedEventArgs e)
    {
        _glassColor = null;
        SyncGlassEditor();
        ErrorText.Text = "";
        PreviewAppearance();
    }

    private void Address_Changed(object sender, RoutedEventArgs e)
    {
        if (ConnectionStatus is null || NewKeyBox is null) return;
        if ((ConnectionCodeBox?.Password.Length ?? 0) > 0 || NewAddressBox.Password.Length > 0 || NewKeyBox.Password.Length > 0)
        { _remove = false; ConnectionStatus.Text = "새 연결 정보 입력됨 · 저장 시 확인"; }
        else ConnectionStatus.Text = _remove ? "저장 시 이 PC 연결 해제" : _hasConnection ? "주소 · 키 등록됨" : "연결 미등록";
        UpdateAddressComparison();
    }
    private void UpdateAddressComparison()
    {
        if (AddressMatchStatus is null || ConnectionCodeBox is null || NewAddressBox is null) return;
        AddressMatchStatus.Text = "";
        if (string.IsNullOrWhiteSpace(ConnectionCodeBox.Password) || string.IsNullOrWhiteSpace(NewAddressBox.Password)) return;
        try
        {
            var original = TemplateConnectionCode.Parse(ConnectionCodeBox.Password);
            var corrected = TemplateConnectionCode.ReplaceEndpoint(ConnectionCodeBox.Password, NewAddressBox.Password);
            AddressMatchStatus.Text = original.Endpoint == corrected.Endpoint
                ? "주소가 같습니다. 저장하면 이 주소로 연결을 확인합니다."
                : "연결 정보의 주소와 다릅니다. 저장하면 새 주소로 확인하며 기존 키와 저장소 ID는 유지합니다.";
        }
        catch (MemoException) { AddressMatchStatus.Text = "연결 정보와 배포 주소의 입력 형식을 확인해 주세요."; }
    }
    private void PasteInto(PasswordBox target, int max)
    {
        try { var text = Clipboard.GetText().Trim(); if (text.Length > max) throw new MemoException("입력 값이 너무 깁니다."); target.Password = text; target.Focus(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ErrorText.Text = ex is MemoException ? ex.Message : "클립보드를 읽지 못했습니다."; }
    }
    private void Paste_Click(object sender, RoutedEventArgs e) => PasteInto(NewAddressBox, 8192);
    private void PasteKey_Click(object sender, RoutedEventArgs e) => PasteInto(NewKeyBox, 128);
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        ConnectionCodeBox.Clear(); NewAddressBox.Clear(); NewKeyBox.Clear(); _remove = true;
        ConnectionStatus.Text = "저장 시 이 PC 연결 해제";
        ErrorText.Text = "메모와 미전송 내용은 기기에 유지합니다. 원격 시트와 다른 PC의 연결은 바꾸지 않습니다.";
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saving) return;
        if (!MemoStyle.TryHexColor(BackgroundHex.Text, out var background) || !MemoStyle.TryHexColor(TextHex.Text, out var text))
        {
            ErrorText.Text = "배경색과 글자색을 #RRGGBB 형식으로 확인해 주세요.";
            EditorPanel.SelectedItem = BubbleTab;
            return;
        }
        _backgroundColor = background; _textColor = text;
        if (!MemoStyle.TryHexColor(TagHex.Text, out var tag) || !MemoStyle.TryHexColor(UrgentHex.Text, out var urgent))
        { ErrorText.Text = "태그색과 임박 일정 배경색을 확인하세요."; EditorPanel.SelectedItem = BubbleTab; return; }
        _tagColor = tag; _urgentColor = urgent;
        if (!MemoStyle.TryHexColor(GlassHex.Text, out _))
        {
            ErrorText.Text = "유리판 배경색을 #RRGGBB 형식으로 확인해 주세요.";
            EditorPanel.SelectedItem = GlassTab;
            return;
        }
        NoteApp noteApp = NoteAppCombo.SelectedValue is NoteApp selectedApp ? selectedApp : NoteApp.GoogleKeep;
        try { _ = NoteApps.LaunchTarget(noteApp); }
        catch (MemoException ex)
        {
            ErrorText.Text = ex.Message;
            EditorPanel.SelectedItem = ConnectionTab;
            return;
        }
        string address = NewAddressBox.Password, key = NewKeyBox.Password;
        string? expectedDataset = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(ConnectionCodeBox.Password))
            {
                if (!_setup.CanEnterConnection) throw new MemoException("앞 단계를 마치거나 ‘전용 코드가 있어요’를 선택하세요.");
                if (!string.IsNullOrWhiteSpace(key))
                    throw new MemoException("연결 정보를 입력했다면 개별 연결 키 칸은 비워 주세요. 배포 주소만 함께 입력할 수 있습니다.");
                var connection = string.IsNullOrWhiteSpace(address)
                    ? TemplateConnectionCode.Parse(ConnectionCodeBox.Password)
                    : TemplateConnectionCode.ReplaceEndpoint(ConnectionCodeBox.Password, address);
                address = connection.Endpoint; key = connection.Key; expectedDataset = connection.DatasetId;
            }
        }
        catch (MemoException ex)
        {
            ErrorText.Text = ex.Message; EditorPanel.SelectedItem = GuideTab; return;
        }
        var request = new SettingsRequest(address, key, _remove, ReadAppearance(),
            TopmostCheck.IsChecked == true, LockCheck.IsChecked == true,
            StartWithWindowsCheck.IsEnabled ? StartWithWindowsCheck.IsChecked == true : null,
            noteApp, int.TryParse(PreviewLinesCombo.SelectedValue as string, out int level) ? Math.Clamp(level, 1, 3) : 2,
            SyncIntervalCombo.SelectedValue is int interval ? interval : SyncPolicy.DefaultIntervalSeconds, NotificationsCheck.IsChecked == true, expectedDataset);
        _saving = true;
        EditorPanel.IsEnabled = SaveButton.IsEnabled = CancelButton.IsEnabled = false;
        SaveButton.Content = "확인 중…";
        ErrorText.Text = "";
        try
        {
            string? error = await _apply(request);
            if (error is null)
            {
                ConnectionCodeBox.Clear(); NewAddressBox.Clear(); NewKeyBox.Clear();
                _saving = false;
                DialogResult = true;
            }
            else ErrorText.Text = error;
        }
        finally
        {
            _saving = false;
            EditorPanel.IsEnabled = SaveButton.IsEnabled = CancelButton.IsEnabled = true;
            SaveButton.Content = "저장";
        }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_saving) e.Cancel = true;
        else { ConnectionCodeBox.Clear(); NewAddressBox.Clear(); NewKeyBox.Clear(); }
    }
}
