using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CommandDeck.Converters;

/// <summary>
/// Converts a hex color string (e.g. "#cba6f7") to a frozen <see cref="SolidColorBrush"/>.
/// Returns <see cref="Brushes.Transparent"/> for null or invalid input.
/// Used in XAML to bind card accent strips: <c>Background="{Binding Color, Converter={StaticResource HexToBrush}}"</c>
/// </summary>
[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch { /* fall through */ }
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
