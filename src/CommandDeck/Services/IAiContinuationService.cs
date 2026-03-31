using CommandDeck.Models;

namespace CommandDeck.Services;

public interface IAiContinuationService
{
    bool CanContinue(string sessionId, AiContinuationType type);

    AiActionContinuation? BuildContinuation(string sessionId, AiContinuationType type);

    string? ResolvePaletteCommandId(AiActionContinuation continuation);
}
