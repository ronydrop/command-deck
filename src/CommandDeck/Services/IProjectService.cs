using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Service for managing development projects (CRUD + persistence).
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Gets all saved projects.
    /// </summary>
    Task<List<Project>> GetAllProjectsAsync();

    /// <summary>
    /// Gets a project by its ID.
    /// </summary>
    Task<Project?> GetProjectAsync(string id);

    /// <summary>
    /// Adds a new project.
    /// </summary>
    Task AddProjectAsync(Project project);

    /// <summary>
    /// Updates an existing project.
    /// </summary>
    Task UpdateProjectAsync(Project project);

    /// <summary>
    /// Deletes a project by ID.
    /// </summary>
    Task DeleteProjectAsync(string id);

    /// <summary>
    /// Scans a directory for development projects.
    /// Returns detected projects (by package.json, composer.json, .git, etc.).
    /// </summary>
    Task<List<Project>> ScanForProjectsAsync(string rootDirectory, int maxDepth = 3);

    /// <summary>
    /// Detects the project type based on files present in the directory.
    /// </summary>
    ProjectType DetectProjectType(string projectPath);

    /// <summary>
    /// Reorders projects by saving new SortOrder values based on the provided ID sequence.
    /// </summary>
    Task ReorderProjectsAsync(List<string> orderedIds);

    /// <summary>
    /// Event raised when the project list changes.
    /// </summary>
    event Action? ProjectsChanged;
}
