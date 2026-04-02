using CommandDeck.Models;

namespace CommandDeck.Services;

public interface IAiTerminalService
{
    Task<bool> IsCliAvailableAsync(string cliName = "claude");

    Task<AiCliInfo> DetectCliAsync();

    string GetLaunchCommand(AiSessionType sessionType, string? modelOrAlias = null);

    Task InjectAiCommandAsync(string sessionId, AiSessionType sessionType, string? modelOrAlias = null);

    void TagSession(TerminalSessionModel session, AiSessionType sessionType, string? modelOrAlias = null);

    IReadOnlyList<TerminalSessionModel> GetActiveAiSessions();

    void InvalidateCliCache();
}
