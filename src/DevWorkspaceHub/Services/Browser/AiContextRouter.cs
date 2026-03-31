using DevWorkspaceHub.Models;
using DevWorkspaceHub.Models.Browser;

namespace DevWorkspaceHub.Services.Browser;

public class AiContextRouter : IAiContextRouter
{
    private readonly ITerminalService _terminalService;
    private readonly IAiTerminalService _aiTerminalService;
    private readonly IAiAgentStateService _agentStateService;

    public event Action<string>? ContextSentToAssistant;
    public event Action<string, string>? ContextSentToTerminal;

    public AiContextRouter(
        ITerminalService terminalService,
        IAiTerminalService aiTerminalService,
        IAiAgentStateService agentStateService)
    {
        _terminalService = terminalService;
        _aiTerminalService = aiTerminalService;
        _agentStateService = agentStateService;
    }

    public List<AgentTargetInfo> GetAvailableTargets()
    {
        var targets = new List<AgentTargetInfo>
        {
            new()
            {
                Id = "assistant",
                DisplayName = "🤖 Assistant AI",
                Type = AgentTargetType.Assistant,
                IsAvailable = true
            }
        };

        var aiSessions = _aiTerminalService.GetActiveAiSessions();
        foreach (var session in aiSessions)
        {
            var state = _agentStateService.GetState(session.Id);
            var stateIcon = state switch
            {
                AiAgentState.Idle => "💤",
                AiAgentState.WaitingUser or AiAgentState.WaitingInput => "⏳",
                AiAgentState.Thinking => "🤔",
                AiAgentState.Executing => "⚡",
                _ => "📟"
            };

            targets.Add(new AgentTargetInfo
            {
                Id = session.Id,
                DisplayName = $"{stateIcon} {session.Title ?? "Terminal"} ({session.AiSessionType})",
                Type = AgentTargetType.Terminal,
                SessionId = session.Id,
                IsAvailable = state is AiAgentState.Idle or AiAgentState.WaitingUser or AiAgentState.WaitingInput
            });
        }

        return targets;
    }

    public async Task SendToAssistantAsync(string formattedContext)
    {
        ContextSentToAssistant?.Invoke(formattedContext);
    }

    public async Task SendToTerminalAsync(string sessionId, string formattedContext)
    {
        await _terminalService.WriteAsync(sessionId, formattedContext);
        ContextSentToTerminal?.Invoke(sessionId, formattedContext);
    }
}
