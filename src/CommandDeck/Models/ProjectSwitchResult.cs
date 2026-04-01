using System.Collections.Generic;
using CommandDeck.ViewModels;

namespace CommandDeck.Models;

/// <summary>
/// Carries the outcome of a project switch operation emitted via
/// <see cref="Services.IProjectSwitchService.SwitchCompleted"/>.
/// The ViewModel subscribes to this event to update observable properties
/// without the service needing to reference the ViewModel.
/// </summary>
public class ProjectSwitchResult
{
    /// <summary>The project that was switched to.</summary>
    public Project SwitchedTo { get; init; } = null!;

    /// <summary>The terminal view-models that were restored from the saved layout.</summary>
    public IReadOnlyList<TerminalViewModel> RestoredTerminals { get; init; } = [];

    /// <summary>The terminal that should become active after the switch.</summary>
    public TerminalViewModel? ActiveTerminal { get; init; }

    /// <summary>Branch display string taken from the loaded git info (may be empty).</summary>
    public string? GitBranchDisplay { get; init; }

    /// <summary>True when the switch completed without error.</summary>
    public bool Success { get; init; }

    /// <summary>Error message when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; init; }
}
