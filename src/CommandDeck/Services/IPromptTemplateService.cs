using System.Collections.Generic;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Registry of prompt templates and agent modes.
/// Provides built-in presets and supports user-created custom entries.
/// </summary>
public interface IPromptTemplateService
{
    // ─── Prompt Templates ─────────────────────────────────────────────────────

    IReadOnlyList<PromptTemplate> Templates { get; }
    IReadOnlyList<PromptTemplate> GetByCategory(string category);
    PromptTemplate? GetTemplate(string id);
    void AddTemplate(PromptTemplate template);
    void UpdateTemplate(PromptTemplate template);
    void DeleteTemplate(string id);

    // ─── Agent Modes ──────────────────────────────────────────────────────────

    IReadOnlyList<AgentMode> Modes { get; }
    AgentMode? GetMode(string id);
    void AddMode(AgentMode mode);
    void UpdateMode(AgentMode mode);
    void DeleteMode(string id);

    // ─── Persistence ─────────────────────────────────────────────────────────

    System.Threading.Tasks.Task SaveAsync();
    System.Threading.Tasks.Task LoadAsync();

    event System.Action? DataChanged;
}
