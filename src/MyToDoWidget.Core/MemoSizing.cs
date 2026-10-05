namespace MyToDoWidget.Core;

public static class MemoSizing
{
    public static double FontSize(double baseSize, bool enabled, double width, double height,
        double referenceWidth, double referenceHeight)
    {
        baseSize = double.IsFinite(baseSize) ? Math.Clamp(baseSize, 11, 36) : 16;
        if (!enabled) return baseSize;
        referenceWidth = ValidSize(referenceWidth, 390);
        referenceHeight = ValidSize(referenceHeight, 530);
        width = ValidSize(width, referenceWidth);
        height = ValidSize(height, referenceHeight);
        // Geometric mean: doubling both dimensions doubles the text size.
        // Resizing only one dimension changes text more gently.
        double scale = Math.Clamp(Math.Sqrt(width / referenceWidth * (height / referenceHeight)), 0.75, 2.5);
        return Math.Round(Math.Clamp(baseSize * scale, 11, 72), 1);
    }

    private static double ValidSize(double size, double fallback) =>
        double.IsFinite(size) && size > 0 ? Math.Clamp(size, 1, 2000) : fallback;
}
