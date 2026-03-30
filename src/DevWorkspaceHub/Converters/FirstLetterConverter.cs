using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace DevWorkspaceHub.Converters;

/// <summary>
/// Extracts the first letter from a string, uppercased.
/// Used for project avatar initials.
/// </summary>
public class FirstLetterConverter : MarkupExtension, IValueConverter
{
    private static FirstLetterConverter? _instance;
    public static FirstLetterConverter Instance => _instance ??= new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && s.Length > 0)
            return s[0].ToString().ToUpper();

        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    public override object ProvideValue(IServiceProvider serviceProvider) => Instance;
}
