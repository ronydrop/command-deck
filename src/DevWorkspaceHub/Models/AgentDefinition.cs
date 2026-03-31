namespace DevWorkspaceHub.Models;

public sealed class AgentDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public AiSessionType SessionType { get; init; } = AiSessionType.Cc;
    public string? ModelOrAlias { get; init; }
    public bool RequiresApiKey { get; init; }
}

public sealed class AgentGroup
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
}
