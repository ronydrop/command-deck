using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CommandDeck.Converters;

/// <summary>
/// Converts a Git file status letter (M, A, D, ?, U) to a theme color brush.
/// </summary>
public class GitFileStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var status = value?.ToString() ?? "";
        var resourceKey = status switch
        {
            "M" => "AccentYellowBrush",
            "A" => "AccentGreenBrush",
            "D" => "AccentRedBrush",
            "R" => "AccentBlueBrush",
            "U" => "AccentPeachBrush",
            "?" => "SubtextBrush",
            _ => "SubtextBrush"
        };
        return Application.Current.FindResource(resourceKey) as Brush
               ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
