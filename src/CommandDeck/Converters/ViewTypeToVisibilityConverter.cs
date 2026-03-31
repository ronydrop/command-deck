using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CommandDeck.Converters;

/// <summary>
/// Converts a view type name (string) match to <see cref="Visibility"/>.
/// Usage: ConverterParameter="DashboardView" — shows when value matches the parameter.
/// </summary>
public class ViewTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var currentView = value?.ToString();
        var targetView = parameter?.ToString();

        return string.Equals(currentView, targetView, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
