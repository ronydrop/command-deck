using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DevWorkspaceHub.Converters;

/// <summary>
/// Converts null or empty string to Visible; non-empty to Collapsed.
/// Inverse of <see cref="StringToVisibilityConverter"/>.
/// </summary>
public class InverseStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.IsNullOrWhiteSpace(value as string)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility.Collapsed;
    }
}
