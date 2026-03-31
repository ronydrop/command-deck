namespace DevWorkspaceHub.Models;

public sealed class AiSessionHistoryEntry
{
    public string SessionId { get; init; } = string.Empty;
    public AiPromptIntent Intent { get; init; }
    public string ModelUsed { get; init; } = string.Empty;
    public string PromptSent { get; init; } = string.Empty;
    public string? Response { get; set; }
    public string? ResponseSummary { get; set; }
    public AiExecutionStatus ExecutionStatus { get; set; } = AiExecutionStatus.Pending;
    public AiActionSource Source { get; init; } = AiActionSource.Unknown;
    public string? CorrelationId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool Success { get; set; } = true;

    public bool CanRetry => ExecutionStatus is AiExecutionStatus.Failed or AiExecutionStatus.Cancelled;
    public bool IsTerminal => ExecutionStatus is AiExecutionStatus.Completed or AiExecutionStatus.Failed;
}

public enum AiExecutionStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum AiActionSource
{
    Unknown,
    CardContextMenu,
    CommandPalette,
    Shortcut,
    RunAgain,
    FixAgain,
    RetryAgain
}
