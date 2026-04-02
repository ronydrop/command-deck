using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CommandDeck.Converters;

/// <summary>
/// Converts a file path string to an ImageSource.
/// Returns null for null or empty strings, avoiding the WPF
/// ImageSourceConverter error when binding empty strings to Image.Source.
/// </summary>
public class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute));
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
