namespace MyToDoWidget.Core;

public static class WidgetHeight
{
    public const double Minimum = 84;

    public static double Fit(double chrome, double content, double available, double maximum = 2000)
    {
        chrome = double.IsFinite(chrome) ? Math.Max(0, chrome) : 0;
        content = double.IsFinite(content) ? Math.Max(0, content) : 0;
        available = double.IsFinite(available) ? Math.Max(Minimum, available) : 530;
        maximum = double.IsFinite(maximum) ? Math.Max(Minimum, maximum) : 2000;
        double limit = Math.Min(available, maximum);
        return Math.Clamp(Math.Ceiling(chrome + content), Minimum, limit);
    }
}
