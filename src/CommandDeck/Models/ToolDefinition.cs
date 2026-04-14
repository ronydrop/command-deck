using System.Text.Json;

namespace CommandDeck.Models;

/// <summary>
/// Describes a tool (function) that can be invoked by the AI assistant.
/// Maps to Anthropic's tool definition format and OpenAI's function definition format.
/// </summary>
public sealed class ToolDefinition
{
    /// <summary>Unique tool name (snake_case, e.g. "kanban_create_card").</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable description sent to the model so it knows when to call this tool.</summary>
    public required string Description { get; init; }

    /// <summary>JSON Schema (draft-07) object describing the tool's input parameters.</summary>
    public required JsonElement InputSchema { get; init; }
}
