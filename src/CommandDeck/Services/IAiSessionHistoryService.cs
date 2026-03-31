using CommandDeck.Models;

namespace CommandDeck.Services;

public interface IAiSessionHistoryService
{
    void Record(AiSessionHistoryEntry entry);

    void UpdateStatus(string sessionId, string? correlationId, AiExecutionStatus status, string? response = null);

    AiSessionHistoryEntry? GetLast(string sessionId);

    AiSessionHistoryEntry? GetLastByIntent(string sessionId, AiPromptIntent intent);

    AiSessionHistoryEntry? GetByCorrelationId(string sessionId, string correlationId);

    IReadOnlyList<AiSessionHistoryEntry> GetHistory(string sessionId, int limit = 20);

    IReadOnlyList<AiSessionHistoryEntry> GetRetryable(string sessionId);

    bool HasHistory(string sessionId);

    void Clear(string sessionId);
}
