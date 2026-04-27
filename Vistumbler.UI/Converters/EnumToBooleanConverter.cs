using System.Globalization;
using System.Windows.Data;

namespace Vistumbler.UI.Converters;

public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return value.ToString()?.Equals(parameter.ToString(), StringComparison.OrdinalIgnoreCase) ?? false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value?.Equals(true) != true) return Binding.DoNothing;
        try
        {
            return Enum.Parse(targetType, parameter?.ToString() ?? string.Empty);
        }
        catch
        {
            return Binding.DoNothing;
        }
    }
}
