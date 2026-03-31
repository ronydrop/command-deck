using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Registry for all command palette actions.
/// ViewModels and services register commands at startup;
/// CommandPaletteViewModel queries and executes them.
/// </summary>
public interface ICommandPaletteService
{
    // ─── Original WIN members ─────────────────────────────────────────────

    void Register(CommandDefinitionModel command);
    void Unregister(string commandId);

    /// <summary>Returns visible commands matching the query, sorted by relevance.</summary>
    IReadOnlyList<CommandDefinitionModel> Search(string query);

    /// <summary>Returns all visible commands (empty query).</summary>
    IReadOnlyList<CommandDefinitionModel> GetAll();

    event Action? CommandsChanged;

    // ─── Expanded WSL members ─────────────────────────────────────────────

    /// <summary>
    /// All currently registered commands (read-only).
    /// </summary>
    ReadOnlyObservableCollection<CommandDefinition> Commands { get; }

    /// <summary>
    /// Registers a command definition. Replaces any existing command with the same Id.
    /// </summary>
    void RegisterCommand(CommandDefinition command);

    /// <summary>
    /// Unregisters a command by its Id. No-op if not found.
    /// </summary>
    void UnregisterCommand(string commandId);

    /// <summary>
    /// Searches registered commands using fuzzy substring matching.
    /// </summary>
    IReadOnlyList<CommandDefinition> SearchCommands(string query);

    /// <summary>
    /// Executes the given command's Action and records it in history.
    /// </summary>
    Task ExecuteCommandAsync(CommandDefinition command);

    /// <summary>
    /// Event raised when a command is executed.
    /// </summary>
    event Action<CommandDefinition>? CommandExecuted;
}
