using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.Services;
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

        // ─── Spatial canvas services ─────────────────────────────────────
        services.AddSingleton<ICanvasCameraService, CanvasCameraService>();
        services.AddSingleton<ILayoutPersistenceService, LayoutPersistenceService>();
        services.AddSingleton<CanvasItemFactory>();

        // WorkspaceService needs CanvasItemFactory which needs IWorkspaceService — break cycle:
        // CanvasItemFactory has a SetWorkspaceService() method called after both are constructed.
        services.AddSingleton<IWorkspaceService>(sp =>
        {
            var factory = sp.GetRequiredService<CanvasItemFactory>();
            var ws = new WorkspaceService(factory);
            factory.SetWorkspaceService(ws);
            return ws;
        });

        // ─── Advanced services ───────────────────────────────────────────
        services.AddSingleton<IWorkspaceTreeService, WorkspaceTreeService>();
        services.AddSingleton<ICommandPaletteService, CommandPaletteService>();

        // ─── AI Assistant services ───────────────────────────────────────
        services.AddSingleton<HttpClient>(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton<AssistantSettings>();
        services.AddSingleton<IAssistantProvider, OllamaProvider>();
        services.AddSingleton<IAssistantProvider, OpenAIProviderStub>(); // multiregistro
        services.AddSingleton<IAssistantService, AssistantService>();

        // ─── AI Terminal (cc/claude CLI) ──────────────────────────────────
        services.AddSingleton<IAiTerminalService, AiTerminalService>();
        services.AddSingleton<IAiContextService, AiContextService>();
        services.AddSingleton<IAiModelConfigService, AiModelConfigService>();
        services.AddSingleton<IAiSessionHistoryService, AiSessionHistoryService>();
        services.AddSingleton<IAiContinuationService, AiContinuationService>();

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

        // Transient: each request gets a fresh instance
        services.AddTransient<TerminalViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProjectEditViewModel>();

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

        // Apply saved theme before showing window.
        // Task.Run avoids deadlock: GetSettingsAsync uses await internally which would capture
        // the WPF SynchronizationContext and try to resume on the blocked UI thread.
        try
        {
            var settingsService = _serviceProvider!.GetService<ISettingsService>();
            if (settingsService != null)
            {
                var settings = Task.Run(() => settingsService.GetSettingsAsync()).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(settings.ThemeName))
                    ApplyTheme(settings.ThemeName);
            }
        }
        catch { }

        // Resolve and show the main window
        var mainWindow = _serviceProvider!.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        // Initialize SQLite database (creates file + tables if missing)
        try
        {
            var db = _serviceProvider!.GetRequiredService<IDatabaseService>();
            Task.Run(() => db.InitializeAsync()).GetAwaiter().GetResult();
            var assistantService = _serviceProvider!.GetRequiredService<IAssistantService>();
            Task.Run(() => assistantService.RestorePreferencesAsync()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] Init failed: {ex}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider != null)
        {
            try
            {
                // Close all terminal sessions before shutdown
                var terminalService = _serviceProvider.GetService<ITerminalService>();
                terminalService?.CloseAllSessionsAsync().GetAwaiter().GetResult();
            }
            catch { }

            try
            {
                // Persist window state
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
                    }).GetAwaiter().GetResult();
                }
            }
            catch { }

            if (_serviceProvider is IDisposable disposable)
                disposable.Dispose();
        }

        base.OnExit(e);
    }
}
