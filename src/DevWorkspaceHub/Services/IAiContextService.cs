using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

public interface IAiContextService
{
    Task<AiTerminalContext?> GetActiveTerminalContextAsync();

    Task<AiTerminalContext?> GetTerminalContextAsync(string sessionId);

    Task<string> BuildPromptAsync(AiPromptIntent intent, AiTerminalContext? context = null, int outputLines = 40);

    string GetRecentOutput(string sessionId, int lines = 40);
}
