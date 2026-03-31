using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Detects and tracks the semantic state of AI agent terminals
/// by analysing their stripped output in real time.
/// </summary>
public interface IAiAgentStateService
{
    /// <summary>Fires when a registered AI session transitions to a new state.</summary>
    event Action<AiAgentStateChangedArgs>? StateChanged;

    /// <summary>Starts monitoring terminal output for the given session.</summary>
    void RegisterSession(string sessionId);

    /// <summary>Stops monitoring and cleans up resources for the given session.</summary>
    void UnregisterSession(string sessionId);

    /// <summary>Returns the current detected state for a registered session.</summary>
    AiAgentState GetState(string sessionId);
}
