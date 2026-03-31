using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DevWorkspaceHub.Converters;

/// <summary>
/// Converts a boolean to a GridLength.
/// True  -> GridLength(ConverterParameter or 320, GridUnitType.Pixel)
/// False -> GridLength(0)
/// </summary>
public class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isOpen = value is bool b && b;

        if (!isOpen)
            return new GridLength(0);

        double width = 320;
        if (parameter is string s && double.TryParse(s, out var parsed))
            width = parsed;

        return new GridLength(width);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
