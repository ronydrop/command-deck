using System;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Orchestrates the full project-switch use case: saves the current workspace,
/// tears down active terminals, updates services, then restores the target
/// project's saved session.
/// </summary>
public interface IProjectSwitchService
{
    /// <summary>
    /// Switches the active project, saving the current session and restoring
    /// the target session.
    /// </summary>
    /// <param name="project">The project to switch to.</param>
    /// <param name="context">
    /// Contextual data (active terminals, factory) needed for the switch.
    /// </param>
    /// <param name="progress">
    /// Optional progress reporter for status messages. Reports plain-text
    /// strings that the caller can assign to status-bar/overlay labels.
    /// </param>
    Task SwitchToAsync(
        Project project,
        ProjectSwitchContext context,
        IProgress<string>? progress = null);

    /// <summary>
    /// Raised after a switch completes (success or failure).
    /// Subscribers should update observable ViewModel properties from the
    /// <see cref="ProjectSwitchResult"/> payload.
    /// </summary>
    event EventHandler<ProjectSwitchResult>? SwitchCompleted;
}
