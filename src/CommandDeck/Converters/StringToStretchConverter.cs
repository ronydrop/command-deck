using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CommandDeck.Converters;

/// <summary>
/// Converts a stretch mode string ("None", "Fill", "Uniform", "UniformToFill")
/// to <see cref="Stretch"/> enum value.
/// </summary>
public class StringToStretchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && Enum.TryParse<Stretch>(s, true, out var stretch))
            return stretch;

        return Stretch.UniformToFill;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Stretch stretch)
            return stretch.ToString();

        return "UniformToFill";
    }
}
