namespace CommandDeck.Models;

public record AiCliInfo(
    bool ClaudeAvailable,
    string? ClaudeVersion,
    bool CodexAvailable,
    bool AiderAvailable,
    bool GeminiAvailable,
    bool CopilotAvailable);
