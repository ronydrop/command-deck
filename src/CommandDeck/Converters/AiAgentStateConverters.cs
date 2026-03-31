using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CommandDeck.Models;

namespace CommandDeck.Converters;

/// <summary>Converts <see cref="AiAgentState"/> to its emoji icon.</summary>
public class AiAgentStateToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is AiAgentState state ? AiAgentStateChangedArgs.GetIcon(state) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts <see cref="AiAgentState"/> to a short label.</summary>
public class AiAgentStateToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is AiAgentState state ? AiAgentStateChangedArgs.GetLabel(state) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts <see cref="AiAgentState"/> to a color brush.</summary>
public class AiAgentStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Purple  = Freeze(Color.FromRgb(0xCB, 0xA6, 0xF7)); // Mauve
    private static readonly SolidColorBrush Blue    = Freeze(Color.FromRgb(0x89, 0xB4, 0xFA)); // Blue
    private static readonly SolidColorBrush Yellow  = Freeze(Color.FromRgb(0xF9, 0xE2, 0xAF)); // Yellow
    private static readonly SolidColorBrush Teal    = Freeze(Color.FromRgb(0x94, 0xE2, 0xD5)); // Teal
    private static readonly SolidColorBrush Green   = Freeze(Color.FromRgb(0xA6, 0xE3, 0xA1)); // Green
    private static readonly SolidColorBrush Red     = Freeze(Color.FromRgb(0xF3, 0x8B, 0xA8)); // Red
    private static readonly SolidColorBrush Default = Freeze(Color.FromRgb(0x6C, 0x70, 0x86)); // Overlay0

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is AiAgentState state ? state switch
        {
            AiAgentState.Thinking     => Purple,
            AiAgentState.Executing    => Blue,
            AiAgentState.WaitingUser  => Yellow,
            AiAgentState.WaitingInput => Teal,
            AiAgentState.Completed    => Green,
            AiAgentState.Error        => Red,
            _                         => Default
        } : Default;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// <summary>Converts <see cref="AiAgentState"/> to Visibility (Visible when not Idle).</summary>
public class AiAgentStateToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is AiAgentState state && state != AiAgentState.Idle
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
