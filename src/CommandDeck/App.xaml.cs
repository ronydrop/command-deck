using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using CommandDeck.Models;
using CommandDeck.Services;
using CommandDeck.Services.Browser;
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
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // ─── Services (Singletons for shared state) ─────────────────────
        services.AddSingleton<ITerminalService, TerminalService>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IGitService, GitService>();
        services.AddSingleton<IProcessMonitorService, ProcessMonitorService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IExternalEditorService, ExternalEditorService>();

        // ─── Notification & Pane State ──────────────────────────────────
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IPaneStateService, PaneStateService>();
        services.AddSingleton<IAiAgentStateService, AiAgentStateService>();

        // ─── Spatial canvas services ─────────────────────────────────────
        services.AddSingleton<ICanvasCameraService, CanvasCameraService>();
        services.AddSingleton<ILayoutPersistenceService, LayoutPersistenceService>();
        services.AddSingleton<CanvasItemFactory>();

        // Break the circular dependency (WorkspaceService → CanvasItemFactory → IWorkspaceService)
        // by injecting Lazy<IWorkspaceService> into CanvasItemFactory. The container resolves
        // Lazy<T> automatically; .Value is only evaluated when first accessed at runtime.
        services.AddSingleton(sp => new Lazy<IWorkspaceService>(() => sp.GetRequiredService<IWorkspaceService>()));
        services.AddSingleton<IWorkspaceService, WorkspaceService>();

        // ─── Advanced services ───────────────────────────────────────────
        services.AddSingleton<IWorkspaceTreeService, WorkspaceTreeService>();
        services.AddSingleton<ICommandPaletteService, CommandPaletteService>();

        // ─── AI Assistant services ───────────────────────────────────────
        services.AddSingleton<HttpClient>(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton<AssistantSettings>();
        services.AddSingleton<IClaudeOAuthService, ClaudeOAuthService>();
        services.AddSingleton<IAssistantProvider>(sp => new OllamaProvider(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<AssistantSettings>()));
        services.AddSingleton<IAssistantProvider>(sp => new OpenAIProvider(
            sp.GetService<ISecretStorageService>(),
            sp.GetService<ISettingsService>(),
            sp.GetRequiredService<AssistantSettings>()));
        services.AddSingleton<IAssistantProvider>(sp => new AnthropicProvider(
            sp.GetService<ISecretStorageService>(),
            sp.GetService<ISettingsService>(),
            sp.GetRequiredService<AssistantSettings>(),
            sp.GetService<IClaudeOAuthService>()));
        services.AddSingleton<IAssistantProvider>(sp => new OpenRouterProvider(
            sp.GetService<ISecretStorageService>(),
            sp.GetRequiredService<AssistantSettings>()));
        services.AddSingleton<IClaudeUsageService, ClaudeUsageService>();
        services.AddSingleton<IAssistantService, AssistantService>();

        // ─── Dynamic Island ──────────────────────────────────────────────
        services.AddSingleton<DynamicIslandViewModel>();
        services.AddSingleton<DynamicIslandWindow>();

        // ─── AI Orb ──────────────────────────────────────────────────────
        services.AddSingleton<IAiOrbService, AiOrbService>();
        services.AddSingleton<IVoiceInputService, VoiceInputService>();
        services.AddSingleton<AiOrbViewModel>();

        // ─── AI Terminal (cc/claude CLI) ──────────────────────────────────
        services.AddSingleton<IAiTerminalService, AiTerminalService>();
        services.AddSingleton<IAiContextService, AiContextService>();
        services.AddSingleton<IAiSessionHistoryService, AiSessionHistoryService>();
        services.AddSingleton<IAiContinuationService, AiContinuationService>();
        services.AddSingleton<IAiTerminalLauncher, AiTerminalLauncher>();
        services.AddSingleton<IAgentSelectorService, AgentSelectorService>();
        services.AddSingleton<AgentSelectorViewModel>();

        // ─── SQLite persistence (additive — does not replace JSON) ────────
        services.AddSingleton<IDatabaseService, DatabaseService>();

        // ─── New Persistence (SQLite) ─────────────────────────────────────
        services.AddSingleton<IPersistenceService, PersistenceService>();
        services.AddSingleton<ISecretStorageService, SecretStorageService>();
        services.AddSingleton<MigrationService>();

        // ─── Terminal Sessions ─────────────────────────────────────────────
        services.AddSingleton<ITerminalSessionService, TerminalSessionService>();

        // ─── Workspace Hierarchy ───────────────────────────────────────────
        services.AddSingleton<IWorkspaceHierarchyService, WorkspaceHierarchyService>();

        // ─── Update Checker ───────────────────────────────────────────────
        services.AddSingleton<IUpdateService, UpdateService>();

        // ─── Terminal Background ─────────────────────────────────────────
        services.AddSingleton<ITerminalBackgroundService, TerminalBackgroundService>();

        // ─── Browser Services ────────────────────────────────────────────
        services.AddSingleton<IBrowserRuntimeService, BrowserRuntimeService>();
        services.AddSingleton<ILocalAppSessionService, LocalAppSessionService>();
        services.AddSingleton<IDomSelectionService, DomSelectionService>();
        services.AddSingleton<IAiContextRouter, AiContextRouter>();
        services.AddSingleton<ICdpService, CdpService>();
        services.AddSingleton<ICodeMappingService, CodeMappingService>();
        services.AddSingleton<ISelectionHistoryService, SelectionHistoryService>();
        services.AddSingleton<PortHealthCheckService>();

        // ─── Layout Strategies ──────────────────────────────────────────
        services.AddSingleton<FreeCanvasLayoutStrategy>();
        services.AddSingleton<TiledLayoutStrategy>();

        // ─── ViewModels ─────────────────────────────────────────────────
        services.AddSingleton<MiniMapViewModel>();
        services.AddSingleton<WorkspaceTreeViewModel>();
        services.AddSingleton<CommandPaletteViewModel>();
        services.AddSingleton<BranchSelectorViewModel>();
        services.AddSingleton<WorktreeSelectorViewModel>();
        services.AddSingleton<TerminalCanvasViewModel>();
        services.AddSingleton<AssistantPanelViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ProjectListViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ProcessMonitorViewModel>();
        services.AddSingleton<BrowserViewModel>();

        // Transient: each request gets a fresh instance
        services.AddTransient<TerminalViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProjectEditViewModel>();

        // Lazy wrappers to break circular DI dependencies
        services.AddSingleton(sp => new Lazy<MainViewModel>(() => sp.GetRequiredService<MainViewModel>()));

        // Factories: allow MainViewModel to resolve transient VMs without knowing IServiceProvider
        services.AddSingleton<Func<TerminalViewModel>>(sp => () => sp.GetRequiredService<TerminalViewModel>());
        services.AddSingleton<Func<SettingsViewModel>>(sp => () => sp.GetRequiredService<SettingsViewModel>());
        services.AddSingleton<Func<ProjectEditViewModel>>(sp => () => sp.GetRequiredService<ProjectEditViewModel>());

        // ─── Views ──────────────────────────────────────────────────────
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
                    if (!string.IsNullOrEmpty(settings.ThemeName))
                        ApplyTheme(settings.ThemeName);
                }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App.OnStartup] Theme load failed: {ex.Message}"); }

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

        // Show Dynamic Island overlay (non-activating, always-on-top)
        try
        {
            var island = _serviceProvider!.GetRequiredService<DynamicIslandWindow>();
            island.Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DynamicIsland] Window show failed: {ex}");
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
                assistantService.ApplySettings(appSettings.AiProvider, appSettings.AiModel, baseUrl, apiKey, appSettings.AnthropicAuthMode);
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

        // Initialize Dynamic Island (loads existing sessions + persisted visibility)
        try
        {
            var island = _serviceProvider!.GetRequiredService<DynamicIslandViewModel>();
            await island.InitializeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DynamicIsland] Init failed: {ex}");
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

            try
            {
                var island = _serviceProvider.GetService<DynamicIslandViewModel>();
                island?.Dispose();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App.OnExit] DynamicIsland dispose failed: {ex.Message}"); }

            if (_serviceProvider is IDisposable disposable)
                disposable.Dispose();
        }

        base.OnExit(e);
    }
}
