using DevWorkspaceHub.Models;
using DevWorkspaceHub.Services;
using DevWorkspaceHub.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DevWorkspaceHub.Helpers;

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
            Keywords = "terminal proximo next tab switch"
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
            Keywords = "dashboard painel home inicio"
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
            Keywords = "configuracoes settings preferencias"
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
        var aiModelConfig = serviceProvider.GetRequiredService<IAiModelConfigService>();
        var aiHistory = serviceProvider.GetRequiredService<IAiSessionHistoryService>();
        var aiLauncher = serviceProvider.GetRequiredService<IAiTerminalLauncher>();

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.launch.cc",
            Title = "AI: Open cc",
            Category = "AI",
            Shortcut = "Ctrl+Shift+A",
            Icon = "\uE945",
            Action = () => aiLauncher.LaunchAsync(AiSessionType.Cc),
            Keywords = "ai cc claude terminal agent"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.launch.sonnet",
            Title = "AI: Run Sonnet",
            Category = "AI",
            Icon = "\uE945",
            Action = () => aiLauncher.LaunchAsync(AiSessionType.CcRun, "sonnet"),
            Keywords = "ai sonnet modelo model"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.launch.opus",
            Title = "AI: Run Opus",
            Category = "AI",
            Icon = "\uE945",
            Action = () => aiLauncher.LaunchAsync(AiSessionType.CcRun, "opus"),
            Keywords = "ai opus modelo model"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.launch.haiku",
            Title = "AI: Run Haiku",
            Category = "AI",
            Icon = "\uE945",
            Action = () => aiLauncher.LaunchAsync(AiSessionType.CcRun, "haiku"),
            Keywords = "ai haiku rapido fast"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.launch.agent",
            Title = "AI: Run Agent",
            Category = "AI",
            Icon = "\uE945",
            Action = () => aiLauncher.LaunchAsync(AiSessionType.CcRun, "agent"),
            Keywords = "ai agent autonomo autonomous"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.launch.openrouter",
            Title = "AI: OpenRouter",
            Category = "AI",
            Icon = "\uE945",
            Action = () => aiLauncher.LaunchAsync(AiSessionType.CcOpenRouter),
            Keywords = "ai openrouter or modelos picker"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.launch.claude",
            Title = "AI: Open Claude",
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
            Id = "ai.explain.output",
            Title = "AI: Explain Terminal Output",
            Category = "AI",
            Icon = "\uE946",
            IsEnabled = () => mainViewModel.ActiveTerminal is not null,
            Action = () =>
            {
                var sourceSessionId = mainViewModel.ActiveTerminal?.Session?.Id ?? "";
                var correlationId = Guid.NewGuid().ToString("N")[..12];

                mainViewModel.IsAIPanelOpen = true;
                mainViewModel.AssistantPanel.ExplainOutputCommand.Execute(null);

                aiHistory.Record(new AiSessionHistoryEntry
                {
                    SessionId = sourceSessionId,
                    Intent = AiPromptIntent.ExplainOutput,
                    ModelUsed = aiModelConfig.GetActiveModelOrAlias(),
                    PromptSent = "(panel explain)",
                    Source = AiActionSource.CommandPalette,
                    CorrelationId = correlationId,
                    ExecutionStatus = AiExecutionStatus.Running
                });

                return Task.CompletedTask;
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
                var prompt = await aiContextService.BuildPromptAsync(AiPromptIntent.FixError);
                if (string.IsNullOrWhiteSpace(prompt)) return;

                var sourceSessionId = mainViewModel.ActiveTerminal?.Session?.Id ?? "";
                var correlationId = Guid.NewGuid().ToString("N")[..12];
                var routing = aiModelConfig.RecommendModel(AiPromptIntent.FixError);

                aiHistory.Record(new AiSessionHistoryEntry
                {
                    SessionId = sourceSessionId,
                    Intent = AiPromptIntent.FixError,
                    ModelUsed = routing.ModelOrAlias,
                    PromptSent = prompt,
                    Source = AiActionSource.CommandPalette,
                    CorrelationId = correlationId,
                    ExecutionStatus = AiExecutionStatus.Running
                });

                try
                {
                    var cli = await aiTerminalService.DetectCliAsync();
                    if (cli.CcAvailable)
                    {
                        await aiLauncher.LaunchAsync(AiSessionType.CcRun, routing.ModelOrAlias);
                        await Task.Delay(1500);
                        var activeSession = mainViewModel.ActiveTerminal?.Session;
                        if (activeSession is not null)
                            await terminalSessionService.WriteAsync(activeSession.Id, prompt + "\n");
                    }
                    else
                    {
                        mainViewModel.IsAIPanelOpen = true;
                        mainViewModel.AssistantPanel.InputText = prompt;
                        mainViewModel.AssistantPanel.SendMessageCommand.Execute(null);
                    }
                    aiHistory.UpdateStatus(sourceSessionId, correlationId, AiExecutionStatus.Completed);
                }
                catch
                {
                    aiHistory.UpdateStatus(sourceSessionId, correlationId, AiExecutionStatus.Failed);
                }
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
                var sourceSessionId = mainViewModel.ActiveTerminal?.Session?.Id ?? "";
                var correlationId = Guid.NewGuid().ToString("N")[..12];
                var prompt = await aiContextService.BuildPromptAsync(AiPromptIntent.SendContext, outputLines: 50);
                if (string.IsNullOrWhiteSpace(prompt)) return;

                aiHistory.Record(new AiSessionHistoryEntry
                {
                    SessionId = sourceSessionId,
                    Intent = AiPromptIntent.SendContext,
                    ModelUsed = aiModelConfig.GetActiveModelOrAlias(),
                    PromptSent = prompt,
                    Source = AiActionSource.CommandPalette,
                    CorrelationId = correlationId,
                    ExecutionStatus = AiExecutionStatus.Running
                });

                try
                {
                    await aiLauncher.LaunchAsync(AiSessionType.Cc);
                    await Task.Delay(1500);
                    var activeSession = mainViewModel.ActiveTerminal?.Session;
                    if (activeSession is not null)
                        await terminalSessionService.WriteAsync(activeSession.Id, prompt + "\n");
                    aiHistory.UpdateStatus(sourceSessionId, correlationId, AiExecutionStatus.Completed);
                }
                catch
                {
                    aiHistory.UpdateStatus(sourceSessionId, correlationId, AiExecutionStatus.Failed);
                }
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
                if (!string.IsNullOrWhiteSpace(prompt))
                    mainViewModel.AssistantPanel.InputText = prompt;

                mainViewModel.IsAIPanelOpen = true;
                mainViewModel.AssistantPanel.SuggestCommandCommand.Execute(null);

                aiHistory.Record(new AiSessionHistoryEntry
                {
                    SessionId = sourceSessionId,
                    Intent = AiPromptIntent.SuggestCommand,
                    ModelUsed = aiModelConfig.GetActiveModelOrAlias(),
                    PromptSent = prompt ?? "",
                    Source = AiActionSource.CommandPalette,
                    CorrelationId = correlationId,
                    ExecutionStatus = AiExecutionStatus.Running
                });
            },
            Keywords = "ai sugerir comando suggest command gerar"
        });

        // ─── AI Model Switch commands ─────────────────────────────────
        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.model.use.sonnet",
            Title = $"AI: Use Sonnet (active slot)",
            Category = "AI Model",
            Icon = "\uE8AB",
            Action = () =>
            {
                aiModelConfig.SetActiveSlot(AiModelSlot.Sonnet);
                return Task.CompletedTask;
            },
            Keywords = "ai modelo sonnet trocar switch"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.model.use.opus",
            Title = "AI: Use Opus (active slot)",
            Category = "AI Model",
            Icon = "\uE8AB",
            Action = () =>
            {
                aiModelConfig.SetActiveSlot(AiModelSlot.Opus);
                return Task.CompletedTask;
            },
            Keywords = "ai modelo opus trocar switch"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.model.use.haiku",
            Title = "AI: Use Haiku (active slot)",
            Category = "AI Model",
            Icon = "\uE8AB",
            Action = () =>
            {
                aiModelConfig.SetActiveSlot(AiModelSlot.Haiku);
                return Task.CompletedTask;
            },
            Keywords = "ai modelo haiku trocar switch rapido fast"
        });

        commandService.RegisterCommand(new CommandDefinition
        {
            Id = "ai.model.use.agent",
            Title = "AI: Use Agent (active slot)",
            Category = "AI Model",
            Icon = "\uE8AB",
            Action = () =>
            {
                aiModelConfig.SetActiveSlot(AiModelSlot.Agent);
                return Task.CompletedTask;
            },
            Keywords = "ai modelo agent trocar switch autonomo"
        });

        // ═══════════════════════════════════════════════════════════════
        // Create and return the ViewModel
        // ═══════════════════════════════════════════════════════════════

        return serviceProvider.GetRequiredService<CommandPaletteViewModel>();
    }

    private static string GetLastLines(string text, int count)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var taken = lines.Length > count ? lines[^count..] : lines;
        return string.Join('\n', taken);
    }
}
