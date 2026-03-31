using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CommandDeck.Converters;

/// <summary>
/// Converts null/non-null to <see cref="Visibility"/>.
/// null    -> Collapsed
/// non-null -> Visible
/// Pass "Inverse" as parameter to swap behavior.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value is null;
        var p = parameter as string;
        var inverse = string.Equals(p, "Inverse", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(p, "Invert", StringComparison.OrdinalIgnoreCase);

        return (isNull ^ inverse) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
