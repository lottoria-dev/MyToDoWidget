namespace MyToDoWidget;

// This value carries only appearance settings; it never exposes the saved sheet address.
public sealed record AppearanceOptions(double FontSize, int BackgroundPercent, bool LightTheme, bool GlassEffect,
    BubbleTail TailSide, BubbleShape Shape, string BubbleColor, int BubbleBackgroundPercent,
    string TextColor, string FontFamilyName, bool Bold, bool Italic)
{
    public static AppearanceOptions FromState(AppState state) => new(state.FontSize, state.BackgroundPercent,
        state.LightTheme, state.GlassEffect, state.TailSide, state.BubbleShape, state.BubbleColor,
        state.BubbleBackgroundPercent, state.MemoTextColor, state.MemoFontFamily, state.MemoBold, state.MemoItalic);

    public void ApplyTo(AppState state)
    {
        state.FontSize = FontSize; state.BackgroundPercent = BackgroundPercent;
        state.LightTheme = LightTheme; state.GlassEffect = GlassEffect;
        state.TailSide = TailSide; state.BubbleShape = Shape;
        state.BubbleColor = BubbleColor; state.BubbleBackgroundPercent = BubbleBackgroundPercent;
        state.MemoTextColor = TextColor; state.MemoFontFamily = FontFamilyName;
        state.MemoBold = Bold; state.MemoItalic = Italic;
    }
}
