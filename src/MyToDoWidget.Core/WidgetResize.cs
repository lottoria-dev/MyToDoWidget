namespace MyToDoWidget.Core;

public static class WidgetResize
{
    // Project both pointer axes onto the original diagonal. Width is the sole size input;
    // text scaling and automatic content height keep the existing layout proportions.
    public static double WidthDelta(double width, double height, double dx, double dy, bool diagonal)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return 0;
        if (!diagonal) return dx;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return dx;
        double ratio = height / width;
        return (dx + dy * ratio) / (1 + ratio * ratio);
    }
}
