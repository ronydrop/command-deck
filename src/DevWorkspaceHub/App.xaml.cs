using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.Services;
using DevWorkspaceHub.Services.Browser;
using DevWorkspaceHub.ViewModels;
using DevWorkspaceHub.Views;

namespace DevWorkspaceHub;

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

        // ─── Notification & Pane State ──────────────────────────────────
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IPaneStateService, PaneStateService>();
        services.AddSingleton<IAiAgentStateService, AiAgentStateService>();

        // ─── Spatial canvas services ─────────────────────────────────────
        services.AddSingleton<ICanvasCameraService, CanvasCameraService>();
        services.AddSingleton<ILayoutPersistenceService, LayoutPersistenceService>();
        services.AddSingleton<CanvasItemFactory>();

        // WorkspaceService needs CanvasItemFactory which needs IWorkspaceService — break cycle:
        // CanvasItemFactory has a SetWorkspaceService() method called after both are constructed.
        services.AddSingleton<IWorkspaceService>(sp =>
        {
            var factory = sp.GetRequiredService<CanvasItemFactory>();
            var persistence = sp.GetRequiredService<IPersistenceService>();
            var ws = new WorkspaceService(factory, persistence);
            factory.SetWorkspaceService(ws);
            return ws;
        });

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
        services.AddSingleton<IAssistantService, AssistantService>();

        // ─── AI Terminal (cc/claude CLI) ──────────────────────────────────
        services.AddSingleton<IAiTerminalService, AiTerminalService>();
        services.AddSingleton<IAiContextService, AiContextService>();
        services.AddSingleton<IAiModelConfigService, AiModelConfigService>();
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

        // ─── Export/Import ─────────────────────────────────────────────────
        services.AddSingleton<IWorkspaceExportService, WorkspaceExportService>();

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
    /// Swaps the active theme ResourceDictionary (always at index 0 in MergedDictionaries).
    /// </summary>
    public static void ApplyTheme(string themeName)
    {
        var uri = new Uri($"Resources/Themes/{themeName}.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };
        Current.Resources.MergedDictionaries[0] = dict;
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
        catch { }

        // Resolve and show the main window
        try
        {
            var mainWindow = _serviceProvider!.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao iniciar a janela principal:\n\n{ex}", "DevWorkspaceHub - Erro de Inicialização", MessageBoxButton.OK, MessageBoxImage.Error);
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
                try { apiKey = await secretStorage.RetrieveSecretAsync(secretName) ?? string.Empty; } catch { }
                assistantService.ApplySettings(appSettings.AiProvider, appSettings.AiModel, appSettings.AiBaseUrl, apiKey, appSettings.AnthropicAuthMode);
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
            catch { }

            // Close all terminal sessions before shutdown (with timeout)
            try
            {
                var terminalService = _serviceProvider.GetService<ITerminalService>();
                if (terminalService != null)
                    Task.Run(() => terminalService.CloseAllSessionsAsync()).Wait(TimeSpan.FromSeconds(3));
            }
            catch { }

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
            catch { }

            if (_serviceProvider is IDisposable disposable)
                disposable.Dispose();
        }

        base.OnExit(e);
    }
}
