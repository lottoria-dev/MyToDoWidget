namespace MyToDoWidget.Core;

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 1;
    public string? Source { get; set; }
    public List<string> Notes { get; set; } = [];
    public bool HasSnapshot { get; set; }
    public DateTimeOffset? LastFetched { get; set; }
    public double Width { get; set; } = 390;
    public double Height { get; set; } = 530;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double FontSize { get; set; } = 16;
    // 0 = transparent, 100 = opaque. Preserve the stored value when upgrading.
    public int BackgroundPercent { get; set; } = 60;
    public BubbleTail TailSide { get; set; } = BubbleTail.Left;
    public BubbleShape BubbleShape { get; set; } = BubbleShape.Rounded;
    public string BubbleColor { get; set; } = MemoStyle.DefaultBackground;
    public int BubbleBackgroundPercent { get; set; } = 100;
    public string MemoTextColor { get; set; } = MemoStyle.DefaultText;
    public string MemoFontFamily { get; set; } = MemoStyle.DefaultFont;
    public bool MemoBold { get; set; }
    public bool MemoItalic { get; set; }
    public bool IsRolledUp { get; set; }
    public bool LightTheme { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool LockPosition { get; set; }
    public bool GlassEffect { get; set; } = true;
    public bool RefreshOnStart { get; set; }

    public AppState Copy()
    {
        var copy = (AppState)MemberwiseClone();
        copy.Notes = new List<string>(Notes);
        return copy;
    }

    public void Normalize()
    {
        Width = double.IsFinite(Width) ? Math.Clamp(Width, 310, 2000) : 390;
        Height = double.IsFinite(Height) ? Math.Clamp(Height, 280, 2000) : 530;
        FontSize = double.IsFinite(FontSize) ? Math.Clamp(Math.Round(FontSize), 11, 36) : 16;
        BackgroundPercent = Math.Clamp(BackgroundPercent, 0, 100);
        BubbleBackgroundPercent = Math.Clamp(BubbleBackgroundPercent, 0, 100);
        if (!Enum.IsDefined(TailSide)) TailSide = BubbleTail.Left;
        if (!Enum.IsDefined(BubbleShape)) BubbleShape = BubbleShape.Rounded;
        BubbleColor = MemoStyle.TryHexColor(BubbleColor, out var background) ? background : MemoStyle.DefaultBackground;
        MemoTextColor = MemoStyle.TryHexColor(MemoTextColor, out var text) ? text : MemoStyle.DefaultText;
        MemoFontFamily = string.IsNullOrWhiteSpace(MemoFontFamily) || MemoFontFamily.Length > 128
            ? MemoStyle.DefaultFont : MemoFontFamily.Trim();
        if (Left.HasValue && !double.IsFinite(Left.Value)) Left = null;
        if (Top.HasValue && !double.IsFinite(Top.Value)) Top = null;
        Notes ??= [];
        if (Source is null) { Notes.Clear(); LastFetched = null; HasSnapshot = false; }
    }
}
