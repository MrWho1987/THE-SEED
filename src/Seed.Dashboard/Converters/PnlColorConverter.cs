using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Seed.Dashboard.Converters;

public class PnlColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Profit = new(Color.FromRgb(0x00, 0xF6, 0xA1));
    private static readonly SolidColorBrush Loss = new(Color.FromRgb(0xFF, 0x38, 0x64));
    private static readonly SolidColorBrush Flat = new(Color.FromRgb(0x94, 0xA3, 0xB8));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d) return d > 0 ? Profit : d < 0 ? Loss : Flat;
        if (value is float f) return f > 0 ? Profit : f < 0 ? Loss : Flat;
        if (value is double dbl) return dbl > 0 ? Profit : dbl < 0 ? Loss : Flat;
        return Flat;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class PnlArrowConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d) return d > 0 ? "\u25B2" : d < 0 ? "\u25BC" : "\u2014";
        if (value is float f) return f > 0 ? "\u25B2" : f < 0 ? "\u25BC" : "\u2014";
        if (value is double dbl) return dbl > 0 ? "\u25B2" : dbl < 0 ? "\u25BC" : "\u2014";
        return "\u2014";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
