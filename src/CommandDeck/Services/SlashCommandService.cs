using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CommandDeck.Services;

/// <summary>
/// Thread-safe registry and dispatcher for chat slash commands.
/// </summary>
public sealed class SlashCommandService : ISlashCommandService
{
    private readonly List<SlashCommandDescriptor> _ordered = new();
    private readonly Dictionary<string, SlashCommandDescriptor> _byName  = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public IReadOnlyList<SlashCommandDescriptor> Commands
    {
        get { lock (_lock) return _ordered.AsReadOnly(); }
    }

    public void Register(SlashCommandDescriptor command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_lock)
        {
            _byName[command.Name] = command;
            foreach (var alias in command.Aliases)
                _byName[alias] = command;

            if (!_ordered.Any(c => c.Name.Equals(command.Name, StringComparison.OrdinalIgnoreCase)))
                _ordered.Add(command);
        }
    }

    public async Task<SlashCommandResult> TryExecuteAsync(string input, SlashCommandContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith('/'))
            return new SlashCommandResult { Handled = false };

        // Parse "/name rest_of_args"
        var withoutSlash = input[1..].TrimStart();
        var spaceIdx = withoutSlash.IndexOf(' ');
        var name = spaceIdx < 0 ? withoutSlash : withoutSlash[..spaceIdx];
        var args = spaceIdx < 0 ? string.Empty : withoutSlash[(spaceIdx + 1)..].Trim();

        SlashCommandDescriptor? descriptor;
        lock (_lock) _byName.TryGetValue(name, out descriptor);

        if (descriptor is null)
            return new SlashCommandResult { Handled = false };

        var ctx = context with { Args = args };
        return await descriptor.Handler(ctx, ct).ConfigureAwait(false);
    }
}
