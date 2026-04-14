using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CommandDeck.Converters;

/// <summary>
/// Converts a hex color string (e.g. "#cba6f7") to a WPF <see cref="Color"/>.
/// Returns <see cref="Colors.Transparent"/> for null or invalid input.
/// Used in XAML like: <c>Color="{Binding AccentColor, Converter={StaticResource HexToColor}}"</c>
/// </summary>
[ValueConversion(typeof(string), typeof(Color))]
public class HexToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch { /* fall through */ }
        }
        return Colors.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
