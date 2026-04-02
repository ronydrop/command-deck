using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CommandDeck.Models;

namespace CommandDeck.Converters;

/// <summary>
/// Maps Dynamic Island tones to theme brushes.
/// Parameters:
/// - "Background" for softer card backgrounds
/// - "Border" for outline / badge emphasis
/// - "Foreground" for text accents
/// </summary>
public sealed class DynamicIslandToneToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var tone = value is DynamicIslandVisualTone visualTone
            ? visualTone
            : DynamicIslandVisualTone.Neutral;
        var variant = parameter as string ?? "Foreground";

        return (tone, variant) switch
        {
            (DynamicIslandVisualTone.Accent, "Background") => GetBrush("AccentBlueBrush", 0.16),
            (DynamicIslandVisualTone.Accent, "Border") => GetBrush("AccentBlueBrush"),
            (DynamicIslandVisualTone.Accent, _) => GetBrush("AccentBlueBrush"),

            (DynamicIslandVisualTone.Success, "Background") => GetBrush("AccentGreenBrush", 0.16),
            (DynamicIslandVisualTone.Success, "Border") => GetBrush("AccentGreenBrush"),
            (DynamicIslandVisualTone.Success, _) => GetBrush("AccentGreenBrush"),

            (DynamicIslandVisualTone.Warning, "Background") => GetBrush("AccentYellowBrush", 0.16),
            (DynamicIslandVisualTone.Warning, "Border") => GetBrush("AccentYellowBrush"),
            (DynamicIslandVisualTone.Warning, _) => GetBrush("AccentYellowBrush"),

            (DynamicIslandVisualTone.Danger, "Background") => GetBrush("AccentRedBrush", 0.16),
            (DynamicIslandVisualTone.Danger, "Border") => GetBrush("AccentRedBrush"),
            (DynamicIslandVisualTone.Danger, _) => GetBrush("AccentRedBrush"),

            (_, "Background") => GetBrush("Surface1Brush"),
            (_, "Border") => GetBrush("Surface2Brush"),
            _ => GetBrush("TextBrush")
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static Brush GetBrush(string key, double? opacity = null)
    {
        var brush = Application.Current.Resources[key] as SolidColorBrush
                    ?? Brushes.Transparent;

        if (opacity is null)
            return brush;

        var clone = brush.Clone();
        clone.Opacity = opacity.Value;
        return clone;
    }
}
