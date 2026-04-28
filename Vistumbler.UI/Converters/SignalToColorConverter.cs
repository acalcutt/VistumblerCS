using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Vistumbler.UI.Converters;

/// <summary>
/// Converts a signal-strength percentage (int?) to a colour matching the
/// original Vistumbler icon set:
///   0       → grey        (no/unknown signal)
///   1–20    → red
///   21–40   → orange
///   41–60   → yellow
///   61–80   → light green
///   81–100  → green
/// </summary>
public class SignalToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Grey       = new(Color.FromRgb(0x88, 0x88, 0x88));
    private static readonly SolidColorBrush Red        = new(Color.FromRgb(0xDD, 0x33, 0x33));
    private static readonly SolidColorBrush Orange     = new(Color.FromRgb(0xFF, 0x8C, 0x00));
    private static readonly SolidColorBrush Yellow     = new(Color.FromRgb(0xCC, 0xA8, 0x00));
    private static readonly SolidColorBrush LightGreen = new(Color.FromRgb(0x7C, 0xBF, 0x57));
    private static readonly SolidColorBrush Green      = new(Color.FromRgb(0x22, 0xA0, 0x22));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Nullable int? boxes to int when it has a value, so `is int` covers both int and int?
        int signal = value is int s ? s : 0;

        return signal switch
        {
            <= 0  => Grey,
            <= 20 => Red,
            <= 40 => Orange,
            <= 60 => Yellow,
            <= 80 => LightGreen,
            _     => Green
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
