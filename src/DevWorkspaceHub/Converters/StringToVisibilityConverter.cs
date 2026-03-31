using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DevWorkspaceHub.Converters;

/// <summary>
/// Converts a non-null, non-empty string to Visible; null or empty to Collapsed.
/// Used to show/hide the shortcut badge in command palette items.
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrWhiteSpace(value as string)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}
