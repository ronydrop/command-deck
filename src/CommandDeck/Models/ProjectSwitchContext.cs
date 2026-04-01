using System;
using System.Collections.Generic;
using CommandDeck.ViewModels;

namespace CommandDeck.Models;

/// <summary>
/// Carries the contextual state the caller (ViewModel) passes to
/// <see cref="Services.IProjectSwitchService"/> so the service never needs
/// to reference the ViewModel directly.
/// </summary>
public class ProjectSwitchContext
{
    /// <summary>The id of the project that is currently active (before the switch).</summary>
    public string? CurrentProjectId { get; init; }

    /// <summary>All terminal view-models currently open on the canvas.</summary>
    public IReadOnlyList<TerminalViewModel> ActiveTerminals { get; init; } = [];

    /// <summary>Factory that creates a fresh <see cref="TerminalViewModel"/> from the DI container.</summary>
    public Func<TerminalViewModel> TerminalVmFactory { get; init; } = null!;
}
