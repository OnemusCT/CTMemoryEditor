using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CTMemoryEditor.Converters;

/// <summary>
/// Converts a boolean to a color brush: true = green, false = red.
/// </summary>
public sealed class BoolToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x2E, 0xCC, 0x71));
    private static readonly SolidColorBrush Red = new(Color.FromRgb(0xE7, 0x4C, 0x3C));

    static BoolToColorConverter()
    {
        Green.Freeze();
        Red.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Green : Red;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
