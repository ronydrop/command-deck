using System.Windows;
using Microsoft.Extensions.DependencyInjection;
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

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
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
        services.AddSingleton<SettingsService>();

        // ─── ViewModels ─────────────────────────────────────────────────
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ProjectListViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ProcessMonitorViewModel>();

        // Transient: each request gets a fresh instance
        services.AddTransient<ProjectEditViewModel>();

        // ─── Views ──────────────────────────────────────────────────────
        services.AddSingleton<MainWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Resolve and show the main window
        var mainWindow = _serviceProvider!.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // Clean up terminal sessions
        if (_serviceProvider != null)
        {
            var terminalService = _serviceProvider.GetService<ITerminalService>();
            if (terminalService != null)
            {
                await terminalService.CloseAllSessionsAsync();
            }

            // Save window state
            try
            {
                var settingsService = _serviceProvider.GetService<SettingsService>();
                if (settingsService != null && MainWindow != null)
                {
                    var settings = await settingsService.GetSettingsAsync();
                    settings.WindowWidth = MainWindow.Width;
                    settings.WindowHeight = MainWindow.Height;
                    settings.IsMaximized = MainWindow.WindowState == WindowState.Maximized;
                    await settingsService.SaveSettingsAsync(settings);
                }
            }
            catch
            {
                // Don't fail exit on settings save error
            }

            if (_serviceProvider is IDisposable disposable)
                disposable.Dispose();
        }

        base.OnExit(e);
    }
}
