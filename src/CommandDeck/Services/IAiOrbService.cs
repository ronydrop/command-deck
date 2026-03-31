using System.Windows;
using System.Threading.Tasks;

namespace CommandDeck.Services;

/// <summary>
/// Describes metadata about an AI provider for the Orb widget.
/// </summary>
public record OrbProviderInfo(string Name, string DisplayName, string GlowColor);

/// <summary>
/// Orchestrates all AI Orb actions: context capture, prompt improvement,
/// command execution, provider switching, and position persistence.
/// </summary>
public interface IAiOrbService
{
    /// <summary>Returns the active provider info (name + glow color).</summary>
    OrbProviderInfo GetActiveProviderInfo();

    /// <summary>Improves the last command in the active terminal.</summary>
    Task<string> ImproveLastCommandAsync();

    /// <summary>Captures terminal context and copies it to the clipboard.</summary>
    Task CopyContextToClipboardAsync();

    /// <summary>Executes a command string in the active terminal session.</summary>
    Task ExecuteCommandAsync(string command);

    /// <summary>Switches the active AI provider by name.</summary>
    Task SwitchProviderAsync(string providerName);

    /// <summary>Sends a message to the active AI and returns the response.</summary>
    Task<string> SendMessageToAiAsync(string message);

    /// <summary>Returns the default orb position synchronously (before async load).</summary>
    Point GetSavedPosition();

    /// <summary>Loads saved orb position asynchronously from persisted settings.</summary>
    Task<Point> LoadSavedPositionAsync();

    /// <summary>Persists the orb position to settings.</summary>
    void SavePosition(Point position);
}
