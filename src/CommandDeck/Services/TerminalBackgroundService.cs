using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommandDeck.Helpers;

namespace CommandDeck.Services;

/// <summary>
/// Manages terminal background wallpaper processing and caching.
/// Singleton service — all terminals share the same processed image.
/// </summary>
public class TerminalBackgroundService : ITerminalBackgroundService
{
    private readonly ISettingsService _settingsService;
    private readonly string _wallpapersDir;
    private readonly Dispatcher _dispatcher;

    public ImageSource? ProcessedImage { get; private set; }
    public double Opacity { get; private set; }
    public double OverlayOpacity { get; private set; }
    public bool IsDarkOverlay { get; private set; } = true;
    public Stretch WallpaperStretch { get; private set; } = Stretch.UniformToFill;
    public bool HasWallpaper { get; private set; }

    public event Action? BackgroundChanged;

    public TerminalBackgroundService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _wallpapersDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CommandDeck", "wallpapers");

        Directory.CreateDirectory(_wallpapersDir);

        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    public async Task InitializeAsync()
    {
        var settings = await _settingsService.GetSettingsAsync();
        await ApplyFromSettingsAsync(settings);
    }

    public async Task<(bool Success, string? Error)> ImportAndApplyImageAsync(string sourceFilePath)
    {
        var (isValid, error) = ImageProcessor.ValidateImage(sourceFilePath);
        if (!isValid)
            return (false, error);

        var ext = Path.GetExtension(sourceFilePath);
        var destFileName = $"{Guid.NewGuid()}{ext}";
        var destPath = Path.Combine(_wallpapersDir, destFileName);

        try
        {
            File.Copy(sourceFilePath, destPath, overwrite: true);
        }
        catch (Exception ex)
        {
            return (false, $"Erro ao copiar imagem: {ex.Message}");
        }

        var settings = await _settingsService.GetSettingsAsync();

        // Remove old wallpaper file if different
        CleanupOldWallpaper(settings.TerminalWallpaperPath, destPath);

        settings.TerminalWallpaperPath = destPath;
        await _settingsService.SaveSettingsAsync(settings);

        await ApplyFromSettingsAsync(settings);
        return (true, null);
    }

    public async Task ReprocessImageAsync()
    {
        var settings = await _settingsService.GetSettingsAsync();
        await ApplyFromSettingsAsync(settings);
    }

    public async Task ResetToDefaultAsync()
    {
        var settings = await _settingsService.GetSettingsAsync();

        CleanupOldWallpaper(settings.TerminalWallpaperPath, string.Empty);

        settings.TerminalWallpaperPath = string.Empty;
        settings.TerminalWallpaperOpacity = 0.15;
        settings.TerminalWallpaperBlurRadius = 0.0;
        settings.TerminalWallpaperDarkOverlay = true;
        settings.TerminalWallpaperOverlayOpacity = 0.4;
        settings.TerminalWallpaperBrightness = 1.0;
        settings.TerminalWallpaperContrast = 1.0;
        settings.TerminalWallpaperStretch = "UniformToFill";

        await _settingsService.SaveSettingsAsync(settings);

        _dispatcher.Invoke(() =>
        {
            ProcessedImage = null;
            HasWallpaper = false;
            Opacity = 0.15;
            OverlayOpacity = 0.4;
            IsDarkOverlay = true;
            WallpaperStretch = Stretch.UniformToFill;
            BackgroundChanged?.Invoke();
        });
    }

    private async Task ApplyFromSettingsAsync(AppSettings settings)
    {
        var path = settings.TerminalWallpaperPath;
        var blur = settings.TerminalWallpaperBlurRadius;
        var brightness = settings.TerminalWallpaperBrightness;
        var contrast = settings.TerminalWallpaperContrast;
        var opacity = settings.TerminalWallpaperOpacity;
        var overlayOpacity = settings.TerminalWallpaperOverlayOpacity;
        var darkOverlay = settings.TerminalWallpaperDarkOverlay;
        var stretchStr = settings.TerminalWallpaperStretch;

        // Process image on a background thread
        BitmapSource? processed = null;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            processed = await Task.Run(() => ImageProcessor.ProcessImage(path, blur, brightness, contrast));
        }

        var stretch = stretchStr switch
        {
            "None" => Stretch.None,
            "Fill" => Stretch.Fill,
            "Uniform" => Stretch.Uniform,
            _ => Stretch.UniformToFill
        };

        _dispatcher.Invoke(() =>
        {
            ProcessedImage = processed;
            HasWallpaper = processed != null;
            Opacity = opacity;
            OverlayOpacity = overlayOpacity;
            IsDarkOverlay = darkOverlay;
            WallpaperStretch = stretch;
            BackgroundChanged?.Invoke();
        });
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        // Fire-and-forget; errors are silently swallowed
        _ = ApplyFromSettingsAsync(settings);
    }

    private void CleanupOldWallpaper(string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || oldPath == newPath)
            return;

        // Only delete if it's inside our managed wallpapers folder
        if (oldPath.StartsWith(_wallpapersDir, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(oldPath); } catch { /* best effort */ }
        }
    }
}
