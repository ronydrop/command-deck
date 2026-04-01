using System.Windows.Media;

namespace CommandDeck.Helpers;

/// <summary>Default theme color constants used across the application.</summary>
public static class ThemeColors
{
    // Catppuccin Mocha (default theme)
    public static readonly Color CatppuccinText = Color.FromRgb(0xCD, 0xD6, 0xF4);
    public static readonly Color CatppuccinBase = Color.FromRgb(0x1E, 0x1E, 0x2E);

    // Default project/accent colors
    public const string DefaultProjectColor = "#7C3AED";
    public const string DefaultAccentColor = "#CBA6F7";
}
