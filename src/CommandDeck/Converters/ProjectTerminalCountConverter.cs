using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CommandDeck.Converters;

/// <summary>
/// Multi-value converter that resolves the terminal count for a project.
/// values[0] = Project.Id (string), values[1] = Dictionary&lt;string, int&gt; (TerminalCountsByProjectId).
/// Pass "Visibility" as parameter to return Visibility instead of a count string.
/// </summary>
public class ProjectTerminalCountConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values[0] is not string id || values[1] is not Dictionary<string, int> counts)
        {
            return parameter?.ToString() == "Visibility" ? Visibility.Collapsed : "0";
        }

        var count = counts.TryGetValue(id, out var c) ? c : 0;

        if (parameter?.ToString() == "Visibility")
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;

        return count.ToString();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
