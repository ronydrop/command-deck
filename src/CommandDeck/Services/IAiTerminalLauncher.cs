using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Launches a new terminal pre-configured with an AI CLI command (claude, cc, etc.).
/// </summary>
public interface IAiTerminalLauncher
{
    Task LaunchAsync(AiSessionType sessionType, string? modelOrAlias = null);
}
