using System.Collections.Concurrent;
using CommandDeck.Models;

namespace CommandDeck.Services;

public sealed class AiSessionHistoryService : IAiSessionHistoryService
{
    private const int MaxEntriesPerSession = 50;

    private readonly ConcurrentDictionary<string, List<AiSessionHistoryEntry>> _history = new();

    public void Record(AiSessionHistoryEntry entry)
    {
        var list = _history.GetOrAdd(entry.SessionId, _ => new List<AiSessionHistoryEntry>());

        lock (list)
        {
            list.Add(entry);
            if (list.Count > MaxEntriesPerSession)
                list.RemoveAt(0);
        }
    }

    public void UpdateStatus(string sessionId, string? correlationId, AiExecutionStatus status, string? response = null)
    {
        if (!_history.TryGetValue(sessionId, out var list))
            return;

        lock (list)
        {
            AiSessionHistoryEntry? target = null;

            if (correlationId is not null)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].CorrelationId == correlationId)
                    {
                        target = list[i];
                        break;
                    }
                }
            }

            target ??= list.Count > 0 ? list[^1] : null;
            if (target is null) return;

            target.ExecutionStatus = status;
            target.Success = status == AiExecutionStatus.Completed;
            if (response is not null)
                target.Response = response;
        }
    }

    public AiSessionHistoryEntry? GetLast(string sessionId)
    {
        if (!_history.TryGetValue(sessionId, out var list))
            return null;

        lock (list)
        {
            return list.Count > 0 ? list[^1] : null;
        }
    }

    public AiSessionHistoryEntry? GetLastByIntent(string sessionId, AiPromptIntent intent)
    {
        if (!_history.TryGetValue(sessionId, out var list))
            return null;

        lock (list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Intent == intent)
                    return list[i];
            }
        }

        return null;
    }

    public AiSessionHistoryEntry? GetByCorrelationId(string sessionId, string correlationId)
    {
        if (!_history.TryGetValue(sessionId, out var list))
            return null;

        lock (list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].CorrelationId == correlationId)
                    return list[i];
            }
        }

        return null;
    }

    public IReadOnlyList<AiSessionHistoryEntry> GetHistory(string sessionId, int limit = 20)
    {
        if (!_history.TryGetValue(sessionId, out var list))
            return Array.Empty<AiSessionHistoryEntry>();

        lock (list)
        {
            var count = Math.Min(limit, list.Count);
            return list.GetRange(list.Count - count, count).AsReadOnly();
        }
    }

    public IReadOnlyList<AiSessionHistoryEntry> GetRetryable(string sessionId)
    {
        if (!_history.TryGetValue(sessionId, out var list))
            return Array.Empty<AiSessionHistoryEntry>();

        lock (list)
        {
            return list.Where(e => e.CanRetry).ToList().AsReadOnly();
        }
    }

    public bool HasHistory(string sessionId)
    {
        return _history.TryGetValue(sessionId, out var list) && list.Count > 0;
    }

    public void Clear(string sessionId)
    {
        _history.TryRemove(sessionId, out _);
    }
}
