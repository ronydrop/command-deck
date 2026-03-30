using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

public interface IAiContinuationService
{
    bool CanContinue(string sessionId, AiContinuationType type);

    AiActionContinuation? BuildContinuation(string sessionId, AiContinuationType type, AiModelSlot? overrideSlot = null);

    string? ResolvePaletteCommandId(AiActionContinuation continuation);
}
