using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DevWorkspaceHub.Converters;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the value is 0 (empty collection),
/// <see cref="Visibility.Collapsed"/> otherwise.
/// Used for the "empty state" panel.
/// </summary>
public class InverseCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int count = value switch
        {
            int i => i,
            _ => 0
        };
        return count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
