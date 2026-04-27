using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Vistumbler.UI.Converters;

/// <summary>
/// Converts a 6-character hex string (e.g. "99B4A1") to a SolidColorBrush.
/// Returns a transparent brush for invalid input.
/// </summary>
[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public class HexColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && hex.Length == 6 &&
            int.TryParse(hex, NumberStyles.HexNumber, null, out int rgb))
        {
            byte r = (byte)((rgb >> 16) & 0xFF);
            byte g = (byte)((rgb >>  8) & 0xFF);
            byte b = (byte)( rgb        & 0xFF);
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
