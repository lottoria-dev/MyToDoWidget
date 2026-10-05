using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Interop;
using MyToDoWidget.Services;
using Forms = System.Windows.Forms;

namespace MyToDoWidget;

public sealed record SettingsRequest(string NewAddress, bool RemoveConnection, AppearanceOptions Appearance,
    bool AlwaysOnTop, bool LockPosition, bool RefreshOnStart, bool? StartWithWindows);

public partial class SettingsWindow : Window
{
    private const string OfficialDistributionUrl = "https://mathtime.kr/?page=mytodo";
    private readonly Func<SettingsRequest, Task<string?>> _apply;
    private readonly Action<AppearanceOptions> _preview;
    private readonly Func<AppearanceOptions, double> _displayFontSize;
    private readonly double _textReferenceWidth, _textReferenceHeight;
    private readonly bool _hasConnection;
    private readonly bool _widgetLightTheme;
    private bool _remove, _saving, _ready, _updatingColors;
    private string _backgroundColor, _textColor;
    private string? _glassColor;
    private int[]? _customColors;
    private sealed record Choice<T>(T Value, string Label);

    public SettingsWindow(AppState state, Func<SettingsRequest, Task<string?>> apply, Action<AppearanceOptions> preview,
        Func<AppearanceOptions, double> displayFontSize)
    {
        _apply = apply; _preview = preview;
        _displayFontSize = displayFontSize;
        _textReferenceWidth = state.TextReferenceWidth; _textReferenceHeight = state.TextReferenceHeight;
        _hasConnection = state.Source is not null;
        _widgetLightTheme = state.LightTheme;
        _backgroundColor = state.BubbleColor; _textColor = state.MemoTextColor;
        _glassColor = state.GlassColor;
        InitializeComponent();
        Theme.ApplySettings(Resources);
        AboutVersion.Text = $"버전 {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)}";
        if (BuildIdentity.IsTest) AboutVersion.Text += " · 검토용";
        OfficialAddressText.Text = OfficialDistributionUrl;
        // Deliberately never assign the stored source to any UI property.
        ConnectionStatus.Text = _hasConnection ? "주소 등록됨" : "주소 미등록";
        RemoveButton.IsEnabled = _hasConnection;
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
        AlignmentCombo.ItemsSource = new[]
        {
            new Choice<BubbleAlign>(BubbleAlign.Left, "왼쪽"),
            new Choice<BubbleAlign>(BubbleAlign.Center, "가운데"),
            new Choice<BubbleAlign>(BubbleAlign.Right, "오른쪽")
        };
        WidthModeCombo.ItemsSource = new[]
        {
            new Choice<BubbleWidthMode>(BubbleWidthMode.Content, "내용에 맞춤"),
            new Choice<BubbleWidthMode>(BubbleWidthMode.Widget, "위젯 폭에 맞춤")
        };
        AlignmentCombo.SelectedValue = state.BubbleAlignment ?? BubbleAlign.Left;
        WidthModeCombo.SelectedValue = state.BubbleWidth;
        GapSlider.Value = state.BubbleGap;
        ThemeList.ItemsSource = MemoAppearance.Presets;
        GlassThemeList.ItemsSource = MemoAppearance.GlassPresets;
        BackgroundHex.Text = _backgroundColor; TextHex.Text = _textColor;
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
        TopmostCheck.IsChecked = state.AlwaysOnTop;
        LockCheck.IsChecked = state.LockPosition;
        StartupCheck.IsChecked = state.RefreshOnStart;
        try { StartWithWindowsCheck.IsChecked = WindowsStartup.Registration.IsRegistered; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            StartWithWindowsCheck.IsEnabled = false;
            StartWithWindowsHint.Text = "자동 실행 상태를 읽지 못했습니다. 다른 설정은 저장할 수 있습니다.";
        }
        if (!_hasConnection) EditorPanel.SelectedItem = GuideTab;
        _ready = true;
        MatchTheme();
        SyncGlassEditor();
        UpdateLabels();
        PreviewAppearance();
        UpdateTabPresentation();
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
        AlignmentCombo.SelectedValue is BubbleAlign alignment ? alignment : BubbleAlign.Left,
        WidthModeCombo.SelectedValue is BubbleWidthMode widthMode ? widthMode : BubbleWidthMode.Content,
        (int)Math.Round(GapSlider.Value));

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
        SampleSurface.Background = MemoAppearance.Brush(MemoAppearance.GlassColor(appearance.GlassColor, appearance.LightTheme), appearance.BackgroundPercent);
        foreach (var bubble in new[] { SampleBubble, SampleBubbleNext })
        {
            bubble.Shape = appearance.Shape;
            bubble.TailSide = appearance.TailSide;
            bubble.HorizontalAlignment = MemoAppearance.Alignment(appearance);
            bubble.Fill = MemoAppearance.Brush(appearance.BubbleColor, appearance.BubbleBackgroundPercent);
            bubble.Stroke = MemoAppearance.Brush(appearance.TextColor, (int)Math.Round(appearance.BubbleBackgroundPercent * 0.18));
        }
        SampleBubble.Margin = new Thickness(0, 0, 0, appearance.BubbleGap);
        foreach (var text in new[] { SampleText, SampleTextNext })
        {
            text.Foreground = MemoAppearance.Brush(appearance.TextColor);
            text.FontFamily = MemoAppearance.Font(appearance.FontFamilyName);
            text.FontSize = _displayFontSize(appearance);
            text.FontWeight = appearance.Bold ? FontWeights.Bold : FontWeights.Normal;
            text.FontStyle = appearance.Italic ? FontStyles.Italic : FontStyles.Normal;
        }
        AlignmentCombo.IsEnabled = appearance.WidthMode == BubbleWidthMode.Content;
        LayoutHint.Text = appearance.WidthMode == BubbleWidthMode.Widget
            ? "모든 말풍선이 메모 영역의 폭을 채웁니다. 정렬 선택은 내용에 맞춤으로 돌아가면 적용됩니다."
            : "내용에 따라 폭이 달라지고 긴 메모는 줄바꿈합니다. 정렬과 꼬리 방향은 별개입니다.";
        FontScaleHint.Text = appearance.ScaleTextWithWindow
            ? $"현재 표시 크기 {SampleText.FontSize:0.#} · 창의 가로·세로 크기에 따라 변합니다."
            : $"고정 크기 {appearance.FontSize:0} · 창 크기를 바꿔도 글자 크기를 유지합니다.";
        BackgroundSwatch.Background = MemoAppearance.Brush(_backgroundColor);
        TextSwatch.Background = MemoAppearance.Brush(_textColor);
    }

    private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { UpdateLabels(); PreviewAppearance(); }
    private void Appearance_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        PreviewAppearance();
    }
    private void Choice_Changed(object sender, SelectionChangedEventArgs e) => PreviewAppearance();

    private void UpdateTabPresentation()
    {
        if (PreviewPanel is null || EditorPanel is null) return;
        PreviewPanel.Visibility = EditorPanel.SelectedItem == BubbleTab || EditorPanel.SelectedItem == GlassTab
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EditorPanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, EditorPanel)) UpdateTabPresentation();
    }

    private void GoToConnection_Click(object sender, RoutedEventArgs e)
    {
        EditorPanel.SelectedItem = ConnectionTab;
        NewAddressBox.Focus();
    }

    private void ShowGuide_Click(object sender, RoutedEventArgs e) => EditorPanel.SelectedItem = GuideTab;
    private void GuideSheets_Click(object sender, RoutedEventArgs e) => OpenBrowser(SheetSource.SheetsHome);
    private void OfficialPage_Click(object sender, RoutedEventArgs e) => OpenBrowser(OfficialDistributionUrl);

    private void OpenBrowser(string address)
    {
        try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { MessageBox.Show(this, "기본 웹 브라우저를 열지 못했습니다. Windows의 기본 앱 설정을 확인해 주세요.", "MyToDo", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "구글 시트 업로드용 템플릿 저장", FileName = "MyToDo_Template.csv",
            DefaultExt = ".csv", Filter = "CSV 파일 (*.csv)|*.csv", AddExtension = true, OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            using var template = typeof(SettingsWindow).Assembly.GetManifestResourceStream("MyToDoWidget.MyToDo_Template.csv")
                ?? throw new IOException("Template resource missing.");
            using (var file = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write)) template.CopyTo(file);
            GuideFeedback.Text = "템플릿을 저장했습니다. 구글 시트의 파일 → 가져오기 → 업로드에서 선택하세요.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { GuideFeedback.Text = "템플릿을 저장하지 못했습니다. 저장 위치와 쓰기 권한을 확인해 주세요."; }
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
        if (ConnectionStatus is null) return;
        if (NewAddressBox.Password.Length == 0)
        {
            ConnectionStatus.Text = _remove ? "저장 시 연결 해제" : _hasConnection ? "주소 등록됨" : "주소 미등록";
            return;
        }
        _remove = false;
        ConnectionStatus.Text = "새 주소 입력됨 · 저장 시 확인";
    }
    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = Clipboard.GetText();
            if (text.Length > 8192) { ErrorText.Text = "주소가 너무 깁니다. Google Sheets 공유 주소만 복사해 주세요."; return; }
            NewAddressBox.Password = text.Trim();
            NewAddressBox.Focus();
        }
        catch (System.Runtime.InteropServices.ExternalException) { ErrorText.Text = "클립보드를 읽지 못했습니다. 다시 붙여넣어 주세요."; }
    }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        NewAddressBox.Clear();
        _remove = true;
        ConnectionStatus.Text = "저장 시 연결 해제";
        ErrorText.Text = "저장하면 이 컴퓨터의 연결과 보관된 메모를 지웁니다. 구글 시트 원본은 유지됩니다.";
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
        if (!MemoStyle.TryHexColor(GlassHex.Text, out _))
        {
            ErrorText.Text = "유리판 배경색을 #RRGGBB 형식으로 확인해 주세요.";
            EditorPanel.SelectedItem = GlassTab;
            return;
        }
        var request = new SettingsRequest(NewAddressBox.Password, _remove, ReadAppearance(),
            TopmostCheck.IsChecked == true, LockCheck.IsChecked == true, StartupCheck.IsChecked == true,
            StartWithWindowsCheck.IsEnabled ? StartWithWindowsCheck.IsChecked == true : null);
        _saving = true;
        EditorPanel.IsEnabled = SaveButton.IsEnabled = CancelButton.IsEnabled = false;
        SaveButton.Content = "확인 중…";
        ErrorText.Text = "";
        try
        {
            string? error = await _apply(request);
            if (error is null)
            {
                NewAddressBox.Clear();
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
        else NewAddressBox.Clear();
    }
}
