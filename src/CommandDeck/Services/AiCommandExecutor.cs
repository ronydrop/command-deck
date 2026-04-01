using CommandDeck.Models;
using CommandDeck.ViewModels;

namespace CommandDeck.Services;

/// <summary>
/// Implements AI command orchestration: fix-last-error and send-output-to-AI pipelines.
/// Extracted from lambda closures in <see cref="CommandDeck.Helpers.CommandPaletteRegistrar"/>.
/// </summary>
public sealed class AiCommandExecutor : IAiCommandExecutor
{
    /// <summary>
    /// Milliseconds to wait after launching an AI terminal session before writing the prompt.
    /// Gives the process time to reach a ready state.
    /// </summary>
    private const int TerminalSettleDurationMs = 1500;

    private readonly IAiContextService _aiContextService;
    private readonly IAiTerminalService _aiTerminalService;
    private readonly IAiTerminalLauncher _aiTerminalLauncher;
    private readonly IAiSessionHistoryService _aiHistory;
    private readonly ITerminalSessionService _terminalSessionService;
    private readonly Lazy<MainViewModel> _mainViewModel;

    public AiCommandExecutor(
        IAiContextService aiContextService,
        IAiTerminalService aiTerminalService,
        IAiTerminalLauncher aiTerminalLauncher,
        IAiSessionHistoryService aiHistory,
        ITerminalSessionService terminalSessionService,
        Lazy<MainViewModel> mainViewModel)
    {
        _aiContextService = aiContextService;
        _aiTerminalService = aiTerminalService;
        _aiTerminalLauncher = aiTerminalLauncher;
        _aiHistory = aiHistory;
        _terminalSessionService = terminalSessionService;
        _mainViewModel = mainViewModel;
    }

    /// <inheritdoc/>
    public async Task FixLastErrorAsync(string sessionId, CancellationToken ct = default)
    {
        var prompt = await _aiContextService.BuildPromptAsync(AiPromptIntent.FixError);
        if (string.IsNullOrWhiteSpace(prompt)) return;

        var correlationId = Guid.NewGuid().ToString("N")[..12];
        _aiHistory.Record(new AiSessionHistoryEntry
        {
            SessionId = sessionId,
            Intent = AiPromptIntent.FixError,
            ModelUsed = "default",
            PromptSent = prompt,
            Source = AiActionSource.CommandPalette,
            CorrelationId = correlationId,
            ExecutionStatus = AiExecutionStatus.Running
        });

        try
        {
            var cli = await _aiTerminalService.DetectCliAsync();
            if (cli.CcAvailable)
            {
                await _aiTerminalLauncher.LaunchAsync(AiSessionType.CcRun);
                await Task.Delay(TerminalSettleDurationMs, ct);
                var activeSession = _mainViewModel.Value.ActiveTerminal?.Session;
                if (activeSession is not null)
                    await _terminalSessionService.WriteAsync(activeSession.Id, prompt + "\n");
            }
            else
            {
                var vm = _mainViewModel.Value;
                vm.IsAIPanelOpen = true;
                vm.AssistantPanel.InputText = prompt;
                vm.AssistantPanel.SendMessageCommand.Execute(null);
            }
            _aiHistory.UpdateStatus(sessionId, correlationId, AiExecutionStatus.Completed);
        }
        catch
        {
            _aiHistory.UpdateStatus(sessionId, correlationId, AiExecutionStatus.Failed);
        }
    }

    /// <inheritdoc/>
    public async Task SendOutputToAiAsync(string sessionId, CancellationToken ct = default)
    {
        var prompt = await _aiContextService.BuildPromptAsync(AiPromptIntent.SendContext, outputLines: 50);
        if (string.IsNullOrWhiteSpace(prompt)) return;

        var correlationId = Guid.NewGuid().ToString("N")[..12];
        _aiHistory.Record(new AiSessionHistoryEntry
        {
            SessionId = sessionId,
            Intent = AiPromptIntent.SendContext,
            ModelUsed = "default",
            PromptSent = prompt,
            Source = AiActionSource.CommandPalette,
            CorrelationId = correlationId,
            ExecutionStatus = AiExecutionStatus.Running
        });

        try
        {
            await _aiTerminalLauncher.LaunchAsync(AiSessionType.Cc);
            await Task.Delay(TerminalSettleDurationMs, ct);
            var activeSession = _mainViewModel.Value.ActiveTerminal?.Session;
            if (activeSession is not null)
                await _terminalSessionService.WriteAsync(activeSession.Id, prompt + "\n");
            _aiHistory.UpdateStatus(sessionId, correlationId, AiExecutionStatus.Completed);
        }
        catch
        {
            _aiHistory.UpdateStatus(sessionId, correlationId, AiExecutionStatus.Failed);
        }
    }
}
