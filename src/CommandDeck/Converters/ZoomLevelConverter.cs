using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace CommandDeck.Converters;

/// <summary>
/// Converts a ZoomPercent integer to a level string: "low", "normal", or "high".
/// Low  = below  50 %   → yellow badge accent
/// High = above 150 %   → blue  badge accent
/// Otherwise "normal"   → default Surface0 badge
/// Used by the toolbar zoom badge DataTriggers.
/// </summary>
public class ZoomLevelConverter : MarkupExtension, IValueConverter
{
    private static ZoomLevelConverter? _instance;
    public static ZoomLevelConverter Instance => _instance ??= new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int pct)
        {
            if (pct < 50)  return "low";
            if (pct > 150) return "high";
        }
        return "normal";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => Instance;
}
