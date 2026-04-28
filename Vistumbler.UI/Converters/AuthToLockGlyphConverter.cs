using System;
using System.Globalization;
using System.Windows.Data;
using Vistumbler.Core.Models;

namespace Vistumbler.UI.Converters;

/// <summary>
/// Converts AuthenticationType to a Segoe MDL2 Assets glyph:
///   Open → U+E785  (open padlock)
///   Anything else → U+E72E  (closed padlock)
/// </summary>
public class AuthToLockGlyphConverter : IValueConverter
{
    private const string LockOpen   = "\uE785";
    private const string LockClosed = "\uE72E";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is AuthenticationType auth && auth == AuthenticationType.Open
            ? LockOpen
            : LockClosed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
