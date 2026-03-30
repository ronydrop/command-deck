using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <summary>
/// User preferences and application settings, persisted to JSON.
/// </summary>
public partial class AppSettings : ObservableObject
{
    [ObservableProperty]
    private string _terminalFontFamily = "Cascadia Code, Consolas, Courier New";

    [ObservableProperty]
    private double _terminalFontSize = 14.0;

    [ObservableProperty]
    private ShellType _defaultShell = ShellType.WSL;

    [ObservableProperty]
    private string _projectScanDirectory = @"C:\Users";

    [ObservableProperty]
    private int _projectScanMaxDepth = 3;

    [ObservableProperty]
    private int _gitRefreshIntervalSeconds = 5;

    [ObservableProperty]
    private int _processMonitorIntervalSeconds = 3;

    [ObservableProperty]
    private bool _startWithLastProject = true;

    [ObservableProperty]
    private string _lastOpenedProjectId = string.Empty;

    [ObservableProperty]
    private double _windowWidth = 1400;

    [ObservableProperty]
    private double _windowHeight = 900;

    [ObservableProperty]
    private bool _isMaximized;

    // Keyboard shortcuts
    [ObservableProperty]
    private string _newTerminalShortcut = "Ctrl+Shift+T";

    [ObservableProperty]
    private string _closeTerminalShortcut = "Ctrl+Shift+W";

    [ObservableProperty]
    private string _nextTerminalShortcut = "Ctrl+Tab";

    [ObservableProperty]
    private string _previousTerminalShortcut = "Ctrl+Shift+Tab";

    [ObservableProperty]
    private string _toggleSidebarShortcut = "Ctrl+B";

    [ObservableProperty]
    private string _focusTerminalShortcut = "Ctrl+`";
}

/// <summary>
/// Service for loading and saving application settings.
/// </summary>
public class SettingsService
{
    private readonly string _settingsFilePath;
    private AppSettings? _settings;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public event Action<AppSettings>? SettingsChanged;

    public SettingsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DevWorkspaceHub");

        Directory.CreateDirectory(appDataPath);
        _settingsFilePath = Path.Combine(appDataPath, "settings.json");
    }

    /// <summary>
    /// Gets the current settings, loading from disk if necessary.
    /// </summary>
    public async Task<AppSettings> GetSettingsAsync()
    {
        if (_settings != null) return _settings;

        await _lock.WaitAsync();
        try
        {
            if (_settings != null) return _settings;
            _settings = await LoadSettingsAsync();
            return _settings;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Saves the current settings to disk.
    /// </summary>
    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            _settings = settings;
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(_settingsFilePath, json);
            SettingsChanged?.Invoke(settings);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Resets settings to defaults.
    /// </summary>
    public async Task ResetToDefaultsAsync()
    {
        var defaults = new AppSettings();
        await SaveSettingsAsync(defaults);
    }

    private async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = await File.ReadAllTextAsync(_settingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // Return defaults on error
        }

        return new AppSettings();
    }
}
