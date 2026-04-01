using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using CommandDeck.Models;
using CommandDeck.Services;
using CommandDeck.Services.Browser;
using CommandDeck.ViewModels;
using CommandDeck.Views;

namespace CommandDeck.Extensions;

/// <summary>
/// Extension methods that group DI registrations by functional domain,
/// keeping <see cref="App.xaml.cs"/> ConfigureServices lean and readable.
/// </summary>
public static class ServiceCollectionExtensions
{
    // ─── Terminal ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers terminal lifecycle, background rendering, and process monitoring services.
    /// </summary>
    public static IServiceCollection AddTerminalServices(this IServiceCollection services)
    {
        services.AddSingleton<ITerminalService, TerminalService>();
        services.AddSingleton<ITerminalBackgroundService, TerminalBackgroundService>();
        services.AddSingleton<IProcessMonitorService, ProcessMonitorService>();
        services.AddSingleton<ITerminalSessionService, TerminalSessionService>();
        return services;
    }

    // ─── AI / Assistant ───────────────────────────────────────────────────────

    /// <summary>
    /// Registers all AI assistant providers, the facade service, and related AI features.
    /// </summary>
    public static IServiceCollection AddAiServices(this IServiceCollection services)
    {
        services.AddSingleton<HttpClient>(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton<AssistantSettings>();
        services.AddSingleton<IClaudeOAuthService, ClaudeOAuthService>();

        // Multi-registration: each provider resolves as IAssistantProvider
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
        services.AddSingleton<IGitAiService, GitAiService>();

        // AI Orb
        services.AddSingleton<IAiOrbService, AiOrbService>();
        services.AddSingleton<IVoiceInputService, VoiceInputService>();

        // AI Terminal (cc/claude CLI)
        services.AddSingleton<IAiTerminalService, AiTerminalService>();
        services.AddSingleton<IAiContextService, AiContextService>();
        services.AddSingleton<IAiSessionHistoryService, AiSessionHistoryService>();
        services.AddSingleton<IAiContinuationService, AiContinuationService>();
        services.AddSingleton<IAiTerminalLauncher, AiTerminalLauncher>();
        services.AddSingleton<IAgentSelectorService, AgentSelectorService>();

        return services;
    }

    // ─── Browser ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers all services that support the integrated browser panel.
    /// </summary>
    public static IServiceCollection AddBrowserServices(this IServiceCollection services)
    {
        services.AddSingleton<IBrowserRuntimeService, BrowserRuntimeService>();
        services.AddSingleton<ILocalAppSessionService, LocalAppSessionService>();
        services.AddSingleton<IDomSelectionService, DomSelectionService>();
        services.AddSingleton<IAiContextRouter, AiContextRouter>();
        services.AddSingleton<ICdpService, CdpService>();
        services.AddSingleton<ICodeMappingService, CodeMappingService>();
        services.AddSingleton<ISelectionHistoryService, SelectionHistoryService>();
        services.AddSingleton<PortHealthCheckService>();
        return services;
    }

    // ─── Canvas ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers canvas, workspace, and layout services.
    /// </summary>
    public static IServiceCollection AddCanvasServices(this IServiceCollection services)
    {
        services.AddSingleton<ICanvasCameraService, CanvasCameraService>();
        services.AddSingleton<ILayoutPersistenceService, LayoutPersistenceService>();
        services.AddSingleton<CanvasItemFactory>();

        // Break the circular dependency (WorkspaceService → CanvasItemFactory → IWorkspaceService)
        // by injecting Lazy<IWorkspaceService> into CanvasItemFactory. The container resolves
        // Lazy<T> automatically; .Value is only evaluated when first accessed at runtime.
        services.AddSingleton(sp => new Lazy<IWorkspaceService>(() => sp.GetRequiredService<IWorkspaceService>()));
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<ICanvasItemsService>(sp => sp.GetRequiredService<IWorkspaceService>());
        services.AddSingleton<IWorkspaceLifecycleService>(sp => sp.GetRequiredService<IWorkspaceService>());

        services.AddSingleton<IWorkspaceTreeService, WorkspaceTreeService>();
        services.AddSingleton<IWorkspaceHierarchyService, WorkspaceHierarchyService>();

        services.AddSingleton<FreeCanvasLayoutStrategy>();
        services.AddSingleton<TiledLayoutStrategy>();
        return services;
    }

    // ─── Projects ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers project management, detection, and Git services.
    /// </summary>
    public static IServiceCollection AddProjectServices(this IServiceCollection services)
    {
        services.AddSingleton<IProjectDetectionService, ProjectDetectionService>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IGitService, GitService>();
        return services;
    }

    // ─── ViewModels ───────────────────────────────────────────────────────────

    /// <summary>
    /// Registers all ViewModels (Singletons and Transients) and their factory delegates.
    /// </summary>
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        // Singletons
        services.AddSingleton<MiniMapViewModel>();
        services.AddSingleton<WorkspaceTreeViewModel>();
        services.AddSingleton<CommandPaletteViewModel>();
        services.AddSingleton<BranchSelectorViewModel>();
        services.AddSingleton<WorktreeSelectorViewModel>();
        services.AddSingleton<TerminalCanvasViewModel>();
        services.AddSingleton<AssistantPanelViewModel>();
        services.AddSingleton<TerminalManagerViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ProjectListViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ProcessMonitorViewModel>();
        services.AddSingleton<BrowserViewModel>();
        services.AddSingleton<AiOrbViewModel>();
        services.AddSingleton<AgentSelectorViewModel>();
        services.AddSingleton<DynamicIslandViewModel>();

        // Transient: each request gets a fresh instance
        services.AddTransient<TerminalViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProjectEditViewModel>();

        // Lazy wrappers — break circular DI dependencies at resolution time
        services.AddSingleton(sp => new Lazy<TerminalCanvasViewModel>(() => sp.GetRequiredService<TerminalCanvasViewModel>()));
        services.AddSingleton(sp => new Lazy<DashboardViewModel>(() => sp.GetRequiredService<DashboardViewModel>()));
        services.AddSingleton(sp => new Lazy<BrowserViewModel>(() => sp.GetRequiredService<BrowserViewModel>()));
        services.AddSingleton(sp => new Lazy<WorkspaceTreeViewModel>(() => sp.GetRequiredService<WorkspaceTreeViewModel>()));
        services.AddSingleton(sp => new Lazy<ProjectListViewModel>(() => sp.GetRequiredService<ProjectListViewModel>()));
        services.AddSingleton(sp => new Lazy<MainViewModel>(() => sp.GetRequiredService<MainViewModel>()));

        // Factory delegates — allow MainViewModel to resolve transient VMs without IServiceProvider
        services.AddSingleton<Func<TerminalViewModel>>(sp => () => sp.GetRequiredService<TerminalViewModel>());
        services.AddSingleton<Func<SettingsViewModel>>(sp => () => sp.GetRequiredService<SettingsViewModel>());
        services.AddSingleton<Func<ProjectEditViewModel>>(sp => () => sp.GetRequiredService<ProjectEditViewModel>());

        return services;
    }
}
