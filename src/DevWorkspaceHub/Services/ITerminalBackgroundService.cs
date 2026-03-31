using System.Windows.Media;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Manages terminal background wallpaper: loading, processing, caching, and notifying consumers.
/// </summary>
public interface ITerminalBackgroundService
{
    /// <summary>Pre-processed (blurred, adjusted) image ready for binding. Null when no wallpaper.</summary>
    ImageSource? ProcessedImage { get; }

    /// <summary>Current wallpaper opacity (0.0–1.0).</summary>
    double Opacity { get; }

    /// <summary>Current overlay opacity (0.0–1.0).</summary>
    double OverlayOpacity { get; }

    /// <summary>True for dark overlay, false for light overlay.</summary>
    bool IsDarkOverlay { get; }

    /// <summary>Stretch mode for the wallpaper image.</summary>
    Stretch WallpaperStretch { get; }

    /// <summary>Whether a wallpaper is currently configured and loaded.</summary>
    bool HasWallpaper { get; }

    /// <summary>Raised when any background property changes. Consumers should refresh their bindings.</summary>
    event Action? BackgroundChanged;

    /// <summary>Loads settings and processes the image on startup.</summary>
    Task InitializeAsync();

    /// <summary>Imports an image file to the wallpapers folder and applies it.</summary>
    Task<(bool Success, string? Error)> ImportAndApplyImageAsync(string sourceFilePath);

    /// <summary>Reprocesses the current image with updated settings (blur, brightness, contrast).</summary>
    Task ReprocessImageAsync();

    /// <summary>Clears the wallpaper and resets all terminal background settings to defaults.</summary>
    Task ResetToDefaultAsync();
}
