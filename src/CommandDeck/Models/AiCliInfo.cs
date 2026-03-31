namespace CommandDeck.Models;

public record AiCliInfo(
    bool CcAvailable,
    bool ClaudeAvailable,
    string? CcVersion,
    string? ClaudeVersion,
    bool OpenRouterConfigured);
