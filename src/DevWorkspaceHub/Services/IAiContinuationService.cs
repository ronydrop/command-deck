using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

public interface IAiContinuationService
{
    bool CanContinue(string sessionId, AiContinuationType type);

    AiActionContinuation? BuildContinuation(string sessionId, AiContinuationType type);

    string? ResolvePaletteCommandId(AiActionContinuation continuation);
}
