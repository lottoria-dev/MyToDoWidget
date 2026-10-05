namespace MyToDoWidget;

// This value carries only appearance settings; it never exposes the saved sheet address.
public sealed record AppearanceOptions(double FontSize, int BackgroundPercent, bool LightTheme, bool GlassEffect,
    BubbleTail TailSide, BubbleShape Shape, string BubbleColor, int BubbleBackgroundPercent,
    string TextColor, string FontFamilyName, bool Bold, bool Italic, string? GlassColor,
    bool ScaleTextWithWindow, double TextReferenceWidth, double TextReferenceHeight,
    int HeaderBackgroundPercent, int OuterBorderPercent,
    BubbleAlign Alignment, BubbleWidthMode WidthMode, int BubbleGap)
{
    public static AppearanceOptions FromState(AppState state) => new(state.FontSize, state.BackgroundPercent,
        state.LightTheme, state.GlassEffect, state.TailSide, state.BubbleShape, state.BubbleColor,
        state.BubbleBackgroundPercent, state.MemoTextColor, state.MemoFontFamily, state.MemoBold, state.MemoItalic, state.GlassColor,
        state.ScaleTextWithWindow, state.TextReferenceWidth, state.TextReferenceHeight,
        state.HeaderBackgroundPercent, state.OuterBorderPercent,
        state.BubbleAlignment ?? (state.TailSide == BubbleTail.Right ? BubbleAlign.Right : BubbleAlign.Left),
        state.BubbleWidth, state.BubbleGap);

    public void ApplyTo(AppState state)
    {
        state.FontSize = FontSize; state.BackgroundPercent = BackgroundPercent;
        state.GlassColor = GlassColor;
        state.HeaderBackgroundPercent = HeaderBackgroundPercent;
        state.OuterBorderPercent = OuterBorderPercent;
        state.BubbleAlignment = Alignment;
        state.BubbleWidth = WidthMode;
        state.BubbleGap = BubbleGap;
        state.ScaleTextWithWindow = ScaleTextWithWindow;
        state.TextReferenceWidth = TextReferenceWidth; state.TextReferenceHeight = TextReferenceHeight;
        state.LightTheme = LightTheme; state.GlassEffect = GlassEffect;
        state.TailSide = TailSide; state.BubbleShape = Shape;
        state.BubbleColor = BubbleColor; state.BubbleBackgroundPercent = BubbleBackgroundPercent;
        state.MemoTextColor = TextColor; state.MemoFontFamily = FontFamilyName;
        state.MemoBold = Bold; state.MemoItalic = Italic;
    }
}
