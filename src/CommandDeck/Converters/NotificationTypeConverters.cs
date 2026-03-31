using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CommandDeck.Models;

namespace CommandDeck.Converters;

/// <summary>
/// Converts NotificationType to an accent color brush for the left border stripe.
/// </summary>
public class NotificationTypeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not NotificationType type)
            return DependencyProperty.UnsetValue;

        var resourceKey = type switch
        {
            NotificationType.Success  => "AccentGreenBrush",
            NotificationType.Warning  => "AccentYellowBrush",
            NotificationType.Error    => "AccentRedBrush",
            NotificationType.Progress => "AccentMauveBrush",
            _                         => "AccentBlueBrush"  // Info
        };

        return Application.Current.TryFindResource(resourceKey) as Brush
               ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts NotificationSource to an icon glyph string.
/// </summary>
public class NotificationSourceToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not NotificationSource source)
            return "\u2139"; // ℹ

        return source switch
        {
            NotificationSource.Terminal => "\uD83D\uDCBB", // 💻
            NotificationSource.Git     => "\uD83D\uDD00",  // 🔀
            NotificationSource.Process => "\u2699",         // ⚙
            NotificationSource.AI      => "\u2728",         // ✨
            _                          => "\u2139"          // ℹ
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
