using System.ComponentModel;
using System.Windows.Interop;
using MyToDoWidget.Services;
using Forms = System.Windows.Forms;

namespace MyToDoWidget;

public sealed record SettingsRequest(string NewAddress, bool RemoveConnection, AppearanceOptions Appearance,
    bool AlwaysOnTop, bool LockPosition, bool RefreshOnStart);

public partial class SettingsWindow : Window
{
    private readonly Func<SettingsRequest, Task<string?>> _apply;
    private readonly Action<AppearanceOptions> _preview;
    private readonly bool _hasConnection;
    private bool _remove, _saving, _ready, _updatingColors;
    private string _backgroundColor, _textColor;
    private int[]? _customColors;
    private sealed record Choice<T>(T Value, string Label);

    public SettingsWindow(AppState state, Func<SettingsRequest, Task<string?>> apply, Action<AppearanceOptions> preview)
    {
        _apply = apply; _preview = preview;
        _hasConnection = state.Source is not null;
        _backgroundColor = state.BubbleColor; _textColor = state.MemoTextColor;
        InitializeComponent();
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
        ThemeList.ItemsSource = MemoAppearance.Presets;
        BackgroundHex.Text = _backgroundColor; TextHex.Text = _textColor;
        var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).Append(state.MemoFontFamily)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
        FontCombo.ItemsSource = fonts;
        FontCombo.SelectedItem = fonts.First(f => string.Equals(f, state.MemoFontFamily, StringComparison.OrdinalIgnoreCase));
        FontSlider.Value = state.FontSize;
        GlassOpacitySlider.Value = state.BackgroundPercent;
        BubbleOpacitySlider.Value = state.BubbleBackgroundPercent;
        BoldCheck.IsChecked = state.MemoBold;
        ItalicCheck.IsChecked = state.MemoItalic;
        LightThemeCheck.IsChecked = state.LightTheme;
        GlassCheck.IsChecked = state.GlassEffect;
        TopmostCheck.IsChecked = state.AlwaysOnTop;
        LockCheck.IsChecked = state.LockPosition;
        StartupCheck.IsChecked = state.RefreshOnStart;
        if (!_hasConnection) EditorPanel.SelectedIndex = 2;
        _ready = true;
        MatchTheme();
        UpdateLabels();
        PreviewAppearance();
        Loaded += (_, _) =>
        {
            var screen = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
            var dpi = VisualTreeHelper.GetDpi(this);
            double availableHeight = Math.Max(300, screen.WorkingArea.Height / dpi.DpiScaleY - 24);
            MinHeight = Math.Min(MinHeight, availableHeight);
            MaxHeight = availableHeight;
            Height = Math.Min(Height, MaxHeight);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsVisible && Owner is { } owner) DesktopAppearance.PlaceSettingsBeside(this, owner);
            }));
        };
    }

    private AppearanceOptions ReadAppearance() => new(FontSlider.Value, (int)Math.Round(GlassOpacitySlider.Value),
        LightThemeCheck.IsChecked == true, GlassCheck.IsChecked == true,
        TailCombo.SelectedValue is BubbleTail tail ? tail : BubbleTail.Left,
        ShapeCombo.SelectedValue is BubbleShape shape ? shape : BubbleShape.Rounded,
        _backgroundColor, (int)Math.Round(BubbleOpacitySlider.Value), _textColor,
        FontCombo.SelectedItem as string ?? MemoStyle.DefaultFont, BoldCheck.IsChecked == true, ItalicCheck.IsChecked == true);

    private void UpdateLabels()
    {
        if (!_ready) return;
        FontValue.Text = $"{FontSlider.Value:0}";
        GlassOpacityValue.Text = $"{GlassOpacitySlider.Value:0}%";
        BubbleOpacityValue.Text = $"{BubbleOpacitySlider.Value:0}%";
    }

    private void PreviewAppearance()
    {
        if (!_ready || _saving || _updatingColors) return;
        var appearance = ReadAppearance();
        _preview(appearance);
        SampleBubble.Shape = appearance.Shape;
        SampleBubble.TailSide = appearance.TailSide;
        SampleBubble.HorizontalAlignment = appearance.TailSide == BubbleTail.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        SampleBubble.Fill = MemoAppearance.Brush(appearance.BubbleColor, appearance.BubbleBackgroundPercent);
        SampleBubble.Stroke = MemoAppearance.Brush(appearance.TextColor, (int)Math.Round(appearance.BubbleBackgroundPercent * 0.18));
        SampleText.Foreground = MemoAppearance.Brush(appearance.TextColor);
        SampleText.FontFamily = MemoAppearance.Font(appearance.FontFamilyName);
        SampleText.FontSize = appearance.FontSize;
        SampleText.FontWeight = appearance.Bold ? FontWeights.Bold : FontWeights.Normal;
        SampleText.FontStyle = appearance.Italic ? FontStyles.Italic : FontStyles.Normal;
        BackgroundSwatch.Background = MemoAppearance.Brush(_backgroundColor);
        TextSwatch.Background = MemoAppearance.Brush(_textColor);
    }

    private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { UpdateLabels(); PreviewAppearance(); }
    private void Appearance_Changed(object sender, RoutedEventArgs e) => PreviewAppearance();
    private void Choice_Changed(object sender, SelectionChangedEventArgs e) => PreviewAppearance();

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
            if (text.Length > 8192) { ErrorText.Text = "주소가 너무 깁니다. CSV 게시 주소만 복사해 주세요."; return; }
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
            EditorPanel.SelectedIndex = 0;
            return;
        }
        _backgroundColor = background; _textColor = text;
        var request = new SettingsRequest(NewAddressBox.Password, _remove, ReadAppearance(),
            TopmostCheck.IsChecked == true, LockCheck.IsChecked == true, StartupCheck.IsChecked == true);
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
