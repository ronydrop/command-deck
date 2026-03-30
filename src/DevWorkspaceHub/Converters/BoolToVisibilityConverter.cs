using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DevWorkspaceHub.Converters;

/// <summary>
/// Converts boolean to Visibility. True = Visible, False = Collapsed.
/// Pass "Invert" as parameter to reverse the logic.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;

        if (parameter is string param && param.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool result = value is Visibility v && v == Visibility.Visible;

        if (parameter is string param && param.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            result = !result;

        return result;
    }
}

/// <summary>
/// Converts an integer count to Visibility. count > 0 = Visible, 0 = Collapsed.
/// Pass "Invert" to reverse.
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasItems = value is int count && count > 0;

        if (parameter is string param && param.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            hasItems = !hasItems;

        return hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts null/empty string to Visibility. Non-null/non-empty = Visible, else = Collapsed.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasValue = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            _ => true
        };

        if (parameter is string param && param.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            hasValue = !hasValue;

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
