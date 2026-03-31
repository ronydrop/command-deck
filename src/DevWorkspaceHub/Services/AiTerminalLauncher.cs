using System.Linq;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.ViewModels;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Creates a new terminal and injects the appropriate AI CLI command.
/// Extracted from CommandPaletteRegistrar to be reusable across toolbar and palette.
/// </summary>
public sealed class AiTerminalLauncher : IAiTerminalLauncher
{
    private readonly Lazy<MainViewModel> _mainViewModel;
    private readonly IAiTerminalService _aiTerminalService;
    private readonly ITerminalSessionService _terminalSessionService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IAiAgentStateService _aiAgentStateService;

    public AiTerminalLauncher(
        Lazy<MainViewModel> mainViewModel,
        IAiTerminalService aiTerminalService,
        ITerminalSessionService terminalSessionService,
        IWorkspaceService workspaceService,
        IAiAgentStateService aiAgentStateService)
    {
        _mainViewModel = mainViewModel;
        _aiTerminalService = aiTerminalService;
        _terminalSessionService = terminalSessionService;
        _workspaceService = workspaceService;
        _aiAgentStateService = aiAgentStateService;
    }

    public async Task LaunchAsync(AiSessionType sessionType, string? modelOrAlias = null)
    {
        var mainVm = _mainViewModel.Value;

        await mainVm.NewTerminalCommand.ExecuteAsync(null);

        var activeVm = mainVm.ActiveTerminal;
        if (activeVm?.Session is null)
            return;

        var sessionModel = _terminalSessionService.GetSession(activeVm.Session.Id);
        if (sessionModel is null)
            return;

        _aiTerminalService.TagSession(sessionModel, sessionType, modelOrAlias);

        activeVm.Title = sessionModel.Title;

        var canvasItem = _workspaceService.TerminalItems
            .FirstOrDefault(t => t.Terminal == activeVm);
        canvasItem?.UpdateAiMetadata(sessionModel.AiSessionType, sessionModel.AiModelUsed);

        // Register session for AI agent state detection
        _aiAgentStateService.RegisterSession(activeVm.Session.Id);

        await _aiTerminalService.InjectAiCommandAsync(activeVm.Session.Id, sessionType, modelOrAlias);
    }
}
