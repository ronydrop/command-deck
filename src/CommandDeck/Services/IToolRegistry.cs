using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Registry of tools that can be invoked by the AI assistant during a conversation.
/// Provides schema definitions (for sending to the provider) and execution handlers.
/// </summary>
public interface IToolRegistry
{
    /// <summary>All registered tool definitions, in registration order.</summary>
    IReadOnlyList<ToolDefinition> All { get; }

    /// <summary>Returns the definition for the given tool name, or null if not registered.</summary>
    ToolDefinition? Get(string name);

    /// <summary>
    /// Registers a tool along with its async execution handler.
    /// The handler receives the raw JSON input and returns a result string.
    /// </summary>
    void Register(ToolDefinition tool, Func<JsonElement, CancellationToken, Task<string>> handler);

    /// <summary>
    /// Executes the named tool with the given JSON input.
    /// Throws <see cref="KeyNotFoundException"/> if the tool is not registered.
    /// </summary>
    Task<string> ExecuteAsync(string toolName, JsonElement input, CancellationToken ct);
}
