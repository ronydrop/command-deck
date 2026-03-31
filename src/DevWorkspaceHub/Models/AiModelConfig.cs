namespace DevWorkspaceHub.Models;

public sealed class AiModelConfig
{
    public AiModelSlot Slot { get; init; }
    public string ModelId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ShortLabel { get; init; } = string.Empty;
    public AiTaskAffinity TaskAffinity { get; init; } = AiTaskAffinity.General;
}

public enum AiTaskAffinity
{
    General,
    FastQuery,
    CodeGeneration,
    Refactoring,
    Agent,
    Orchestration
}

public sealed class AiRoutingResult
{
    public AiModelSlot RecommendedSlot { get; init; }
    public string ModelOrAlias { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
