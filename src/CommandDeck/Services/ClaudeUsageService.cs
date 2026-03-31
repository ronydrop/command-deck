namespace CommandDeck.Services;

/// <summary>
/// Session-scoped tracker for Claude/Anthropic API token usage and estimated cost.
/// Thread-safe via Interlocked operations on the counters.
/// </summary>
public sealed class ClaudeUsageService : IClaudeUsageService
{
    // Atomic counters — only Interlocked.Add/Read should touch these.
    private long _inputTokens;
    private long _outputTokens;
    private string? _currentModel;

    // Pricing as of mid-2025 (USD per 1 million tokens)
    private static readonly (decimal input, decimal output) HaikuRates  = (0.80m, 4.00m);
    private static readonly (decimal input, decimal output) SonnetRates = (3.00m, 15.00m);
    private static readonly (decimal input, decimal output) OpusRates   = (15.00m, 75.00m);

    private const decimal BrlPerUsd = 5.70m;

    public long SessionInputTokens  => Interlocked.Read(ref _inputTokens);
    public long SessionOutputTokens => Interlocked.Read(ref _outputTokens);
    public long SessionTotalTokens  => SessionInputTokens + SessionOutputTokens;
    public string? CurrentModel     => _currentModel;

    public decimal SessionCostUsd =>
        CalculateCost(SessionInputTokens, SessionOutputTokens, _currentModel);

    public decimal SessionCostBrl => SessionCostUsd * BrlPerUsd;

    public event Action? UsageUpdated;

    /// <inheritdoc/>
    public void TrackUsage(int inputTokens, int outputTokens, string? model = null)
    {
        Interlocked.Add(ref _inputTokens, inputTokens);
        Interlocked.Add(ref _outputTokens, outputTokens);

        if (!string.IsNullOrWhiteSpace(model))
            _currentModel = model;

        UsageUpdated?.Invoke();
    }

    /// <inheritdoc/>
    public void Reset()
    {
        Interlocked.Exchange(ref _inputTokens, 0);
        Interlocked.Exchange(ref _outputTokens, 0);
        UsageUpdated?.Invoke();
    }

    private static decimal CalculateCost(long input, long output, string? model)
    {
        var (inputRate, outputRate) = GetRates(model);
        return (input / 1_000_000m) * inputRate
             + (output / 1_000_000m) * outputRate;
    }

    private static (decimal input, decimal output) GetRates(string? model)
    {
        var m = model?.ToLowerInvariant() ?? string.Empty;
        if (m.Contains("opus"))   return OpusRates;
        if (m.Contains("sonnet")) return SonnetRates;
        return HaikuRates; // default / haiku / unknown
    }
}
