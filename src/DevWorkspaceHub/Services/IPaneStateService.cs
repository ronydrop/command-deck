using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Tracks the state of terminal panes (Idle/Running/Waiting/Done),
/// manages icon aggregation and scrolling titles — equivalent to
/// Zellij's claude-pane-notify plugin.
/// </summary>
public interface IPaneStateService
{
    /// <summary>
    /// Fired when any pane's state changes.
    /// </summary>
    event Action<PaneStateInfo>? StateChanged;

    /// <summary>
    /// Fired when the aggregated icon string changes (for status bar display).
    /// </summary>
    event Action<string>? AggregatedIconsChanged;

    /// <summary>
    /// Set the state of a pane. Optionally provide a custom title.
    /// </summary>
    void SetState(string paneId, PaneState state, string? title = null);

    /// <summary>
    /// Get current state info for a pane. Returns null if not tracked.
    /// </summary>
    PaneStateInfo? GetState(string paneId);

    /// <summary>
    /// Reset a pane to Idle, clearing its custom title.
    /// </summary>
    void ResetToIdle(string paneId);

    /// <summary>
    /// Remove a pane from tracking entirely (e.g. when terminal is closed).
    /// </summary>
    void RemovePane(string paneId);

    /// <summary>
    /// Get aggregated icon string for all non-Idle panes (for status bar).
    /// </summary>
    string GetAggregatedIcons();
}
