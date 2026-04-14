using System;
using System.Collections.Generic;

namespace CommandDeck.Models;

// ─── Context Value ───────────────────────────────────────────────────────────

/// <summary>
/// A key/value context entry shared between canvas tiles via <see cref="Services.ITileContextService"/>.
/// </summary>
public class TileContextEntry
{
    /// <summary>Semantic key for this context (e.g. "terminal.output", "git.branch", "browser.url").</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The context value. Cast to the expected type in the consumer.</summary>
    public object? Value { get; init; }

    /// <summary>ID of the tile that produced this context entry.</summary>
    public string? SourceTileId { get; init; }

    /// <summary>Human-readable source label (e.g. "Terminal 1", "Browser").</summary>
    public string? SourceLabel { get; init; }

    /// <summary>UTC timestamp when this entry was last set.</summary>
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

// ─── Well-known context keys ─────────────────────────────────────────────────

/// <summary>
/// Pre-defined context keys used across tiles for consistent cross-tile communication.
/// </summary>
public static class TileContextKeys
{
    // Terminal
    public const string TerminalLastOutput   = "terminal.last_output";
    public const string TerminalLastCommand  = "terminal.last_command";
    public const string TerminalActiveId     = "terminal.active_id";
    public const string TerminalWorkingDir   = "terminal.working_dir";

    // Git
    public const string GitBranch            = "git.branch";
    public const string GitStatus            = "git.status";
    public const string GitDiff              = "git.diff";

    // Browser
    public const string BrowserUrl           = "browser.url";
    public const string BrowserTitle         = "browser.title";
    public const string BrowserSelectedHtml  = "browser.selected_html";

    // Project
    public const string ProjectId            = "project.id";
    public const string ProjectName          = "project.name";
    public const string ProjectPath          = "project.path";

    // AI
    public const string AiLastResponse      = "ai.last_response";
    public const string AiActiveMode        = "ai.active_mode";
    public const string AiProvider          = "ai.provider";
}

/// <summary>
/// Arguments for a tile context change notification.
/// </summary>
public class TileContextChangedArgs
{
    public string Key { get; init; } = string.Empty;
    public TileContextEntry? Entry { get; init; }
    public TileContextEntry? Previous { get; init; }
}
