using System.Collections.Generic;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Provides project-type detection and directory scanning logic, decoupled from
/// the persistence concerns in <see cref="IProjectService"/>.
/// </summary>
public interface IProjectDetectionService
{
    /// <summary>
    /// Inspects the files present in <paramref name="path"/> and returns the most
    /// likely <see cref="ProjectType"/>. Returns <see cref="ProjectType.Unknown"/> when
    /// no recognised marker files are found or the path does not exist.
    /// </summary>
    ProjectType DetectProjectType(string path);

    /// <summary>
    /// Recursively walks <paramref name="rootPath"/> up to <paramref name="maxDepth"/>
    /// levels and returns the paths of directories that look like project roots
    /// (contain package.json, .git/HEAD, composer.json, *.csproj, etc.).
    /// </summary>
    Task<IReadOnlyList<string>> ScanForProjectPathsAsync(string rootPath, int maxDepth = 3);

    /// <summary>Returns the accent colour associated with <paramref name="projectType"/>.</summary>
    string GetProjectTypeColor(ProjectType projectType);

    /// <summary>Returns the icon character (Segoe MDL2 / FontAwesome code-point) for <paramref name="projectType"/>.</summary>
    string GetProjectTypeIcon(ProjectType projectType);
}
