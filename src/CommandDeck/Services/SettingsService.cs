using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// User preferences and application settings, persisted to JSON.
/// </summary>
public partial class AppSettings : ObservableObject
{
    [ObservableProperty]
    private string _terminalFontFamily = "Cascadia Code, Consolas, Courier New";

    [ObservableProperty]
    private double _terminalFontSize = 14.0;

    /// <summary>
    /// Controls how the terminal resizes when the card is dragged.
    /// Auto: columns/rows from font metrics and control size.
    /// Manual: user-defined fixed size.
    /// FixedCols: columns fixed, rows adjust to height.
    /// </summary>
    [ObservableProperty]
    private string _terminalResizeBehavior = "Auto";

    [ObservableProperty]
    private ShellType _defaultShell = ShellType.WSL;

    [ObservableProperty]
    private string _projectScanDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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

    [ObservableProperty]
    private string _themeName = "LiquidGlassDark";

    // ─── AI Assistant Settings ────────────────────────────────────────────

    /// <summary>
    /// Active AI provider: "openai", "local", or "none".
    /// </summary>
    [ObservableProperty]
    private string _aiProvider = "none";

    /// <summary>
    /// Model identifier for the active AI provider (e.g., "gpt-4o-mini", "llama3").
    /// </summary>
    [ObservableProperty]
    private string _aiModel = "gpt-4o-mini";

    /// <summary>
    /// Override the default API base URL for the active provider.
    /// Leave empty to use the provider's default endpoint.
    /// </summary>
    [ObservableProperty]
    private string _aiBaseUrl = string.Empty;

    /// <summary>
    /// Per-provider model for Anthropic.
    /// </summary>
    [ObservableProperty]
    private string _anthropicProviderModel = "claude-sonnet-4-6-20250627";

    /// <summary>
    /// Per-provider model for OpenAI.
    /// </summary>
    [ObservableProperty]
    private string _openAiProviderModel = "gpt-4o-mini";

    /// <summary>
    /// Per-provider model for OpenRouter.
    /// </summary>
    [ObservableProperty]
    private string _openRouterProviderModel = "anthropic/claude-sonnet-4.6";

    /// <summary>
    /// Per-provider model for Ollama.
    /// </summary>
    [ObservableProperty]
    private string _ollamaProviderModel = "llama3.2";

    /// <summary>
    /// Base URL for the local Ollama instance.
    /// </summary>
    [ObservableProperty]
    private string _ollamaBaseUrl = "http://localhost:11434";

    /// <summary>
    /// Anthropic auth mode: "apikey" or "claude_oauth".
    /// </summary>
    [ObservableProperty]
    private string _anthropicAuthMode = "apikey";

    /// <summary>
    /// Whether the AI assistant panel is visible in the UI.
    /// </summary>
    [ObservableProperty]
    private bool _aiAssistantVisible;


    // ─── Notification Settings ───────────────────────────────────────────

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private bool _notifyTerminalEvents = true;

    [ObservableProperty]
    private bool _notifyGitEvents = true;

    [ObservableProperty]
    private bool _notifyProcessEvents = true;

    [ObservableProperty]
    private bool _notifyAiEvents = true;

    [ObservableProperty]
    private bool _notifySystemEvents = true;

    [ObservableProperty]
    private bool _notificationSoundEnabled;

    // ─── Canvas Wallpaper Settings ──────────────────────────────────────

    [ObservableProperty]
    private string _defaultAgentId = "cc";

    [ObservableProperty]
    private string _canvasWallpaperPath = string.Empty;

    [ObservableProperty]
    private double _canvasWallpaperOpacity = 0.15;

    [ObservableProperty]
    private string _canvasWallpaperStretch = "UniformToFill";

    // ─── AI Orb Settings ────────────────────────────────────────────────────

    [ObservableProperty]
    private double _aiOrbPositionX = 32;

    [ObservableProperty]
    private double _aiOrbPositionY = 400;

    // ─── Canvas Zoom Settings ──────────────────────────────────────────

    [ObservableProperty]
    private string _canvasZoomMode = "CtrlScroll";

    // ─── Terminal Background Settings ──────────────────────────────────

    [ObservableProperty]
    private string _terminalWallpaperPath = string.Empty;

    [ObservableProperty]
    private double _terminalWallpaperOpacity = 0.15;

    [ObservableProperty]
    private double _terminalWallpaperBlurRadius = 0.0;

    [ObservableProperty]
    private bool _terminalWallpaperDarkOverlay = true;

    [ObservableProperty]
    private double _terminalWallpaperOverlayOpacity = 0.4;

    [ObservableProperty]
    private double _terminalWallpaperBrightness = 1.0;

    [ObservableProperty]
    private double _terminalWallpaperContrast = 1.0;

    [ObservableProperty]
    private string _terminalWallpaperStretch = "UniformToFill";
}

/// <summary>
/// Service for loading and saving application settings.
/// </summary>
public class SettingsService : ISettingsService, IDisposable
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
            "CommandDeck");

        Directory.CreateDirectory(appDataPath);
        _settingsFilePath = Path.Combine(appDataPath, "settings.json");
    }

    /// <summary>
    /// Gets the current settings, loading from disk if necessary.
    /// </summary>
    public async Task<AppSettings> GetSettingsAsync()
    {
        if (_settings != null) return _settings;

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_settings != null) return _settings;
            _settings = await LoadSettingsAsync().ConfigureAwait(false);
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
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(_settingsFilePath, json).ConfigureAwait(false);
            _settings = settings;  // Only cache after successful write
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

    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = await File.ReadAllTextAsync(_settingsFilePath).ConfigureAwait(false);
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
