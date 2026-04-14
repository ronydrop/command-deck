using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using CommandDeck.Extensions;
using CommandDeck.Helpers;
using CommandDeck.Models;
using CommandDeck.Services;
using CommandDeck.ViewModels;
using CommandDeck.Views;

namespace CommandDeck;

/// <summary>
/// Application entry point with Dependency Injection setup using Microsoft.Extensions.DependencyInjection.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public static IServiceProvider Services =>
        ((App)Current)._serviceProvider ?? throw new InvalidOperationException("DI not initialized");

    public App()
    {
        var logPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CommandDeck", "perf.log");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
        System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(logPath));
        System.Diagnostics.Trace.AutoFlush = true;

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Global handlers: prevent silent crashes from unhandled exceptions
        DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            System.Diagnostics.Debug.WriteLine($"[UnhandledException] {e.Exception}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            System.Diagnostics.Debug.WriteLine($"[UnobservedTask] {e.Exception}");
        };
    }

    /// <summary>
    /// Registers all services, view models, and views in the DI container.
    /// Grouped by domain via extension methods in <see cref="ServiceCollectionExtensions"/>.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // ─── Infrastructure (shared by all domains) ─────────────────────
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IExternalEditorService, ExternalEditorService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IPaneStateService, PaneStateService>();
        services.AddSingleton<IAiAgentStateService, AiAgentStateService>();
        services.AddSingleton<ICommandPaletteService, CommandPaletteService>();
        services.AddSingleton<IUpdateService, UpdateService>();

        // ─── Persistence ─────────────────────────────────────────────────
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IPersistenceService, PersistenceService>();
        services.AddSingleton<ISecretStorageService, SecretStorageService>();
        services.AddSingleton<MigrationService>();

        // ─── Domain groups ───────────────────────────────────────────────
        services.AddTerminalServices();
        services.AddProjectServices();
        services.AddCanvasServices();
        services.AddAiServices();
        services.AddBrowserServices();
        services.AddMcpServices();

        // ─── Project Switch Service ──────────────────────────────────────
        // ViewModels are injected as Lazy<T> so the service never holds a
        // hard reference to them at construction time (avoids circular deps).
        services.AddSingleton<IProjectSwitchService, ProjectSwitchService>();

        // ─── ViewModels & Factories ──────────────────────────────────────
        services.AddViewModels();

        // ─── Windows & Views ─────────────────────────────────────────────
        services.AddSingleton<MainWindow>();
    }

    /// <summary>
    /// Raised after a theme is applied. The argument is the theme's BaseBg color.
    /// </summary>
    public static event Action<System.Windows.Media.Color>? ThemeApplied;

    /// <summary>
    /// Swaps the active theme ResourceDictionary (always at index 0 in MergedDictionaries).
    /// </summary>
    public static void ApplyTheme(string themeName)
    {
        var uri = new Uri($"Resources/Themes/{themeName}.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };
        Current.Resources.MergedDictionaries[0] = dict;

        if (Current.Resources["BaseBg"] is System.Windows.Media.Color baseBg)
            ThemeApplied?.Invoke(baseBg);
    }

    /// <summary>
    /// Applies a theme resolving the effective mode first.
    /// For <see cref="ThemeMode.System"/>, queries the OS appearance setting.
    /// If <paramref name="themeId"/> is incompatible with the resolved mode, falls back
    /// to <see cref="ThemeCatalog.DefaultThemeForMode"/>.
    /// </summary>
    public static void ApplyTheme(ThemeMode mode, string? themeId)
    {
        var effectiveMode = mode == ThemeMode.System
            ? SystemThemeDetector.GetSystemMode()
            : mode;

        var effectiveId = !string.IsNullOrEmpty(themeId) && ThemeCatalog.IsCompatible(themeId, effectiveMode)
            ? themeId
            : ThemeCatalog.DefaultThemeForMode(effectiveMode);

        ApplyTheme(effectiveId);
    }

    /// <summary>
    /// Re-applies the currently saved theme when the OS appearance changes while
    /// the app is running in <see cref="ThemeMode.System"/> mode.
    /// </summary>
    private static void OnSystemThemeModeChanged(object? sender, EventArgs e)
    {
        var settingsService = Services.GetService<ISettingsService>();
        var settings = settingsService?.CurrentSettings;
        if (settings?.ThemeMode != ThemeMode.System) return;

        var effectiveMode = SystemThemeDetector.GetSystemMode();
        var themeId = effectiveMode == ThemeMode.Light
            ? settings.LastLightTheme
            : settings.LastDarkTheme;

        Current.Dispatcher.Invoke(() => ApplyTheme(themeId));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize SQLite database and run pending migrations (with timeout to avoid hanging)
        try
        {
            var persistence = _serviceProvider!.GetRequiredService<IPersistenceService>();
            Task.Run(() => persistence.InitAsync()).Wait(TimeSpan.FromSeconds(10));

            // One-time migration from JSON files to SQLite
            var migration = _serviceProvider!.GetRequiredService<MigrationService>();
            Task.Run(() => migration.MigrateAsync()).Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] DB init/migration error: {ex}");
        }

        // Apply saved theme before showing window.
        // Task.Run avoids deadlock: GetSettingsAsync uses await internally which would capture
        // the WPF SynchronizationContext and try to resume on the blocked UI thread.
        try
        {
            var settingsService = _serviceProvider!.GetService<ISettingsService>();
            if (settingsService != null)
            {
                var settingsTask = Task.Run(() => settingsService.GetSettingsAsync());
                if (settingsTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    var settings = settingsTask.Result;
                    ApplyTheme(settings.ThemeMode, settings.ThemeName);
                }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App.OnStartup] Theme load failed: {ex.Message}"); }

        // Subscribe to OS theme changes so System mode re-applies when Windows switches dark ↔ light.
        SystemThemeDetector.SystemModeChanged += OnSystemThemeModeChanged;

        // Register Kanban tools and slash commands into their respective registries.
        // Must run after DI is built and canvas services are configured.
        try
        {
            KanbanToolsRegistrar.RegisterAll(_serviceProvider!);
            SlashCommandRegistrar.RegisterAll(_serviceProvider!);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Tool/slash registration error: {ex}");
        }

        // Resolve and show the main window
        try
        {
            var mainWindow = _serviceProvider!.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao iniciar a janela principal:\n\n{ex}", "CommandDeck - Erro de Inicialização", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Post-window initialization runs asynchronously to avoid blocking UI thread (deadlock).
        _ = InitializeServicesAsync();
    }

    /// <summary>
    /// Initializes post-window services asynchronously so the UI thread is not blocked.
    /// </summary>
    private async Task InitializeServicesAsync()
    {
        // Initialize SQLite database (creates file + tables if missing)
        try
        {
            var db = _serviceProvider!.GetRequiredService<IDatabaseService>();
            await db.InitializeAsync();
            var assistantService = _serviceProvider!.GetRequiredService<IAssistantService>();
            await assistantService.RestorePreferencesAsync();

            // Bridge: apply AppSettings AI config to AssistantService so the chat panel
            // uses whatever the user configured in Settings (provider, model, url, key).
            try
            {
                var settingsService = _serviceProvider!.GetRequiredService<ISettingsService>();
                var appSettings = await settingsService.GetSettingsAsync();
                var secretStorage = _serviceProvider!.GetRequiredService<ISecretStorageService>();
                var apiKey = string.Empty;
                var providerLower = appSettings.AiProvider?.ToLowerInvariant() ?? "none";
                var secretName = providerLower switch
                {
                    "anthropic" => "ai_anthropic_api_key",
                    "openrouter" => "ai_openrouter_api_key",
                    _ => "ai_openai_api_key"
                };
                try { apiKey = await secretStorage.RetrieveSecretAsync(secretName) ?? string.Empty; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App.InitializeServicesAsync] Secret retrieval failed ({secretName}): {ex.Message}"); }

                // For Ollama, use its dedicated base URL field; other providers ignore baseUrl.
                var baseUrl = providerLower == "local" ? appSettings.OllamaBaseUrl : string.Empty;

                System.Diagnostics.Debug.WriteLine($"[App] AI startup — provider={appSettings.AiProvider}, model={appSettings.AiModel}, baseUrl={baseUrl}");
                assistantService.ApplySettings(appSettings.AiProvider ?? string.Empty, appSettings.AiModel, baseUrl, apiKey, appSettings.AnthropicAuthMode);
            }
            catch (Exception ex2)
            {
                System.Diagnostics.Debug.WriteLine($"[App] AI settings bridge failed: {ex2}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] Init failed: {ex}");
        }

        // Initialize terminal background service (loads wallpaper from settings)
        try
        {
            var bgService = _serviceProvider!.GetRequiredService<ITerminalBackgroundService>();
            await bgService.InitializeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TerminalBackground] Init failed: {ex}");
        }

        // Initialize AI Orb (loads persisted position and provider)
        try
        {
            var aiOrb = _serviceProvider!.GetRequiredService<AiOrbViewModel>();
            await aiOrb.InitializeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiOrb] Init failed: {ex}");
        }

        // Start the local MCP server (exposes CommandDeck tools to AI agents)
        try
        {
            var mcpServer = _serviceProvider!.GetRequiredService<IMcpServerService>();
            _ = mcpServer.StartAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[McpServer] Start failed: {ex}");
        }

    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider != null)
        {
            // Save current project workspace layout before shutdown (with timeout)
            try
            {
                var mainVm = _serviceProvider.GetService<MainViewModel>();
                if (mainVm != null)
                    Task.Run(() => mainVm.CanvasViewModel.SaveCurrentLayoutAsync()).Wait(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App.OnExit] Layout save failed: {ex.Message}"); }

            // Close all terminal sessions before shutdown (with timeout)
            try
            {
                var terminalService = _serviceProvider.GetService<ITerminalService>();
                if (terminalService != null)
                    Task.Run(() => terminalService.CloseAllSessionsAsync()).Wait(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App.OnExit] Terminal sessions close failed: {ex.Message}"); }

            // Persist window state (with timeout)
            try
            {
                var settingsService = _serviceProvider.GetService<ISettingsService>();
                if (settingsService != null && MainWindow != null)
                {
                    var w = MainWindow.Width;
                    var h = MainWindow.Height;
                    var max = MainWindow.WindowState == WindowState.Maximized;
                    Task.Run(async () =>
                    {
                        var settings = await settingsService.GetSettingsAsync();
                        settings.WindowWidth = w;
                        settings.WindowHeight = h;
                        settings.IsMaximized = max;
                        await settingsService.SaveSettingsAsync(settings);
                    }).Wait(TimeSpan.FromSeconds(3));
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App.OnExit] Settings save failed: {ex.Message}"); }

            // Stop MCP server and clean up its config file before container disposal
            try
            {
                _serviceProvider.GetService<IMcpServerService>()?.Dispose();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App.OnExit] MCP server dispose failed: {ex.Message}"); }

            if (_serviceProvider is IDisposable disposable)
                disposable.Dispose();
        }

        base.OnExit(e);
    }
}
