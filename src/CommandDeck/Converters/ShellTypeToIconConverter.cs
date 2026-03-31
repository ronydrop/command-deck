using System;
using System.Globalization;
using System.Windows.Data;
using CommandDeck.Models;

namespace CommandDeck.Converters;

/// <summary>Converts <see cref="ShellType"/> to the full display name.</summary>
public class ShellTypeToNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ShellType shellType)
            return shellType.GetDisplayName();
        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts <see cref="ShellType"/> to a short symbol used in the sidebar.</summary>
public class ShellTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ShellType t)
        {
            return t switch
            {
                ShellType.WSL => ">_",
                ShellType.PowerShell => "PS",
                ShellType.CMD => "CMD",
                ShellType.GitBash => "GIT",
                _ => ">_"
            };
        }
        return ">_";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
