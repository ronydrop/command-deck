using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Default implementation of <see cref="IProjectDetectionService"/>.
/// Contains the marker-file heuristics extracted from <see cref="ProjectService"/>.
/// </summary>
public class ProjectDetectionService : IProjectDetectionService
{
    /// <inheritdoc />
    public ProjectType DetectProjectType(string path)
    {
        if (!Directory.Exists(path))
            return ProjectType.Unknown;

        // Laravel: artisan + composer.json present
        if (File.Exists(Path.Combine(path, "artisan")) &&
            File.Exists(Path.Combine(path, "composer.json")))
            return ProjectType.Laravel;

        // Next.js config variants
        if (File.Exists(Path.Combine(path, "next.config.js")) ||
            File.Exists(Path.Combine(path, "next.config.mjs")) ||
            File.Exists(Path.Combine(path, "next.config.ts")))
            return ProjectType.NextJs;

        // TypeScript / React / Vue — check package.json contents
        if (File.Exists(Path.Combine(path, "tsconfig.json")))
        {
            if (File.Exists(Path.Combine(path, "package.json")))
            {
                try
                {
                    var packageJson = File.ReadAllText(Path.Combine(path, "package.json"));
                    if (packageJson.Contains("\"react\"")) return ProjectType.React;
                    if (packageJson.Contains("\"vue\""))   return ProjectType.Vue;
                }
                catch { /* best-effort: fall through to TypeScript */ }
            }
            return ProjectType.TypeScript;
        }

        if (File.Exists(Path.Combine(path, "package.json")))
            return ProjectType.NodeJs;

        if (Directory.GetFiles(path, "*.csproj").Length > 0 ||
            Directory.GetFiles(path, "*.sln").Length > 0)
            return ProjectType.DotNet;

        if (File.Exists(Path.Combine(path, "requirements.txt")) ||
            File.Exists(Path.Combine(path, "pyproject.toml")) ||
            File.Exists(Path.Combine(path, "setup.py")))
            return ProjectType.Python;

        if (File.Exists(Path.Combine(path, "docker-compose.yml")) ||
            File.Exists(Path.Combine(path, "docker-compose.yaml")) ||
            File.Exists(Path.Combine(path, "Dockerfile")))
            return ProjectType.Docker;

        return ProjectType.Unknown;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ScanForProjectPathsAsync(string rootPath, int maxDepth = 3)
    {
        var results = new List<string>();

        if (!Directory.Exists(rootPath))
            return Task.FromResult<IReadOnlyList<string>>(results);

        ScanDirectory(rootPath, results, 0, maxDepth);
        return Task.FromResult<IReadOnlyList<string>>(results);
    }

    /// <inheritdoc />
    public string GetProjectTypeColor(ProjectType projectType) => projectType switch
    {
        ProjectType.Laravel    => "#FF2D20",
        ProjectType.NodeJs     => "#339933",
        ProjectType.TypeScript => "#3178C6",
        ProjectType.React      => "#61DAFB",
        ProjectType.Vue        => "#4FC08D",
        ProjectType.NextJs     => "#FFFFFF",
        ProjectType.DotNet     => "#512BD4",
        ProjectType.Python     => "#3776AB",
        ProjectType.Docker     => "#2496ED",
        _                      => "#7C3AED"
    };

    /// <inheritdoc />
    public string GetProjectTypeIcon(ProjectType projectType) => projectType switch
    {
        ProjectType.Laravel    => "\uE768",
        ProjectType.NodeJs     => "\uE74E",
        ProjectType.TypeScript => "\uE7A8",
        ProjectType.React      => "\uE74E",
        ProjectType.Vue        => "\uE74E",
        ProjectType.NextJs     => "\uE74E",
        ProjectType.DotNet     => "\uE756",
        ProjectType.Python     => "\uE756",
        ProjectType.Docker     => "\uE7B8",
        _                      => "\uE74E"
    };

    // ─── Private ─────────────────────────────────────────────────────────────

    private static void ScanDirectory(string directory, List<string> results, int currentDepth, int maxDepth)
    {
        if (currentDepth > maxDepth) return;

        try
        {
            bool isProject =
                File.Exists(Path.Combine(directory, "package.json")) ||
                File.Exists(Path.Combine(directory, "composer.json")) ||
                File.Exists(Path.Combine(directory, ".git", "HEAD")) ||
                File.Exists(Path.Combine(directory, "Cargo.toml")) ||
                File.Exists(Path.Combine(directory, "go.mod")) ||
                Directory.GetFiles(directory, "*.csproj").Length > 0 ||
                Directory.GetFiles(directory, "*.sln").Length > 0 ||
                File.Exists(Path.Combine(directory, "pyproject.toml")) ||
                File.Exists(Path.Combine(directory, "requirements.txt"));

            if (isProject)
            {
                results.Add(directory);
                return; // Do not recurse into project subdirectories
            }

            foreach (var subDir in Directory.GetDirectories(directory))
            {
                var dirName = Path.GetFileName(subDir);
                // Skip common non-project directories
                if (dirName.StartsWith('.') ||
                    dirName == "node_modules" ||
                    dirName == "vendor" ||
                    dirName == "bin" ||
                    dirName == "obj" ||
                    dirName == "__pycache__" ||
                    dirName == ".git")
                    continue;

                ScanDirectory(subDir, results, currentDepth + 1, maxDepth);
            }
        }
        catch (UnauthorizedAccessException) { /* skip inaccessible dirs */ }
        catch (DirectoryNotFoundException)  { /* skip missing dirs */ }
    }
}
