namespace CommandDeck.Models;

public sealed class AiActionContinuation
{
    public AiContinuationType Type { get; init; }
    public AiPromptIntent OriginalIntent { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string? OriginalCorrelationId { get; init; }
    public string? AdditionalContext { get; init; }

    public static AiActionContinuation FixAgain(string sessionId, string? correlationId) => new()
    {
        Type = AiContinuationType.FixAgain,
        OriginalIntent = AiPromptIntent.FixError,
        SessionId = sessionId,
        OriginalCorrelationId = correlationId
    };

    public static AiActionContinuation ExplainMore(string sessionId, string? correlationId) => new()
    {
        Type = AiContinuationType.ExplainMore,
        OriginalIntent = AiPromptIntent.ExplainOutput,
        SessionId = sessionId,
        OriginalCorrelationId = correlationId
    };

    public static AiActionContinuation RunAgain(string sessionId, AiSessionHistoryEntry original) => new()
    {
        Type = AiContinuationType.RunAgain,
        OriginalIntent = original.Intent,
        SessionId = sessionId,
        OriginalCorrelationId = original.CorrelationId
    };
}

public enum AiContinuationType
{
    RunAgain,
    FixAgain,
    ExplainMore
}
