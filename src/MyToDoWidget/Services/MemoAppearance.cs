namespace MyToDoWidget.Services;

public sealed record PastelTheme(string Name, string Background, string Foreground)
{
    public Brush BackgroundBrush => MemoAppearance.Brush(Background);
    public Brush ForegroundBrush => MemoAppearance.Brush(Foreground);
}

public static class MemoAppearance
{
    public static IReadOnlyList<PastelTheme> Presets { get; } = new PastelTheme[]
    {
        new("민트", "#DDF2E7", "#263B33"), new("하늘", "#DDEFFC", "#293E51"),
        new("라벤더", "#EAE2FA", "#423653"), new("로즈", "#F8DFE7", "#553541"),
        new("복숭아", "#FBE4D3", "#513C2E"), new("레몬", "#FAF0C9", "#4A4225"),
        new("청록", "#D9F0F0", "#294748"), new("라일락", "#F0DFF2", "#4A3550"),
        new("피스타치오", "#E8F0D6", "#3D472C"), new("살구", "#F9E9D6", "#51412F"),
        new("블루베리", "#E0E5FA", "#323D5B"), new("구름", "#E9EDF2", "#354150")
    };

    public static SolidColorBrush Brush(string hex, int opacity = 100)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        color.A = (byte)Math.Round(Math.Clamp(opacity, 0, 100) * 255.0 / 100);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static FontFamily Font(string name)
    {
        try { return new FontFamily(name); }
        catch (ArgumentException) { return new FontFamily(MemoStyle.DefaultFont); }
    }
}
