using System.Collections.Generic;
using System.Linq;
using CommandDeck.Models;

namespace CommandDeck.Helpers;

/// <summary>
/// A theme option entry used in the theme selector ComboBox.
/// </summary>
/// <param name="Id">The ResourceDictionary file name (without .xaml), used as identifier.</param>
/// <param name="DisplayName">Human-readable label shown in the UI.</param>
public record ThemeOption(string Id, string DisplayName);

/// <summary>
/// Single source of truth for all available themes: their IDs, display names, and mode classification.
/// Avoids hardcoded theme lists scattered across ViewModels and AppSettings.
/// </summary>
public static class ThemeCatalog
{
    // Theme IDs — match the ResourceDictionary file names in Resources/Themes/
    public const string MorphDark       = "MorphDark";
    public const string CatppuccinMocha = "CatppuccinMocha";
    public const string VSCodeDark      = "VSCodeDark";
    public const string Dracula         = "Dracula";
    public const string LiquidGlassDark = "LiquidGlassDark";
    public const string VSCodeLight     = "VSCodeLight";
    public const string LiquidGlass     = "LiquidGlass";

    // Default theme for each mode
    public const string DefaultDark  = MorphDark;
    public const string DefaultLight = LiquidGlass;

    /// <summary>
    /// All registered themes with their mode classification and display name.
    /// Order determines the order they appear in the selector.
    /// </summary>
    private static readonly IReadOnlyList<(string Id, ThemeMode Mode, string DisplayName)> All =
        new[]
        {
            (MorphDark,       ThemeMode.Dark,  "Morph"),
            (CatppuccinMocha, ThemeMode.Dark,  "Catppuccin Mocha"),
            (VSCodeDark,      ThemeMode.Dark,  "VS Code Dark"),
            (Dracula,         ThemeMode.Dark,  "Dracula"),
            (LiquidGlassDark, ThemeMode.Dark,  "Liquid Glass Dark"),
            (VSCodeLight,     ThemeMode.Light, "VS Code Light"),
            (LiquidGlass,     ThemeMode.Light, "Liquid Glass"),
        };

    /// <summary>
    /// Returns the subset of themes valid for the given mode.
    /// For <see cref="ThemeMode.System"/>, returns all themes — callers should
    /// resolve the effective OS mode first via <see cref="SystemThemeDetector.GetSystemMode"/>.
    /// </summary>
    public static IReadOnlyList<ThemeOption> GetThemesForMode(ThemeMode mode)
    {
        if (mode == ThemeMode.System)
            return All.Select(t => new ThemeOption(t.Id, t.DisplayName)).ToList();

        return All
            .Where(t => t.Mode == mode)
            .Select(t => new ThemeOption(t.Id, t.DisplayName))
            .ToList();
    }

    /// <summary>
    /// Returns the ThemeMode classification of a theme ID.
    /// Defaults to Dark for unknown IDs.
    /// </summary>
    public static ThemeMode GetModeForTheme(string themeId)
    {
        var entry = All.FirstOrDefault(t => t.Id == themeId);
        return entry == default ? ThemeMode.Dark : entry.Mode;
    }

    /// <summary>
    /// Returns true when the given theme ID is compatible with the given mode.
    /// System mode accepts any theme.
    /// </summary>
    public static bool IsCompatible(string themeId, ThemeMode mode)
    {
        if (mode == ThemeMode.System) return true;
        return GetModeForTheme(themeId) == mode;
    }

    /// <summary>
    /// Returns the default theme ID for the given mode.
    /// For System, returns the dark default.
    /// </summary>
    public static string DefaultThemeForMode(ThemeMode mode) => mode switch
    {
        ThemeMode.Light  => DefaultLight,
        _                => DefaultDark,
    };

    /// <summary>
    /// Returns a friendly display name for a theme ID, or the raw ID if not found.
    /// </summary>
    public static string GetDisplayName(string themeId)
    {
        var entry = All.FirstOrDefault(t => t.Id == themeId);
        return entry == default ? themeId : entry.DisplayName;
    }
}
