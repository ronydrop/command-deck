using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Converters;

/// <summary>
/// Converts TerminalStatus to a color brush.
/// </summary>
public class TerminalStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value switch
        {
            TerminalStatus.Running => Color.FromRgb(0xA6, 0xE3, 0xA1),   // Green
            TerminalStatus.Starting => Color.FromRgb(0xF9, 0xE2, 0xAF),  // Yellow
            TerminalStatus.Stopped => Color.FromRgb(0x58, 0x5B, 0x70),   // Gray
            TerminalStatus.Error => Color.FromRgb(0xF3, 0x8B, 0xA8),     // Red
            _ => Color.FromRgb(0x58, 0x5B, 0x70)
        };

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts ProcessRunStatus to a color brush.
/// </summary>
public class ProcessStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value switch
        {
            ProcessRunStatus.Running => Color.FromRgb(0xA6, 0xE3, 0xA1),   // Green
            ProcessRunStatus.Suspended => Color.FromRgb(0xF9, 0xE2, 0xAF), // Yellow
            ProcessRunStatus.Stopped => Color.FromRgb(0xF3, 0x8B, 0xA8),   // Red
            _ => Color.FromRgb(0x58, 0x5B, 0x70)
        };

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts GitRepoStatus to a color brush.
/// </summary>
public class GitStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value switch
        {
            GitRepoStatus.Clean => Color.FromRgb(0xA6, 0xE3, 0xA1),       // Green
            GitRepoStatus.Modified => Color.FromRgb(0xF9, 0xE2, 0xAF),    // Yellow
            GitRepoStatus.Staged => Color.FromRgb(0x89, 0xB4, 0xFA),      // Blue
            GitRepoStatus.Conflicted => Color.FromRgb(0xF3, 0x8B, 0xA8),  // Red
            GitRepoStatus.Detached => Color.FromRgb(0xCB, 0xA6, 0xF7),    // Magenta
            _ => Color.FromRgb(0x58, 0x5B, 0x70)
        };

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a hex color string to a SolidColorBrush.
/// </summary>
public class HexColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch
            {
                // Fall through to default
            }
        }

        return new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush brush)
            return $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
        return "#7C3AED";
    }
}

/// <summary>
/// Converts ViewType enum to visibility for showing/hiding views.
/// Usage: ConverterParameter="Dashboard" shows when ViewType == Dashboard.
/// </summary>
public class ViewTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ViewModels.ViewType viewType && parameter is string targetView)
        {
            if (Enum.TryParse<ViewModels.ViewType>(targetView, out var target))
                return viewType == target ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
