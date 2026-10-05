namespace MyToDoWidget.Core;

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 1;
    public string? Source { get; set; }
    public bool SharedLinkSetupRequired { get; set; }
    public List<string> Notes { get; set; } = [];
    public bool HasSnapshot { get; set; }
    public DateTimeOffset? LastFetched { get; set; }
    public double Width { get; set; } = 390;
    public double Height { get; set; } = 530;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public DesktopPosition? PreferredDesktopPosition { get; set; }
    public double FontSize { get; set; } = 16;
    public bool ScaleTextWithWindow { get; set; } = true;
    public double TextReferenceWidth { get; set; }
    public double TextReferenceHeight { get; set; }
    // 0 = transparent, 100 = opaque. Preserve the stored value when upgrading.
    public int BackgroundPercent { get; set; } = 60;
    public int HeaderBackgroundPercent { get; set; } = 92;
    public int OuterBorderPercent { get; set; } = 90;
    // null follows the light/dark theme, including settings saved by older versions.
    public string? GlassColor { get; set; }
    public BubbleTail TailSide { get; set; } = BubbleTail.Left;
    public BubbleShape BubbleShape { get; set; } = BubbleShape.Rounded;
    // Null migrates the former tail-based alignment once, preserving existing layouts.
    public BubbleAlign? BubbleAlignment { get; set; }
    public BubbleWidthMode BubbleWidth { get; set; } = BubbleWidthMode.Content;
    public int BubbleGap { get; set; } = 10;
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
        Width = double.IsFinite(Width) ? Math.Clamp(Width, 180, 2000) : 390;
        Height = double.IsFinite(Height) ? Math.Clamp(Height, 280, 2000) : 530;
        // Anchor older settings at their existing expanded size to avoid an upgrade jump.
        TextReferenceWidth = double.IsFinite(TextReferenceWidth) && TextReferenceWidth > 0
            ? Math.Clamp(TextReferenceWidth, 180, 2000) : Width;
        TextReferenceHeight = double.IsFinite(TextReferenceHeight) && TextReferenceHeight > 0
            ? Math.Clamp(TextReferenceHeight, 280, 2000) : Height;
        FontSize = double.IsFinite(FontSize) ? Math.Clamp(Math.Round(FontSize), 11, 36) : 16;
        BackgroundPercent = Math.Clamp(BackgroundPercent, 0, 100);
        HeaderBackgroundPercent = Math.Clamp(HeaderBackgroundPercent, 0, 100);
        OuterBorderPercent = Math.Clamp(OuterBorderPercent, 0, 100);
        if (PreferredDesktopPosition is { IsValid: false }) PreferredDesktopPosition = null;
        GlassColor = MemoStyle.TryHexColor(GlassColor, out var glass) ? glass : null;
        BubbleBackgroundPercent = Math.Clamp(BubbleBackgroundPercent, 0, 100);
        if (!Enum.IsDefined(TailSide)) TailSide = BubbleTail.Left;
        if (!Enum.IsDefined(BubbleShape)) BubbleShape = BubbleShape.Rounded;
        if (BubbleAlignment is not { } alignment || !Enum.IsDefined(alignment))
            BubbleAlignment = TailSide == BubbleTail.Right ? BubbleAlign.Right : BubbleAlign.Left;
        if (!Enum.IsDefined(BubbleWidth)) BubbleWidth = BubbleWidthMode.Content;
        BubbleGap = Math.Clamp(BubbleGap, 0, 40);
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
