using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommandDeck.Models;

namespace CommandDeck.Services;

public class CommandPaletteService : ICommandPaletteService
{
    // ─── Original WIN state ───────────────────────────────────────────────
    private readonly Dictionary<string, CommandDefinitionModel> _commands = new();

    // ─── Expanded WSL state ───────────────────────────────────────────────
    private readonly ObservableCollection<CommandDefinition> _wslCommands = new();
    private readonly ReadOnlyObservableCollection<CommandDefinition> _wslCommandsReadOnly;
    private readonly Dictionary<string, CommandDefinition> _wslCommandById = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxHistorySize = 10;
    private readonly LinkedList<string> _recentCommandIds = new();
    private readonly HashSet<string> _recentCommandIdSet = new(StringComparer.OrdinalIgnoreCase);

    // ─── Events ───────────────────────────────────────────────────────────
    public event Action? CommandsChanged;
    public event Action<CommandDefinition>? CommandExecuted;
    public ReadOnlyObservableCollection<CommandDefinition> Commands => _wslCommandsReadOnly;

    public CommandPaletteService()
    {
        _wslCommandsReadOnly = new(_wslCommands);
    }

    // ─── Original WIN members ─────────────────────────────────────────────

    public void Register(CommandDefinitionModel command)
    {
        _commands[command.Id] = command;
        CommandsChanged?.Invoke();
    }

    public void Unregister(string commandId)
    {
        if (_commands.Remove(commandId))
            CommandsChanged?.Invoke();
    }

    public IReadOnlyList<CommandDefinitionModel> GetAll()
        => _commands.Values
            .Where(IsCommandVisible)
            .OrderByDescending(c => c.Priority)
            .ThenBy(c => c.Category)
            .ThenBy(c => c.Title)
            .ToList();

    public IReadOnlyList<CommandDefinitionModel> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetAll();

        var q = query.ToLower();
        return _commands.Values
            .Where(IsCommandVisible)
            .Select(c => (cmd: c, score: Score(c, q)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenByDescending(x => x.cmd.Priority)
            .Select(x => x.cmd)
            .ToList();
    }

    // ─── Expanded WSL members ─────────────────────────────────────────────

    public void RegisterCommand(CommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalized = command.WithComputedSearchText();

        if (_wslCommandById.TryGetValue(normalized.Id, out var existing))
        {
            _wslCommands.Remove(existing);
            _wslCommandById.Remove(normalized.Id);
        }

        _wslCommands.Add(normalized);
        _wslCommandById[normalized.Id] = normalized;
        CommandsChanged?.Invoke();
    }

    public void UnregisterCommand(string commandId)
    {
        ArgumentNullException.ThrowIfNull(commandId);

        if (_wslCommandById.TryGetValue(commandId, out var existing))
        {
            _wslCommands.Remove(existing);
            _wslCommandById.Remove(commandId);
            CommandsChanged?.Invoke();
        }
    }

    public IReadOnlyList<CommandDefinition> SearchCommands(string query)
    {
        var enabledCommands = _wslCommands
            .Where(c => c.IsEnabled?.Invoke() ?? true)
            .ToList();

        if (string.IsNullOrWhiteSpace(query))
        {
            return enabledCommands
                .OrderByDescending(c => _recentCommandIdSet.Contains(c.Id))
                .ThenBy(c => GetHistoryIndex(c.Id))
                .ThenBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var normalizedQuery = query.Trim().ToLowerInvariant();
        var queryTerms = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var scored = enabledCommands
            .Select(cmd =>
            {
                var score = ComputeFuzzyScore(cmd.SearchText, normalizedQuery, queryTerms);
                return (Command: cmd, Score: score);
            })
            .Where(pair => pair.Score > 0)
            .ToList();

        return scored
            .OrderByDescending(pair => pair.Score)
            .ThenByDescending(pair => _recentCommandIdSet.Contains(pair.Command.Id))
            .ThenBy(pair => GetHistoryIndex(pair.Command.Id))
            .Select(pair => pair.Command)
            .ToList();
    }

    public async Task ExecuteCommandAsync(CommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);
        AddToHistory(command.Id);

        if (command.Action is not null)
        {
            try
            {
                await command.Action();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CommandPalette] Error executing command '{command.Id}': {ex}");
            }
        }

        CommandExecuted?.Invoke(command);
    }

    // ─── Private helpers (WIN) ────────────────────────────────────────────

    private static bool IsCommandVisible(CommandDefinitionModel c)
        => c.IsVisible?.Invoke() ?? true;

    private static int Score(CommandDefinitionModel c, string q)
    {
        var title = c.Title.ToLower();
        var sub = (c.Subtitle ?? string.Empty).ToLower();

        if (title.StartsWith(q)) return 100;
        if (title.Contains(q)) return 70;
        if (FuzzyMatch(title, q)) return 40;
        if (sub.Contains(q)) return 20;

        return 0;
    }

    private static bool FuzzyMatch(string source, string pattern)
    {
        int si = 0;
        foreach (char c in pattern)
        {
            si = source.IndexOf(c, si);
            if (si < 0) return false;
            si++;
        }
        return true;
    }

    // ─── Private helpers (WSL) ────────────────────────────────────────────

    private static int ComputeFuzzyScore(string searchText, string normalizedQuery, string[] queryTerms)
    {
        if (string.IsNullOrEmpty(searchText) || queryTerms.Length == 0)
            return 0;

        int totalScore = 0;

        foreach (var term in queryTerms)
        {
            int bestTermScore = 0;

            var words = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (word.StartsWith(term, StringComparison.OrdinalIgnoreCase))
                    bestTermScore = Math.Max(bestTermScore, 100);
                else if (word.Contains(term, StringComparison.OrdinalIgnoreCase))
                    bestTermScore = Math.Max(bestTermScore, 50);
            }

            if (searchText.Contains(term, StringComparison.OrdinalIgnoreCase))
                bestTermScore = Math.Max(bestTermScore, 40);

            if (bestTermScore == 0)
                return 0;

            totalScore += bestTermScore;
        }

        if (normalizedQuery.Length > 0 && searchText.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            totalScore += 30;

        return totalScore;
    }

    private void AddToHistory(string commandId)
    {
        if (_recentCommandIdSet.Contains(commandId))
            _recentCommandIds.Remove(commandId);
        else
            _recentCommandIdSet.Add(commandId);

        _recentCommandIds.AddFirst(commandId);

        while (_recentCommandIds.Count > MaxHistorySize)
        {
            var removed = _recentCommandIds.Last!.Value;
            _recentCommandIds.RemoveLast();
            _recentCommandIdSet.Remove(removed);
        }
    }

    private int GetHistoryIndex(string commandId)
    {
        int i = 0;
        foreach (var id in _recentCommandIds)
        {
            if (string.Equals(id, commandId, StringComparison.OrdinalIgnoreCase))
                return MaxHistorySize - i;
            i++;
        }
        return 0;
    }
}
