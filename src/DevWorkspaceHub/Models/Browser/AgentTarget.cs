namespace DevWorkspaceHub.Models.Browser;

public enum AgentTargetType
{
    Assistant,
    Terminal
}

public class AgentTarget
{
    public AgentTargetType Type { get; set; }
    public string? TerminalSessionId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? AgentType { get; set; }
}
