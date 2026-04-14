using CommandDeck.Models;
using CommandDeck.Services;
using CommandDeck.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CommandDeck.Helpers;

/// <summary>
/// Registers all built-in commands for the Command Palette.
/// Call this once during application startup after all services are available.
/// </summary>
public static class CommandPaletteRegistrar
{
    /// <summary>
    /// Registers all built-in commands and returns the CommandPaletteViewModel.
    /// Must be called from the UI thread after DI container is built.
    /// </summary>
    public static CommandPaletteViewModel RegisterBuiltInCommands(IServiceProvider serviceProvider)
    {
        var commandService = serviceProvider.GetRequiredService<ICommandPaletteService>();
        var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();

        // ═══════════════════════════════════════════════════════════════
        // TERMINAL COMMANDS
        // ═══════════════════════════════════════════════════════════════

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "terminal.new",
            Title = "New Terminal",
            Category = "Terminal",
            Shortcut = "Ctrl+Shift+T",
            Icon = "\uE756", // Terminal icon (Segoe MDL2)
            IsEnabled = null,
            Action = () => mainViewModel.NewTerminalCommand.ExecuteAsync(null),
            Keywords = "terminal novo criar shell"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "terminal.close",
            Title = "Close Terminal",
            Category = "Terminal",
            Shortcut = "Ctrl+Shift+W",
            Icon = "\uE711", // Close icon
            IsEnabled = () => mainViewModel.ActiveTerminal is not null,
            Action = () => mainViewModel.CloseTerminalCommand.ExecuteAsync(mainViewModel.ActiveTerminal),
            Keywords = "terminal fechar encerrar"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "terminal.clear",
            Title = "Clear Terminal",
            Category = "Terminal",
            Icon = "\uE8E5", // Clear icon
            IsEnabled = () => mainViewModel.ActiveTerminal is not null,
            Action = async () =>
            {
                if (mainViewModel.ActiveTerminal is not null)
                {
                    mainViewModel.ActiveTerminal.ClearOutputCommand?.Execute(null);
                    await Task.CompletedTask;
                }
            },
            Keywords = "terminal limpar clear reset"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "terminal.switch.next",
            Title = "Switch to Next Terminal",
            Category = "Terminal",
            Shortcut = "Ctrl+Tab",
            Icon = "\uE8AB", // Next icon
            IsEnabled = () => mainViewModel.Terminals.Count > 1,
            Action = () =>
            {
                mainViewModel.NextTerminalCommand.Execute(null);
                return Task.CompletedTask;
            },
            Keywords = "terminal próximo next tab switch"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "terminal.switch.prev",
            Title = "Switch to Previous Terminal",
            Category = "Terminal",
            Shortcut = "Ctrl+Shift+Tab",
            Icon = "\uE892", // Previous icon
            IsEnabled = () => mainViewModel.Terminals.Count > 1,
            Action = () =>
            {
                mainViewModel.PreviousTerminalCommand.Execute(null);
                return Task.CompletedTask;
            },
            Keywords = "terminal anterior previous tab switch"
        });

        // ═══════════════════════════════════════════════════════════════
        // NAVIGATION COMMANDS
        // ═══════════════════════════════════════════════════════════════

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "nav.toggleSidebar",
            Title = "Toggle Sidebar",
            Category = "Navigation",
            Shortcut = "Ctrl+B",
            Icon = "\uE8C4", // Toggle pane icon
            Action = () =>
            {
                mainViewModel.ToggleSidebarCommand.Execute(null);
                return Task.CompletedTask;
            },
            Keywords = "sidebar barra lateral painel toggle"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "nav.focusTerminal",
            Title = "Focus Terminal",
            Category = "Navigation",
            Icon = "\uE756", // Terminal icon
            IsEnabled = () => mainViewModel.ActiveTerminal is not null,
            Action = () =>
            {
                mainViewModel.NavigateToCommand.Execute(ViewType.Terminal);
                return Task.CompletedTask;
            },
            Keywords = "terminal focar focus ir"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "nav.goDashboard",
            Title = "Go to Dashboard",
            Category = "Navigation",
            Icon = "\uE80F", // Home/dashboard icon
            Action = () =>
            {
                mainViewModel.NavigateToCommand.Execute(ViewType.Dashboard);
                return Task.CompletedTask;
            },
            Keywords = "dashboard painel home início"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "nav.goProcessMonitor",
            Title = "Go to Process Monitor",
            Category = "Navigation",
            Icon = "\uE918", // Monitor icon
            Action = () =>
            {
                mainViewModel.NavigateToCommand.Execute(ViewType.ProcessMonitor);
                return Task.CompletedTask;
            },
            Keywords = "processos monitor process"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "nav.openSettings",
            Title = "Go to Settings",
            Category = "Navigation",
            Icon = "\uE713", // Settings icon
            Action = () => mainViewModel.OpenSettingsCommand.ExecuteAsync(null),
            Keywords = "configurações settings preferências"
        });

        // ═══════════════════════════════════════════════════════════════
        // WORKSPACE COMMANDS
        // ═══════════════════════════════════════════════════════════════

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "workspace.newGroup",
            Title = "New Group",
            Category = "Workspace",
            Icon = "\uE710", // New folder/group
            Keywords = "workspace grupo grupo novo group",
            Action = () =>
            {
                // TODO: Implement workspace grouping
                System.Diagnostics.Debug.WriteLine("[CommandPalette] New Group - not yet implemented");
                return Task.CompletedTask;
            }
        });

        // ═══════════════════════════════════════════════════════════════
        // PROJECT COMMANDS
        // ═══════════════════════════════════════════════════════════════

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "project.scan",
            Title = "Scan for Projects",
            Category = "Project",
            Icon = "\uE943", // Search icon
            Action = async () =>
            {
                var projectService = serviceProvider.GetRequiredService<IProjectService>();
                var settingsService = serviceProvider.GetRequiredService<ISettingsService>();
                var settings = await settingsService.GetSettingsAsync();
                await projectService.ScanForProjectsAsync(settings.ProjectScanDirectory, settings.ProjectScanMaxDepth);
            },
            Keywords = "projetos scan buscar detectar scan"
        });

        // ═══════════════════════════════════════════════════════════════
        // AI TERMINAL COMMANDS
        // ═══════════════════════════════════════════════════════════════

        var aiTerminalService = serviceProvider.GetRequiredService<IAiTerminalService>();
        var terminalSessionService = serviceProvider.GetRequiredService<ITerminalSessionService>();
        var workspaceService = serviceProvider.GetRequiredService<IWorkspaceService>();
        var aiContextService = serviceProvider.GetRequiredService<IAiContextService>();
        var aiHistory = serviceProvider.GetRequiredService<IAiSessionHistoryService>();
        var aiLauncher = serviceProvider.GetRequiredService<IAiTerminalLauncher>();
        var aiCommandExecutor = serviceProvider.GetRequiredService<IAiCommandExecutor>();
        var chatTileRouter = serviceProvider.GetRequiredService<ChatTileRouter>();

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.launch.claude",
            Title = "AI: Open Claude",
            Category = "AI",
            Shortcut = "Ctrl+Shift+A",
            Icon = "\uE945",
            Action = () => aiLauncher.LaunchAsync(AiSessionType.Claude),
            Keywords = "ai claude terminal agent"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.launch.claude.shortcut",
            Title = "AI: Open Claude (Alt shortcut)",
            Category = "AI",
            Shortcut = "Ctrl+Shift+C",
            Icon = "\uE945",
            Action = () => aiLauncher.LaunchAsync(AiSessionType.Claude),
            Keywords = "ai claude direto direct"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.launch.claude.resume",
            Title = "AI: Resume Claude Session",
            Category = "AI",
            Icon = "\uE945",
            Action = () => aiLauncher.LaunchAsync(AiSessionType.ClaudeResume),
            Keywords = "ai claude resume retomar continuar session"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.launch.codex",
            Title = "AI: Open Codex",
            Category = "AI",
            Icon = "\uE945",
            Action = () => aiLauncher.LaunchAsync(AiSessionType.Codex),
            Keywords = "ai codex openai agent"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.launch.aider",
            Title = "AI: Open Aider",
            Category = "AI",
            Icon = "\uE945",
            Action = () => aiLauncher.LaunchAsync(AiSessionType.Aider),
            Keywords = "ai aider pair programming"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.launch.gemini",
            Title = "AI: Open Gemini",
            Category = "AI",
            Icon = "\uE945",
            Action = () => aiLauncher.LaunchAsync(AiSessionType.Gemini),
            Keywords = "ai gemini google"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.launch.copilot",
            Title = "AI: Open Copilot",
            Category = "AI",
            Icon = "\uE945",
            Action = () => aiLauncher.LaunchAsync(AiSessionType.Copilot),
            Keywords = "ai copilot github"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.explain.output",
            Title = "AI: Explain Terminal Output",
            Category = "AI",
            Icon = "\uE946",
            IsEnabled = () => mainViewModel.ActiveTerminal is not null,
            Action = async () =>
            {
                var sourceSessionId = mainViewModel.ActiveTerminal?.Session?.Id ?? "";
                var correlationId = Guid.NewGuid().ToString("N")[..12];

                await chatTileRouter.RouteUserMessageAsync("Explique o output do terminal ativo.");

                aiHistory.Record(new AiSessionHistoryEntry
                {
                    SessionId = sourceSessionId,
                    Intent = AiPromptIntent.ExplainOutput,
                    ModelUsed = "default",
                    PromptSent = "(explain output)",
                    Source = AiActionSource.CommandPalette,
                    CorrelationId = correlationId,
                    ExecutionStatus = AiExecutionStatus.Running
                });
            },
            Keywords = "ai explicar erro output terminal explain"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.fix.error",
            Title = "AI: Fix Last Error",
            Category = "AI",
            Icon = "\uE90F",
            IsEnabled = () => mainViewModel.ActiveTerminal?.Session is not null,
            Action = async () =>
            {
                var activeSessionId = mainViewModel.ActiveTerminal?.Session?.Id ?? "";
                await aiCommandExecutor.FixLastErrorAsync(activeSessionId);
            },
            Keywords = "ai fix corrigir erro error terminal"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.send.output",
            Title = "AI: Send Output to AI Terminal",
            Category = "AI",
            Icon = "\uE8A7",
            IsEnabled = () => mainViewModel.ActiveTerminal?.Session is not null,
            Action = async () =>
            {
                var activeSessionId = mainViewModel.ActiveTerminal?.Session?.Id ?? "";
                await aiCommandExecutor.SendOutputToAiAsync(activeSessionId);
            },
            Keywords = "ai enviar output terminal send"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.suggest.command",
            Title = "AI: Suggest Command",
            Category = "AI",
            Icon = "\uE946",
            IsEnabled = () => mainViewModel.ActiveTerminal?.Session is not null,
            Action = async () =>
            {
                var sourceSessionId = mainViewModel.ActiveTerminal?.Session?.Id ?? "";
                var correlationId = Guid.NewGuid().ToString("N")[..12];
                var prompt = await aiContextService.BuildPromptAsync(AiPromptIntent.SuggestCommand);

                var routePrompt = !string.IsNullOrWhiteSpace(prompt)
                    ? $"Sugira um comando para: {prompt}"
                    : "Sugira um comando shell útil para a situação atual.";
                await chatTileRouter.RouteMessageAsync(routePrompt, autoSend: false);

                aiHistory.Record(new AiSessionHistoryEntry
                {
                    SessionId = sourceSessionId,
                    Intent = AiPromptIntent.SuggestCommand,
                    ModelUsed = "default",
                    PromptSent = prompt ?? "",
                    Source = AiActionSource.CommandPalette,
                    CorrelationId = correlationId,
                    ExecutionStatus = AiExecutionStatus.Running
                });
            },
            Keywords = "ai sugerir comando suggest command gerar"
        });

        // ═══════════════════════════════════════════════════════════════
        // Create and return the ViewModel
        // ═══════════════════════════════════════════════════════════════

        return serviceProvider.GetRequiredService<CommandPaletteViewModel>();
    }

}
