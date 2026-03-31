using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace CommandDeck.Helpers;

/// <summary>
/// Utility methods for terminal background image processing:
/// validation, pre-blur, brightness/contrast adjustments.
/// </summary>
public static class ImageProcessor
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
    };

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const int MaxDimension = 4096;
    private const int DownscaleTarget = 1920;

    /// <summary>
    /// Validates an image file for use as terminal background.
    /// </summary>
    public static (bool IsValid, string? Error) ValidateImage(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return (false, "Caminho do arquivo vazio.");

        if (!File.Exists(filePath))
            return (false, "Arquivo não encontrado.");

        var ext = Path.GetExtension(filePath);
        if (!AllowedExtensions.Contains(ext))
            return (false, $"Formato não suportado: {ext}. Use PNG, JPG, BMP, GIF ou WebP.");

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MaxFileSizeBytes)
            return (false, $"Arquivo muito grande ({fileInfo.Length / (1024 * 1024):F1} MB). Máximo: 10 MB.");

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            if (bitmap.PixelWidth > MaxDimension || bitmap.PixelHeight > MaxDimension)
                return (false, $"Imagem muito grande ({bitmap.PixelWidth}x{bitmap.PixelHeight}). Máximo: {MaxDimension}x{MaxDimension}.");
        }
        catch (Exception ex)
        {
            return (false, $"Erro ao ler imagem: {ex.Message}");
        }

        return (true, null);
    }

    /// <summary>
    /// Loads a BitmapImage from file, frozen and cached in memory.
    /// Optionally downscales to DownscaleTarget for performance.
    /// </summary>
    public static BitmapImage LoadImage(string filePath, bool downscale = true)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;

        if (downscale)
            bitmap.DecodePixelWidth = DownscaleTarget;

        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Pre-blurs a BitmapSource by rendering it with a BlurEffect to a RenderTargetBitmap.
    /// The result is frozen and thread-safe — no per-frame GPU cost.
    /// </summary>
    public static BitmapSource PreBlur(BitmapSource source, double radius)
    {
        if (radius <= 0)
            return source;

        var width = source.PixelWidth;
        var height = source.PixelHeight;

        var image = new Image { Source = source };
        image.Effect = new BlurEffect { Radius = radius, RenderingBias = RenderingBias.Performance };
        image.Measure(new Size(width, height));
        image.Arrange(new Rect(0, 0, width, height));

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(image);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>
    /// Adjusts brightness of a BitmapSource by compositing a white (bright) or black (dark)
    /// overlay on a DrawingVisual. Brightness = 1.0 means no change.
    /// Range: 0.5 (darker) to 1.5 (brighter).
    /// </summary>
    public static BitmapSource AdjustBrightness(BitmapSource source, double brightness)
    {
        if (Math.Abs(brightness - 1.0) < 0.01)
            return source;

        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var rect = new Rect(0, 0, width, height);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Draw original image
            dc.DrawImage(source, rect);

            // Overlay: white for brightness > 1, black for brightness < 1
            if (brightness > 1.0)
            {
                var opacity = (brightness - 1.0); // 0.0 to 0.5
                var brush = new SolidColorBrush(Colors.White) { Opacity = opacity };
                brush.Freeze();
                dc.DrawRectangle(brush, null, rect);
            }
            else
            {
                var opacity = (1.0 - brightness); // 0.0 to 0.5
                var brush = new SolidColorBrush(Colors.Black) { Opacity = opacity };
                brush.Freeze();
                dc.DrawRectangle(brush, null, rect);
            }
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>
    /// Adjusts contrast of a BitmapSource using pixel manipulation.
    /// Contrast = 1.0 means no change. Range: 0.5 to 1.5.
    /// </summary>
    public static BitmapSource AdjustContrast(BitmapSource source, double contrast)
    {
        if (Math.Abs(contrast - 1.0) < 0.01)
            return source;

        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[height * stride];

        source.CopyPixels(pixels, stride, 0);

        var factor = contrast;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            // B, G, R, A (BGRA32 format)
            pixels[i] = ClampByte(((pixels[i] / 255.0 - 0.5) * factor + 0.5) * 255.0);
            pixels[i + 1] = ClampByte(((pixels[i + 1] / 255.0 - 0.5) * factor + 0.5) * 255.0);
            pixels[i + 2] = ClampByte(((pixels[i + 2] / 255.0 - 0.5) * factor + 0.5) * 255.0);
            // Alpha unchanged
        }

        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    /// <summary>
    /// Full processing pipeline: load → downscale → blur → brightness → contrast → freeze.
    /// Returns null if path is empty or file doesn't exist.
    /// </summary>
    public static BitmapSource? ProcessImage(string filePath, double blurRadius, double brightness, double contrast)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        try
        {
            BitmapSource image = LoadImage(filePath, downscale: true);
            image = PreBlur(image, blurRadius);
            image = AdjustBrightness(image, brightness);
            image = AdjustContrast(image, contrast);
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static byte ClampByte(double value)
    {
        if (value < 0) return 0;
        if (value > 255) return 255;
        return (byte)value;
    }
}
