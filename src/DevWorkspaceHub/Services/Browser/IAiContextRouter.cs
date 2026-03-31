using DevWorkspaceHub.Models.Browser;

namespace DevWorkspaceHub.Services.Browser;

public class AgentTargetInfo
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public AgentTargetType Type { get; set; }
    public string? SessionId { get; set; }
    public bool IsAvailable { get; set; }
}

public interface IAiContextRouter
{
    List<AgentTargetInfo> GetAvailableTargets();
    Task SendToAssistantAsync(string formattedContext);
    Task SendToTerminalAsync(string sessionId, string formattedContext);
}
