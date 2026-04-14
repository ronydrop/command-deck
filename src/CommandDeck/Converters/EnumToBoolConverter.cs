using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CommandDeck.Converters;

/// <summary>
/// Converts between an enum value and a boolean, enabling RadioButton binding with enums.
/// Usage: IsChecked="{Binding MyEnumProp, Converter={StaticResource EnumToBool}, ConverterParameter=EnumValueName}"
/// </summary>
[ValueConversion(typeof(Enum), typeof(bool))]
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Only update the source when the RadioButton becomes checked (true).
        // When it becomes unchecked (false), do nothing — the other RadioButton
        // will fire its own ConvertBack and update the source.
        if (value is bool b && b && parameter != null)
        {
            try { return Enum.Parse(targetType, parameter.ToString()!); }
            catch { /* fall through to DoNothing */ }
        }
        return Binding.DoNothing;
    }
}
