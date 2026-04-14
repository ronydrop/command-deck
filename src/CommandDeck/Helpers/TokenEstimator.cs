namespace CommandDeck.Helpers;

/// <summary>
/// Provides simple heuristic token estimation and cost calculation for Claude models.
/// Uses ~3.8 characters per token as a reasonable approximation.
/// </summary>
public static class TokenEstimator
{
    private const double CharsPerToken = 3.8;

    // Pricing per 1M tokens (USD) as of early 2025
    private static readonly Dictionary<string, (decimal InputPer1M, decimal OutputPer1M)> Pricing = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-haiku-4-5"]          = (0.80m,   4.00m),
        ["claude-haiku-4-5-20251001"] = (0.80m,   4.00m),
        ["claude-sonnet-4-6"]         = (3.00m,  15.00m),
        ["claude-sonnet-4-5"]         = (3.00m,  15.00m),
        ["claude-opus-4-6"]           = (15.00m, 75.00m),
        ["claude-opus-4-5"]           = (15.00m, 75.00m),
        // Legacy names for display matching
        ["haiku"]                     = (0.80m,   4.00m),
        ["sonnet"]                    = (3.00m,  15.00m),
        ["opus"]                      = (15.00m, 75.00m),
    };

    // Approximate BRL/USD exchange rate (static approximation)
    private const decimal BrlRate = 5.10m;

    /// <summary>
    /// Estimates the number of tokens in the given text using a character-count heuristic.
    /// </summary>
    public static int EstimateTokenCount(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling(text.Length / CharsPerToken);
    }

    /// <summary>
    /// Estimates the cost in USD for the given number of tokens, model and direction.
    /// </summary>
    /// <param name="tokens">Number of tokens.</param>
    /// <param name="model">Model identifier (matched case-insensitively).</param>
    /// <param name="isInput">True for input/prompt tokens, false for output/completion tokens.</param>
    public static decimal EstimateCost(long tokens, string? model, bool isInput)
    {
        if (tokens <= 0 || model is null) return 0m;

        // Try exact match first, then partial substring match
        (decimal inputRate, decimal outputRate) rates = default;
        bool found = false;

        foreach (var key in Pricing.Keys)
        {
            if (model.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                rates = (Pricing[key].InputPer1M, Pricing[key].OutputPer1M);
                found = true;
                break;
            }
        }

        if (!found) rates = (3.00m, 15.00m); // default: Sonnet pricing

        var ratePerToken = isInput
            ? rates.inputRate / 1_000_000m
            : rates.outputRate / 1_000_000m;

        return Math.Round(tokens * ratePerToken, 6);
    }

    /// <summary>Converts USD amount to BRL using a static exchange rate.</summary>
    public static decimal UsdToBrl(decimal usd) => Math.Round(usd * BrlRate, 4);

    /// <summary>
    /// Formats a token count for display (e.g. 1234 → "1,234" or 1200000 → "1.2M").
    /// </summary>
    public static string FormatTokens(long tokens) => tokens switch
    {
        >= 1_000_000 => $"{tokens / 1_000_000.0:0.#}M",
        >= 1_000     => $"{tokens / 1_000.0:0.#}K",
        _            => tokens.ToString("N0")
    };

    /// <summary>Formats a USD cost for display.</summary>
    public static string FormatUsd(decimal usd) => usd < 0.01m
        ? $"< $0.01"
        : $"${usd:F2}";

    /// <summary>Formats a BRL cost for display.</summary>
    public static string FormatBrl(decimal brl) => brl < 0.05m
        ? $"< R$0.05"
        : $"R${brl:F2}";
}
