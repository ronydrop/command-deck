using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CommandDeck.Converters;

/// <summary>
/// Converts a non-null, non-empty string to Visible; null or empty to Collapsed.
/// Pass "Invert" as ConverterParameter to reverse the logic (empty → Visible).
/// Used to show/hide the shortcut badge in command palette items.
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasValue = !string.IsNullOrWhiteSpace(value as string);

        if (parameter is string param && param.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            hasValue = !hasValue;

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool result = value is Visibility v && v == Visibility.Visible;

        if (parameter is string param && param.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            result = !result;

        return result;
    }
}
