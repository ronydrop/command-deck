using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Thread-safe in-process registry of AI tools.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly List<ToolDefinition> _ordered = new();
    private readonly Dictionary<string, (ToolDefinition Def, Func<JsonElement, CancellationToken, Task<string>> Handler)> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public IReadOnlyList<ToolDefinition> All
    {
        get
        {
            lock (_lock) return _ordered.AsReadOnly();
        }
    }

    public ToolDefinition? Get(string name)
    {
        lock (_lock)
            return _map.TryGetValue(name, out var entry) ? entry.Def : null;
    }

    public void Register(ToolDefinition tool, Func<JsonElement, CancellationToken, Task<string>> handler)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock)
        {
            _map[tool.Name] = (tool, handler);
            if (!_ordered.Any(d => d.Name.Equals(tool.Name, StringComparison.OrdinalIgnoreCase)))
                _ordered.Add(tool);
        }
    }

    public Task<string> ExecuteAsync(string toolName, JsonElement input, CancellationToken ct)
    {
        Func<JsonElement, CancellationToken, Task<string>> handler;
        lock (_lock)
        {
            if (!_map.TryGetValue(toolName, out var entry))
                throw new KeyNotFoundException($"Tool '{toolName}' não está registrada.");
            handler = entry.Handler;
        }
        return handler(input, ct);
    }
}
