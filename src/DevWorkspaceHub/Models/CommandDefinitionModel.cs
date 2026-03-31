using System;

namespace DevWorkspaceHub.Models;

/// <summary>Category for grouping commands in the palette UI.</summary>
public enum CommandCategory
{
    Terminal,
    Navigation,
    Workspace,
    Layout,
    Assistant
}

/// <summary>
/// A single executable command registered in the command palette.
/// Commands are registered by services/ViewModels at startup.
/// </summary>
public class CommandDefinitionModel
{
    /// <summary>Unique stable identifier, e.g. "terminal.new".</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display title shown in the palette list.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Secondary line — hints, shortcuts, descriptions.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Keyboard shortcut hint label (display only).</summary>
    public string? ShortcutHint { get; init; }

    public CommandCategory Category { get; init; } = CommandCategory.Navigation;

    /// <summary>Icon key from Icons.xaml.</summary>
    public string IconKey { get; init; } = "TerminalIcon";

    /// <summary>The action to execute. Runs on the UI thread.</summary>
    public Action Execute { get; init; } = () => { };

    /// <summary>Optional predicate — command hidden when false.</summary>
    public Func<bool>? IsVisible { get; init; }

    /// <summary>Search weight: higher = shown earlier for same query score.</summary>
    public int Priority { get; init; } = 0;
}

/// <summary>
/// WSL-style command definition with async actions, fuzzy search keywords,
/// and computed search text. Used by expanded command palette service.
/// </summary>
public sealed class CommandDefinition
{
    /// <summary>Unique identifier for the command (e.g. "terminal.new").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display title shown in the palette (e.g. "New Terminal").</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Category for grouping (e.g. "Terminal", "Navigation", "Project").</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Optional keyboard shortcut display string (e.g. "Ctrl+Shift+T").
    /// </summary>
    public string? Shortcut { get; init; }

    /// <summary>
    /// Optional icon glyph string (Segoe MDL2 Assets or any FontFamily glyph).
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Optional predicate that determines whether this command is currently enabled.
    /// When null, the command is always enabled.
    /// </summary>
    public Func<bool>? IsEnabled { get; init; }

    /// <summary>
    /// The async action to execute when the command is selected.
    /// </summary>
    public Func<Task>? Action { get; init; }

    /// <summary>
    /// Additional keywords for fuzzy matching (lowercase, space-separated).
    /// </summary>
    public string Keywords { get; init; } = string.Empty;

    /// <summary>
    /// Computed search text = "{Category} {Title} {Keywords}" in lowercase.
    /// Used internally by the fuzzy search engine.
    /// </summary>
    internal string SearchText { get; init; } = string.Empty;

    /// <summary>
    /// Computes the normalized search text from Title, Category, and Keywords.
    /// Called once at registration time.
    /// </summary>
    internal CommandDefinition WithComputedSearchText()
    {
        var parts = new[] { Category, Title, Keywords };
        var combined = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return new CommandDefinition
        {
            Id = Id,
            Title = Title,
            Category = Category,
            Shortcut = Shortcut,
            Icon = Icon,
            IsEnabled = IsEnabled,
            Action = Action,
            Keywords = Keywords,
            SearchText = combined.ToLowerInvariant()
        };
    }
}
