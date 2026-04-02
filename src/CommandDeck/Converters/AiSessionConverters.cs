using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CommandDeck.Models;

namespace CommandDeck.Converters;

public class AiSessionTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AiSessionType type && type != AiSessionType.None)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class AiSessionTypeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not AiSessionType type)
            return Brushes.Transparent;

        return type switch
        {
            AiSessionType.Claude       => new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)), // Green
            AiSessionType.ClaudeResume => new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)), // Green
            AiSessionType.Codex        => new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)), // Blue
            AiSessionType.Aider        => new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF)), // Yellow
            AiSessionType.Gemini       => new SolidColorBrush(Color.FromRgb(0x94, 0xE2, 0xD5)), // Cyan
            AiSessionType.Copilot      => new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7)), // Mauve
            _                          => Brushes.Transparent
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class AiSessionTypeToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not AiSessionType type || type == AiSessionType.None)
            return string.Empty;

        return "AI";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class AiModelToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string model || string.IsNullOrEmpty(model))
            return string.Empty;

        return model.ToLowerInvariant() switch
        {
            "claude"        => "CLD",
            "claude-resume" => "RES",
            "codex"         => "CDX",
            "aider"         => "ADR",
            "gemini"        => "GMN",
            "copilot"       => "CPL",
            _ => model.Length > 4 ? model[..4].ToUpperInvariant() : model.ToUpperInvariant()
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class AiModelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string model || string.IsNullOrEmpty(model))
            return Brushes.Transparent;

        return model.ToLowerInvariant() switch
        {
            "claude"        => new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)), // Green
            "claude-resume" => new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)), // Green
            "codex"         => new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)), // Blue
            "aider"         => new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF)), // Yellow
            "gemini"        => new SolidColorBrush(Color.FromRgb(0x94, 0xE2, 0xD5)), // Cyan
            "copilot"       => new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7)), // Mauve
            _               => new SolidColorBrush(Color.FromRgb(0xBA, 0xC2, 0xDE))  // Subtext0
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
