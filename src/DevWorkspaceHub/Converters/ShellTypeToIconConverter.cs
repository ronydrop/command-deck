using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Converters;

/// <summary>
/// Converts ShellType enum to an icon glyph string for display.
/// </summary>
public class ShellTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ShellType shellType)
            return shellType.GetIconGlyph();

        return "\uE756"; // Default terminal icon
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts ShellType enum to its display name.
/// </summary>
public class ShellTypeToNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ShellType shellType)
            return shellType.GetDisplayName();

        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts ShellType to a color brush for visual distinction.
/// </summary>
public class ShellTypeToColorConverter : IValueConverter
{
    private static readonly Dictionary<ShellType, Color> ShellColors = new()
    {
        { ShellType.WSL, Color.FromRgb(0xE9, 0x55, 0x20) },       // Ubuntu orange
        { ShellType.PowerShell, Color.FromRgb(0x01, 0x2A, 0x56) }, // PS blue
        { ShellType.CMD, Color.FromRgb(0x6E, 0x6E, 0x6E) },       // Gray
        { ShellType.GitBash, Color.FromRgb(0xF0, 0x50, 0x33) },    // Git red
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ShellType shellType && ShellColors.TryGetValue(shellType, out var color))
            return new SolidColorBrush(color);

        return new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)); // Default purple
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
