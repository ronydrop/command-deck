using System;
using System.Globalization;
using System.Windows.Data;

namespace CommandDeck.Converters;

/// <summary>
/// IMultiValueConverter that returns true when all string values are equal.
/// Used for model pill active-state detection inside ItemsControl templates.
/// </summary>
public class StringEqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        string? a = values[0]?.ToString();
        string? b = values[1]?.ToString();
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
