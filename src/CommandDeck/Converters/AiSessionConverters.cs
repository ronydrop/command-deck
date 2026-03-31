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
            AiSessionType.Cc => new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7)),        // Mauve
            AiSessionType.CcRun => new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)),     // Blue
            AiSessionType.CcOpenRouter => new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF)), // Yellow
            AiSessionType.Claude => new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)),    // Green
            _ => Brushes.Transparent
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class AiSessionTypeToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not AiSessionType type)
            return string.Empty;

        return type switch
        {
            AiSessionType.Cc => "AI",
            AiSessionType.CcRun => "AI",
            AiSessionType.CcOpenRouter => "OR",
            AiSessionType.Claude => "AI",
            _ => string.Empty
        };
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
            "sonnet" => "SNT",
            "opus" => "OPS",
            "haiku" => "HKU",
            "agent" => "AGT",
            "openrouter" => "OR",
            "claude" => "CLD",
            "default" => "CC",
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
            "opus" => new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7)),      // Mauve
            "sonnet" => new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)),     // Blue
            "haiku" => new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)),      // Green
            "agent" => new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF)),      // Yellow
            "openrouter" => new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF)), // Yellow
            "claude" => new SolidColorBrush(Color.FromRgb(0x94, 0xE2, 0xD5)),     // Cyan
            "default" => new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7)),    // Mauve
            _ => new SolidColorBrush(Color.FromRgb(0xBA, 0xC2, 0xDE))             // Subtext0
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
