namespace CommandDeck.Services;

/// <summary>
/// Tracks Claude/Anthropic token usage and estimated cost for the current session.
/// </summary>
public interface IClaudeUsageService
{
    /// <summary>Total input tokens consumed this session.</summary>
    long SessionInputTokens { get; }

    /// <summary>Total output tokens generated this session.</summary>
    long SessionOutputTokens { get; }

    /// <summary>Total tokens (input + output) this session.</summary>
    long SessionTotalTokens { get; }

    /// <summary>Estimated session cost in US dollars.</summary>
    decimal SessionCostUsd { get; }

    /// <summary>Estimated session cost in Brazilian reais.</summary>
    decimal SessionCostBrl { get; }

    /// <summary>Model used in the last tracked response.</summary>
    string? CurrentModel { get; }

    /// <summary>Fired on the UI dispatcher whenever usage counters change.</summary>
    event Action? UsageUpdated;

    /// <summary>Accumulates token counts from a completed API response.</summary>
    void TrackUsage(int inputTokens, int outputTokens, string? model = null);

    /// <summary>Resets all counters for this session.</summary>
    void Reset();
}
