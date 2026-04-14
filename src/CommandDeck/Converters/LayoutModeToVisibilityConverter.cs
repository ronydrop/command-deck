using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CommandDeck.Models;

namespace CommandDeck.Converters;

/// <summary>
/// Converts a <see cref="LayoutMode"/> to <see cref="Visibility"/>.
/// Parameter "Bento"    → Visible when mode is Bento; Collapsed otherwise.
/// Parameter "NotBento" → Collapsed when mode is Bento; Visible otherwise.
/// </summary>
[ValueConversion(typeof(LayoutMode), typeof(Visibility))]
public class LayoutModeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not LayoutMode mode) return Visibility.Visible;

        bool isBento = mode == LayoutMode.Bento;
        return parameter?.ToString() switch
        {
            "Bento"    =>  isBento ? Visibility.Visible  : Visibility.Collapsed,
            "NotBento" => !isBento ? Visibility.Visible  : Visibility.Collapsed,
            _          => Visibility.Visible
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
