using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

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
            AiContinuationType.RetryWithModel => _historyService.GetLast(sessionId) is { CanRetry: true },
            _ => false
        };
    }

    public AiActionContinuation? BuildContinuation(string sessionId, AiContinuationType type, AiModelSlot? overrideSlot = null)
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

            AiContinuationType.RetryWithModel when overrideSlot.HasValue =>
                _historyService.GetLast(sessionId) is { } entry
                    ? AiActionContinuation.RetryWithModel(sessionId, entry.CorrelationId, overrideSlot.Value)
                    : null,

            _ => null
        };
    }

    public string? ResolvePaletteCommandId(AiActionContinuation continuation)
    {
        if (continuation.Type == AiContinuationType.RetryWithModel && continuation.OverrideModelSlot.HasValue)
        {
            return continuation.OverrideModelSlot.Value switch
            {
                AiModelSlot.Sonnet => "ai.launch.sonnet",
                AiModelSlot.Opus => "ai.launch.opus",
                AiModelSlot.Haiku => "ai.launch.haiku",
                AiModelSlot.Agent => "ai.launch.agent",
                _ => null
            };
        }

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
