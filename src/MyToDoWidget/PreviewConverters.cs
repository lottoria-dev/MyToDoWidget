using System.Globalization;
using System.Windows.Data;
namespace MyToDoWidget;

public sealed class PreviewHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2 && values[0] is double size && values[1] is int lines ? (lines == 0 ? double.PositiveInfinity : size * 1.45 * lines) : 48.0;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
public sealed class LineHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double size ? size * 1.45 : 24.0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class HeaderWrapConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? System.Windows.TextWrapping.Wrap : System.Windows.TextWrapping.NoWrap;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CardFillConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => Services.MemoAppearance.Brush(values.Length > 0 && values[0] is string color && MemoStyle.TryHexColor(color, out var hex) ? hex : MemoStyle.DefaultBackground,
            values.Length > 1 && values[1] is int opacity ? opacity : 100);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
