namespace MyToDoWidget.Core;

public static class BubbleLayout
{
    public static double Width(double available, double natural, bool fitContent)
    {
        available = double.IsFinite(available) ? Math.Max(0, available) : Math.Max(0, natural);
        natural = double.IsFinite(natural) ? Math.Max(0, natural) : available;
        return fitContent ? Math.Min(available, natural) : available;
    }
}
