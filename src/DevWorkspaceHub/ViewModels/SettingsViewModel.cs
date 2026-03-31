using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.Services;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// ViewModel para a tela de configurações.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IAiModelConfigService _aiModelConfigService;
    private readonly ISecretStorageService _secretStorageService;
    private readonly IUpdateService _updateService;
    private readonly IAssistantService _assistantService;
    private readonly ITerminalBackgroundService _terminalBackgroundService;

    // ─── Aparência ────────────────────────────────────────────────────────────

    private string _originalTheme = "LiquidGlassDark";

    [ObservableProperty]
    private string _selectedTheme = "LiquidGlassDark";

    partial void OnSelectedThemeChanged(string value)
    {
        App.ApplyTheme(value);
    }

    // ─── Terminal ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _terminalFontFamily = "Cascadia Code, Consolas, Courier New";

    [ObservableProperty]
    private double _terminalFontSize = 14.0;

    [ObservableProperty]
    private ShellType _defaultShell = ShellType.WSL;

    [ObservableProperty]
    private string _terminalResizeBehavior = "Auto";

    // ─── Projetos ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _projectScanDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [ObservableProperty]
    private int _projectScanMaxDepth = 3;

    [ObservableProperty]
    private bool _startWithLastProject = true;

    // ─── Monitoramento ───────────────────────────────────────────────────────

    [ObservableProperty]
    private int _gitRefreshIntervalSeconds = 5;

    [ObservableProperty]
    private int _processMonitorIntervalSeconds = 3;

    // ─── Atalhos de Teclado ──────────────────────────────────────────────────

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

    // ─── Assistente IA ───────────────────────────────────────────────────────

    [ObservableProperty]
    private string _aiProvider = "none";

    [ObservableProperty]
    private string _aiModel = "gpt-4o-mini";

    [ObservableProperty]
    private string _aiBaseUrl = string.Empty;

    [ObservableProperty]
    private int _aiMaxContextMessages = 20;

    [ObservableProperty]
    private bool _aiAssistantVisible;

    [ObservableProperty]
    private string _aiApiKey = string.Empty;

    // ─── Notificações ────────────────────────────────────────────────────────

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

    // ─── Canvas Wallpaper ───────────────────────────────────────────────

    [ObservableProperty]
    private string _canvasWallpaperPath = string.Empty;

    [ObservableProperty]
    private double _canvasWallpaperOpacity = 0.15;

    [ObservableProperty]
    private string _canvasWallpaperStretch = "UniformToFill";

    public bool HasWallpaper => !string.IsNullOrWhiteSpace(CanvasWallpaperPath);

    // ─── Canvas Zoom ────────────────────────────────────────────────────

    [ObservableProperty]
    private string _canvasZoomMode = "CtrlScroll";

    public IReadOnlyList<string> AvailableZoomModes { get; } = new[]
    {
        "CtrlScroll", "FreeScroll"
    };

    partial void OnCanvasWallpaperPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasWallpaper));
    }

    public IReadOnlyList<string> AvailableStretchModes { get; } = new[]
    {
        "None", "Fill", "Uniform", "UniformToFill"
    };

    [RelayCommand]
    private void BrowseWallpaper()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Selecionar Wallpaper",
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Todos|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            CanvasWallpaperPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void ClearWallpaper()
    {
        CanvasWallpaperPath = string.Empty;
    }

    // ─── Terminal Background ────────────────────────────────────────────

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

    [ObservableProperty]
    private string _terminalWallpaperStatusText = string.Empty;

    public bool HasTerminalWallpaper => !string.IsNullOrWhiteSpace(TerminalWallpaperPath);

    partial void OnTerminalWallpaperPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasTerminalWallpaper));
    }

    [RelayCommand]
    private async Task BrowseTerminalWallpaper()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Selecionar Fundo do Terminal",
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Todos|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            var (success, error) = await _terminalBackgroundService.ImportAndApplyImageAsync(dialog.FileName);
            if (success)
            {
                // Reload path from settings after import
                var settings = await _settingsService.GetSettingsAsync();
                TerminalWallpaperPath = settings.TerminalWallpaperPath;
                TerminalWallpaperStatusText = string.Empty;
            }
            else
            {
                TerminalWallpaperStatusText = error ?? "Erro ao importar imagem.";
            }
        }
    }

    [RelayCommand]
    private async Task ClearTerminalWallpaper()
    {
        await _terminalBackgroundService.ResetToDefaultAsync();
        TerminalWallpaperPath = string.Empty;
        TerminalWallpaperOpacity = 0.15;
        TerminalWallpaperBlurRadius = 0.0;
        TerminalWallpaperDarkOverlay = true;
        TerminalWallpaperOverlayOpacity = 0.4;
        TerminalWallpaperBrightness = 1.0;
        TerminalWallpaperContrast = 1.0;
        TerminalWallpaperStretch = "UniformToFill";
        TerminalWallpaperStatusText = string.Empty;
    }

    // ─── Sobre / Atualização ────────────────────────────────────────────────

    [ObservableProperty]
    private string _appVersion = "1.0.0";

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private string? _updateUrl;

    // ─── Coleções ────────────────────────────────────────────────────────────

    public IReadOnlyList<ShellType> AvailableShells { get; } =
        Enum.GetValues<ShellType>().Distinct().ToList();

    public IReadOnlyList<string> AvailableThemes { get; } = new[]
    {
        "CatppuccinMocha", "VSCodeDark", "VSCodeLight", "Dracula", "LiquidGlass", "LiquidGlassDark"
    };

    public IReadOnlyList<string> AvailableResizeBehaviors { get; } = new[]
    {
        "Auto", "Manual", "FixedCols"
    };

    public IReadOnlyList<string> AvailableAiProviders { get; } = new[]
    {
        "none", "anthropic", "openai", "local"
    };

    public ObservableCollection<AiSlotItemViewModel> AiModelSlots { get; } = new();

    // ─── Propriedades computadas (visibilidade condicional) ──────────────────

    public bool IsAiEnabled => AiProvider != "none";
    public bool IsOpenAiProvider => AiProvider == "openai";
    public bool IsAnthropicProvider => AiProvider == "anthropic";
    public bool IsLocalProvider => AiProvider == "local";
    public bool IsApiKeyProvider => IsOpenAiProvider || IsAnthropicProvider;

    partial void OnAiProviderChanged(string value)
    {
        OnPropertyChanged(nameof(IsAiEnabled));
        OnPropertyChanged(nameof(IsOpenAiProvider));
        OnPropertyChanged(nameof(IsAnthropicProvider));
        OnPropertyChanged(nameof(IsLocalProvider));
        OnPropertyChanged(nameof(IsApiKeyProvider));
    }

    /// <summary>
    /// Disparado quando o diálogo deve ser fechado. True = salvo, False = cancelado.
    /// </summary>
    public event Action<bool>? CloseRequested;

    public SettingsViewModel(
        ISettingsService settingsService,
        IAiModelConfigService aiModelConfigService,
        ISecretStorageService secretStorageService,
        IUpdateService updateService,
        IAssistantService assistantService,
        ITerminalBackgroundService terminalBackgroundService)
    {
        _settingsService = settingsService;
        _aiModelConfigService = aiModelConfigService;
        _secretStorageService = secretStorageService;
        _updateService = updateService;
        _assistantService = assistantService;
        _terminalBackgroundService = terminalBackgroundService;
        AppVersion = _updateService.CurrentVersion;
    }

    /// <summary>
    /// Carrega as configurações do serviço.
    /// </summary>
    public async Task InitializeAsync()
    {
        var settings = await _settingsService.GetSettingsAsync();

        // Aparência
        _originalTheme = settings.ThemeName;
        SelectedTheme = settings.ThemeName;

        // Terminal
        TerminalFontFamily = settings.TerminalFontFamily;
        TerminalFontSize = settings.TerminalFontSize;
        DefaultShell = settings.DefaultShell;
        TerminalResizeBehavior = settings.TerminalResizeBehavior;

        // Projetos
        ProjectScanDirectory = settings.ProjectScanDirectory;
        ProjectScanMaxDepth = settings.ProjectScanMaxDepth;
        StartWithLastProject = settings.StartWithLastProject;

        // Monitoramento
        GitRefreshIntervalSeconds = settings.GitRefreshIntervalSeconds;
        ProcessMonitorIntervalSeconds = settings.ProcessMonitorIntervalSeconds;

        // Atalhos
        NewTerminalShortcut = settings.NewTerminalShortcut;
        CloseTerminalShortcut = settings.CloseTerminalShortcut;
        NextTerminalShortcut = settings.NextTerminalShortcut;
        PreviousTerminalShortcut = settings.PreviousTerminalShortcut;
        ToggleSidebarShortcut = settings.ToggleSidebarShortcut;
        FocusTerminalShortcut = settings.FocusTerminalShortcut;

        // Assistente IA
        AiProvider = settings.AiProvider;
        AiModel = settings.AiModel;
        AiBaseUrl = settings.AiBaseUrl;
        AiMaxContextMessages = settings.AiMaxContextMessages;
        AiAssistantVisible = settings.AiAssistantVisible;

        // Notificações
        NotificationsEnabled = settings.NotificationsEnabled;
        NotifyTerminalEvents = settings.NotifyTerminalEvents;
        NotifyGitEvents = settings.NotifyGitEvents;
        NotifyProcessEvents = settings.NotifyProcessEvents;
        NotifyAiEvents = settings.NotifyAiEvents;
        NotifySystemEvents = settings.NotifySystemEvents;
        NotificationSoundEnabled = settings.NotificationSoundEnabled;

        // Canvas Wallpaper
        CanvasWallpaperPath = settings.CanvasWallpaperPath;
        CanvasWallpaperOpacity = settings.CanvasWallpaperOpacity;
        CanvasWallpaperStretch = settings.CanvasWallpaperStretch;

        // Canvas Zoom
        CanvasZoomMode = settings.CanvasZoomMode;

        // Terminal Background
        TerminalWallpaperPath = settings.TerminalWallpaperPath;
        TerminalWallpaperOpacity = settings.TerminalWallpaperOpacity;
        TerminalWallpaperBlurRadius = settings.TerminalWallpaperBlurRadius;
        TerminalWallpaperDarkOverlay = settings.TerminalWallpaperDarkOverlay;
        TerminalWallpaperOverlayOpacity = settings.TerminalWallpaperOverlayOpacity;
        TerminalWallpaperBrightness = settings.TerminalWallpaperBrightness;
        TerminalWallpaperContrast = settings.TerminalWallpaperContrast;
        TerminalWallpaperStretch = settings.TerminalWallpaperStretch;

        // API key (from secure storage — load based on provider)
        try
        {
            var secretName = AiProvider == "anthropic" ? "ai_anthropic_api_key" : "ai_openai_api_key";
            AiApiKey = await _secretStorageService.RetrieveSecretAsync(secretName) ?? string.Empty;
        }
        catch
        {
            AiApiKey = string.Empty;
        }

        // Model slots
        LoadModelSlots();
    }

    private void LoadModelSlots()
    {
        AiModelSlots.Clear();
        var slots = _aiModelConfigService.GetAllSlots();
        var activeSlot = _aiModelConfigService.ActiveSlot;

        foreach (var cfg in slots)
        {
            AiModelSlots.Add(new AiSlotItemViewModel
            {
                Slot = cfg.Slot,
                DisplayName = cfg.DisplayName,
                ShortLabel = cfg.ShortLabel,
                ModelId = cfg.ModelId,
                IsActive = cfg.Slot == activeSlot
            });
        }
    }

    /// <summary>
    /// Salva as configurações e fecha o diálogo.
    /// </summary>
    [RelayCommand]
    private async Task Save()
    {
        // Clamp values
        TerminalFontSize = Math.Clamp(TerminalFontSize, 8.0, 32.0);
        GitRefreshIntervalSeconds = Math.Clamp(GitRefreshIntervalSeconds, 1, 300);
        ProcessMonitorIntervalSeconds = Math.Clamp(ProcessMonitorIntervalSeconds, 1, 300);
        ProjectScanMaxDepth = Math.Clamp(ProjectScanMaxDepth, 1, 10);
        AiMaxContextMessages = Math.Clamp(AiMaxContextMessages, 1, 100);

        var settings = await _settingsService.GetSettingsAsync();

        // Aparência
        settings.ThemeName = SelectedTheme;

        // Terminal
        settings.TerminalFontFamily = string.IsNullOrWhiteSpace(TerminalFontFamily)
            ? "Cascadia Code, Consolas, Courier New"
            : TerminalFontFamily.Trim();
        settings.TerminalFontSize = TerminalFontSize;
        settings.DefaultShell = DefaultShell;
        settings.TerminalResizeBehavior = TerminalResizeBehavior;

        // Projetos
        settings.ProjectScanDirectory = ProjectScanDirectory;
        settings.ProjectScanMaxDepth = ProjectScanMaxDepth;
        settings.StartWithLastProject = StartWithLastProject;

        // Monitoramento
        settings.GitRefreshIntervalSeconds = GitRefreshIntervalSeconds;
        settings.ProcessMonitorIntervalSeconds = ProcessMonitorIntervalSeconds;

        // Atalhos
        settings.NewTerminalShortcut = NewTerminalShortcut;
        settings.CloseTerminalShortcut = CloseTerminalShortcut;
        settings.NextTerminalShortcut = NextTerminalShortcut;
        settings.PreviousTerminalShortcut = PreviousTerminalShortcut;
        settings.ToggleSidebarShortcut = ToggleSidebarShortcut;
        settings.FocusTerminalShortcut = FocusTerminalShortcut;

        // Assistente IA
        settings.AiProvider = AiProvider;
        settings.AiModel = AiModel;
        settings.AiBaseUrl = AiBaseUrl;
        settings.AiMaxContextMessages = AiMaxContextMessages;
        settings.AiAssistantVisible = AiAssistantVisible;

        // Notificações
        settings.NotificationsEnabled = NotificationsEnabled;
        settings.NotifyTerminalEvents = NotifyTerminalEvents;
        settings.NotifyGitEvents = NotifyGitEvents;
        settings.NotifyProcessEvents = NotifyProcessEvents;
        settings.NotifyAiEvents = NotifyAiEvents;
        settings.NotifySystemEvents = NotifySystemEvents;
        settings.NotificationSoundEnabled = NotificationSoundEnabled;

        // Canvas Wallpaper
        settings.CanvasWallpaperPath = CanvasWallpaperPath;
        settings.CanvasWallpaperOpacity = Math.Clamp(CanvasWallpaperOpacity, 0.0, 1.0);
        settings.CanvasWallpaperStretch = CanvasWallpaperStretch;

        // Canvas Zoom
        settings.CanvasZoomMode = CanvasZoomMode;

        // Terminal Background
        settings.TerminalWallpaperPath = TerminalWallpaperPath;
        settings.TerminalWallpaperOpacity = Math.Clamp(TerminalWallpaperOpacity, 0.0, 1.0);
        settings.TerminalWallpaperBlurRadius = Math.Clamp(TerminalWallpaperBlurRadius, 0.0, 30.0);
        settings.TerminalWallpaperDarkOverlay = TerminalWallpaperDarkOverlay;
        settings.TerminalWallpaperOverlayOpacity = Math.Clamp(TerminalWallpaperOverlayOpacity, 0.0, 1.0);
        settings.TerminalWallpaperBrightness = Math.Clamp(TerminalWallpaperBrightness, 0.5, 1.5);
        settings.TerminalWallpaperContrast = Math.Clamp(TerminalWallpaperContrast, 0.5, 1.5);
        settings.TerminalWallpaperStretch = TerminalWallpaperStretch;

        // Salvar API key (secure storage)
        try
        {
            if (AiProvider == "openai" && !string.IsNullOrWhiteSpace(AiApiKey))
                await _secretStorageService.StoreSecretAsync("ai_openai_api_key", AiApiKey);
            else if (AiProvider == "anthropic" && !string.IsNullOrWhiteSpace(AiApiKey))
                await _secretStorageService.StoreSecretAsync("ai_anthropic_api_key", AiApiKey);
        }
        catch { }

        // Sincronizar model slots
        foreach (var slot in AiModelSlots)
        {
            _aiModelConfigService.SetModelForSlot(slot.Slot, slot.ModelId);
            if (slot.IsActive)
                _aiModelConfigService.SetActiveSlot(slot.Slot);
        }

        App.ApplyTheme(SelectedTheme);

        // Propagate AI settings to the AssistantService BEFORE saving so that when
        // SettingsChanged fires the assistant is already configured with the new values.
        _assistantService.ApplySettings(AiProvider, AiModel, AiBaseUrl, AiApiKey);

        await _settingsService.SaveSettingsAsync(settings);

        CloseRequested?.Invoke(true);
    }

    /// <summary>
    /// Cancela sem salvar.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        App.ApplyTheme(_originalTheme);
        CloseRequested?.Invoke(false);
    }

    /// <summary>
    /// Checks GitHub for a newer release.
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdate()
    {
        IsCheckingUpdate = true;
        UpdateStatusText = "Verificando...";
        HasUpdate = false;

        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            if (result.HasUpdate)
            {
                HasUpdate = true;
                UpdateUrl = result.ReleaseUrl;
                UpdateStatusText = $"Nova versão disponível: v{result.LatestVersion}";
            }
            else
            {
                UpdateStatusText = "Você está usando a versão mais recente.";
            }
        }
        catch
        {
            UpdateStatusText = "Erro ao verificar atualizações.";
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    /// <summary>
    /// Opens the latest release page in the default browser.
    /// </summary>
    [RelayCommand]
    private void OpenUpdatePage()
    {
        if (!string.IsNullOrEmpty(UpdateUrl))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = UpdateUrl,
                UseShellExecute = true
            });
        }
    }

    /// <summary>
    /// Restaura os valores padrão e recarrega.
    /// </summary>
    [RelayCommand]
    private async Task ResetDefaults()
    {
        await _settingsService.ResetToDefaultsAsync();

        // Reset model slots to defaults
        _aiModelConfigService.SetModelForSlot(AiModelSlot.Sonnet, "sonnet");
        _aiModelConfigService.SetModelForSlot(AiModelSlot.Opus, "opus");
        _aiModelConfigService.SetModelForSlot(AiModelSlot.Haiku, "haiku");
        _aiModelConfigService.SetModelForSlot(AiModelSlot.Agent, "agent");
        _aiModelConfigService.SetActiveSlot(AiModelSlot.Sonnet);

        await InitializeAsync();
    }
}
