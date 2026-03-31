using CommandDeck.Models;

namespace CommandDeck.Services;

public sealed class AiContinuationService : IAiContinuationService
{
    private readonly IAiSessionHistoryService _historyService;

    public AiContinuationService(IAiSessionHistoryService historyService)
    {
        _historyService = historyService;
    }

    public bool CanContinue(string sessionId, AiContinuationType type)
    {
        return type switch
        {
            AiContinuationType.RunAgain => _historyService.GetLast(sessionId) is not null,
            AiContinuationType.FixAgain => _historyService.GetLastByIntent(sessionId, AiPromptIntent.FixError) is not null,
            AiContinuationType.ExplainMore => _historyService.GetLastByIntent(sessionId, AiPromptIntent.ExplainOutput) is not null,
            _ => false
        };
    }

    public AiActionContinuation? BuildContinuation(string sessionId, AiContinuationType type)
    {
        return type switch
        {
            AiContinuationType.RunAgain =>
                _historyService.GetLast(sessionId) is { } last
                    ? AiActionContinuation.RunAgain(sessionId, last)
                    : null,

            AiContinuationType.FixAgain =>
                _historyService.GetLastByIntent(sessionId, AiPromptIntent.FixError) is { } fix
                    ? AiActionContinuation.FixAgain(sessionId, fix.CorrelationId)
                    : null,

            AiContinuationType.ExplainMore =>
                _historyService.GetLastByIntent(sessionId, AiPromptIntent.ExplainOutput) is { } explain
                    ? AiActionContinuation.ExplainMore(sessionId, explain.CorrelationId)
                    : null,

            _ => null
        };
    }

    public string? ResolvePaletteCommandId(AiActionContinuation continuation)
    {
        return continuation.OriginalIntent switch
        {
            AiPromptIntent.FixError => "ai.fix.error",
            AiPromptIntent.ExplainOutput => "ai.explain.output",
            AiPromptIntent.SuggestCommand => "ai.suggest.command",
            AiPromptIntent.SendContext => "ai.send.output",
            _ => null
        };
    }
}
