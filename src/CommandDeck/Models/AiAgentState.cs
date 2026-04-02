namespace CommandDeck.Models;

/// <summary>
/// Semantic states of an AI agent running inside a terminal session.
/// Detected by analysing the stripped terminal output.
/// </summary>
public enum AiAgentState
{
    /// <summary>No activity — shell prompt visible.</summary>
    Idle,
    /// <summary>Agent is processing / thinking.</summary>
    Thinking,
    /// <summary>Agent is executing a tool or command.</summary>
    Executing,
    /// <summary>Agent is waiting for user confirmation (Y/n).</summary>
    WaitingUser,
    /// <summary>Agent asked a question and awaits a response.</summary>
    WaitingInput,
    /// <summary>Task completed successfully.</summary>
    Completed,
    /// <summary>An error was detected in the output.</summary>
    Error
}

/// <summary>
/// Parsed multiple-choice line for <see cref="AiAgentState.WaitingInput"/> (display + text sent to terminal).
/// </summary>
public sealed record AiAgentChoiceOption(string Label, string SendText);

/// <summary>
/// Event args emitted by <see cref="Services.IAiAgentStateService"/> when a session's state changes.
/// </summary>
public sealed class AiAgentStateChangedArgs
{
    public required string SessionId { get; init; }
    public required AiAgentState State { get; init; }
    public required string Icon { get; init; }
    public required string Label { get; init; }
    public string? PrimarySnippet { get; init; }
    public string? SecondarySnippet { get; init; }
    public string? SessionTitle { get; init; }
    public bool SupportsMarkdown { get; init; }
    public bool CanJumpToExactContext { get; init; }

    /// <summary>When <see cref="State"/> is <see cref="AiAgentState.WaitingInput"/>, clickable options for the island.</summary>
    public IReadOnlyList<AiAgentChoiceOption> ChoiceOptions { get; init; } = Array.Empty<AiAgentChoiceOption>();

    /// <summary>
    /// Secondary line for the island: first argument to the last tool call, error line snippet, etc.
    /// </summary>
    public string? ActionDetail { get; init; }

    /// <summary>Returns the canonical icon for a given state.</summary>
    public static string GetIcon(AiAgentState state) => state switch
    {
        AiAgentState.Thinking     => "\uD83E\uDDE0", // 🧠
        AiAgentState.Executing    => "\u2699\uFE0F",  // ⚙️
        AiAgentState.WaitingUser  => "\u23F3",        // ⏳
        AiAgentState.WaitingInput => "\uD83D\uDCAC",  // 💬
        AiAgentState.Completed    => "\u2705",        // ✅
        AiAgentState.Error        => "\u274C",        // ❌
        _                         => string.Empty
    };

    /// <summary>Returns a short label for a given state.</summary>
    public static string GetLabel(AiAgentState state) => state switch
    {
        AiAgentState.Thinking     => "Thinking",
        AiAgentState.Executing    => "Executing",
        AiAgentState.WaitingUser  => "Waiting",
        AiAgentState.WaitingInput => "Question",
        AiAgentState.Completed    => "Done",
        AiAgentState.Error        => "Error",
        _                         => string.Empty
    };
}
