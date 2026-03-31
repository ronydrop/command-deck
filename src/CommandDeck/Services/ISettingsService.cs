namespace CommandDeck.Services;

/// <summary>
/// Provides access to application settings with load, save and reset operations.
/// </summary>
public interface ISettingsService
{
    /// <summary>Gets the current settings, loading from disk on first access.</summary>
    Task<AppSettings> GetSettingsAsync();

    /// <summary>
    /// Returns the already-loaded settings synchronously (never blocks).
    /// Returns a default instance if settings have not been loaded yet.
    /// </summary>
    AppSettings CurrentSettings { get; }

    /// <summary>Persists the provided settings to disk.</summary>
    Task SaveSettingsAsync(AppSettings settings);

    /// <summary>Resets all settings to their default values and persists them.</summary>
    Task ResetToDefaultsAsync();

    /// <summary>Raised after settings are successfully saved.</summary>
    event Action<AppSettings>? SettingsChanged;
}
