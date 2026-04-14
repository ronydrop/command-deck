using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Context object passed to slash command handlers.
/// Carries state and callbacks that allow commands to mutate chat without
/// depending on the ViewModel type (avoids Services → ViewModels coupling).
/// </summary>
public sealed record SlashCommandContext
{
    /// <summary>Available model names for the current provider.</summary>
    public required IReadOnlyList<string> AvailableModels { get; init; }

    /// <summary>All registered agent modes.</summary>
    public required IReadOnlyList<AgentMode> AgentModes { get; init; }

    /// <summary>Arguments after the command name (may be empty).</summary>
    public string Args { get; init; } = string.Empty;

    /// <summary>Changes the selected model (e.g. from /model command).</summary>
    public required Action<string> SetModel { get; init; }

    /// <summary>Changes the active agent mode.</summary>
    public required Action<AgentMode?> SetAgent { get; init; }

    /// <summary>Clears the chat history.</summary>
    public required Action ClearHistory { get; init; }

    /// <summary>Tool registry reference (used by /tools command).</summary>
    public required IToolRegistry ToolRegistry { get; init; }

    /// <summary>Tool executor (used by /kanban shortcuts).</summary>
    public required IToolExecutionService ToolExec { get; init; }

    /// <summary>Switches the provider (e.g. from /provider command).</summary>
    public required Action<string> SwitchProvider { get; init; }
}

/// <summary>
/// Result returned by a slash command handler.
/// </summary>
public sealed record SlashCommandResult
{
    /// <summary>Whether a command was matched and handled.</summary>
    public bool Handled { get; init; }

    /// <summary>Optional text to inject into the chat as a system message.</summary>
    public string? ResponseText { get; init; }
}

/// <summary>
/// Descriptor of a single slash command.
/// </summary>
public sealed record SlashCommandDescriptor
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string[] Aliases { get; init; } = Array.Empty<string>();
    public required Func<SlashCommandContext, CancellationToken, Task<SlashCommandResult>> Handler { get; init; }
}

/// <summary>
/// Registry and dispatcher for slash commands entered in the chat input
/// (e.g. <c>/help</c>, <c>/clear</c>, <c>/model claude-opus-4-6</c>).
/// </summary>
public interface ISlashCommandService
{
    /// <summary>All registered commands, in registration order.</summary>
    IReadOnlyList<SlashCommandDescriptor> Commands { get; }

    /// <summary>Registers a new command. Replaces an existing command with the same name.</summary>
    void Register(SlashCommandDescriptor command);

    /// <summary>
    /// Attempts to parse and execute a slash command from raw chat input.
    /// Returns <see cref="SlashCommandResult.Handled"/> = false when input doesn't start
    /// with "/" or no matching command is registered.
    /// </summary>
    Task<SlashCommandResult> TryExecuteAsync(string input, SlashCommandContext context, CancellationToken ct);
}
