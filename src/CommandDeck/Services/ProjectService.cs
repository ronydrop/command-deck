using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Manages development projects with JSON persistence in AppData.
/// </summary>
public class ProjectService : IProjectService, IDisposable
{
    private readonly string _projectsFilePath;
    private List<Project> _projects = new();
    private bool _isLoaded;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public event Action? ProjectsChanged;

    /// <summary>
    /// Fired when a save operation fails (e.g., disk full). Provides the exception for logging/UI feedback.
    /// </summary>
    public event Action<Exception>? SaveFailed;

    /// <summary>
    /// Fired when a load operation fails (e.g., corrupted JSON). Projects will be empty.
    /// </summary>
    public event Action<Exception>? LoadFailed;

    public ProjectService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CommandDeck");

        Directory.CreateDirectory(appDataPath);
        _projectsFilePath = Path.Combine(appDataPath, "projects.json");
    }

    public async Task<List<Project>> GetAllProjectsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!_isLoaded)
                await LoadProjectsAsync();
            return _projects.OrderByDescending(p => p.IsFavorite)
                            .ThenByDescending(p => p.LastOpened)
                            .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Project?> GetProjectAsync(string id)
    {
        var projects = await GetAllProjectsAsync();
        return projects.FirstOrDefault(p => p.Id == id);
    }

    public async Task AddProjectAsync(Project project)
    {
        await _lock.WaitAsync();
        try
        {
            if (!_isLoaded)
                await LoadProjectsAsync();

            // Prevent duplicates
            if (_projects.Any(p => p.Path.Equals(project.Path, StringComparison.OrdinalIgnoreCase)))
                return;

            _projects.Add(project);
            await SaveProjectsAsync();
            ProjectsChanged?.Invoke();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateProjectAsync(Project project)
    {
        await _lock.WaitAsync();
        try
        {
            var index = _projects.FindIndex(p => p.Id == project.Id);
            if (index >= 0)
            {
                _projects[index] = project;
                await SaveProjectsAsync();
                ProjectsChanged?.Invoke();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteProjectAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            _projects.RemoveAll(p => p.Id == id);
            await SaveProjectsAsync();
            ProjectsChanged?.Invoke();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<Project>> ScanForProjectsAsync(string rootDirectory, int maxDepth = 3)
    {
        var detectedProjects = new List<Project>();

        if (!Directory.Exists(rootDirectory))
            return detectedProjects;

        await Task.Run(() => ScanDirectory(rootDirectory, detectedProjects, 0, maxDepth));
        return detectedProjects;
    }

    public ProjectType DetectProjectType(string projectPath)
    {
        if (!Directory.Exists(projectPath))
            return ProjectType.Unknown;

        // Check for specific project indicators
        if (File.Exists(Path.Combine(projectPath, "artisan")) &&
            File.Exists(Path.Combine(projectPath, "composer.json")))
            return ProjectType.Laravel;

        if (File.Exists(Path.Combine(projectPath, "next.config.js")) ||
            File.Exists(Path.Combine(projectPath, "next.config.mjs")) ||
            File.Exists(Path.Combine(projectPath, "next.config.ts")))
            return ProjectType.NextJs;

        if (File.Exists(Path.Combine(projectPath, "tsconfig.json")))
        {
            if (File.Exists(Path.Combine(projectPath, "package.json")))
            {
                try
                {
                    var packageJson = File.ReadAllText(Path.Combine(projectPath, "package.json"));
                    if (packageJson.Contains("\"react\"")) return ProjectType.React;
                    if (packageJson.Contains("\"vue\"")) return ProjectType.Vue;
                }
                catch { }
            }
            return ProjectType.TypeScript;
        }

        if (File.Exists(Path.Combine(projectPath, "package.json")))
            return ProjectType.NodeJs;

        if (Directory.GetFiles(projectPath, "*.csproj").Length > 0 ||
            Directory.GetFiles(projectPath, "*.sln").Length > 0)
            return ProjectType.DotNet;

        if (File.Exists(Path.Combine(projectPath, "requirements.txt")) ||
            File.Exists(Path.Combine(projectPath, "pyproject.toml")) ||
            File.Exists(Path.Combine(projectPath, "setup.py")))
            return ProjectType.Python;

        if (File.Exists(Path.Combine(projectPath, "docker-compose.yml")) ||
            File.Exists(Path.Combine(projectPath, "docker-compose.yaml")) ||
            File.Exists(Path.Combine(projectPath, "Dockerfile")))
            return ProjectType.Docker;

        return ProjectType.Unknown;
    }

    // ─── Private Methods ────────────────────────────────────────────────────

    private void ScanDirectory(string directory, List<Project> results, int currentDepth, int maxDepth)
    {
        if (currentDepth > maxDepth) return;

        try
        {
            // Check if this directory is a project root
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
                var projectType = DetectProjectType(directory);
                var project = new Project
                {
                    Name = Path.GetFileName(directory),
                    Path = directory,
                    ProjectType = projectType,
                    DefaultShell = ShellType.WSL,
                    Color = GetProjectTypeColor(projectType),
                    Icon = GetProjectTypeIcon(projectType)
                };
                results.Add(project);
                return; // Don't recurse into project subdirectories
            }

            // Recurse into subdirectories
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
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }
        catch (DirectoryNotFoundException)
        {
            // Skip missing directories
        }
    }

    private static string GetProjectTypeColor(ProjectType type) => type switch
    {
        ProjectType.Laravel => "#FF2D20",
        ProjectType.NodeJs => "#339933",
        ProjectType.TypeScript => "#3178C6",
        ProjectType.React => "#61DAFB",
        ProjectType.Vue => "#4FC08D",
        ProjectType.NextJs => "#FFFFFF",
        ProjectType.DotNet => "#512BD4",
        ProjectType.Python => "#3776AB",
        ProjectType.Docker => "#2496ED",
        _ => "#7C3AED"
    };

    private static string GetProjectTypeIcon(ProjectType type) => type switch
    {
        ProjectType.Laravel => "\uE768",
        ProjectType.NodeJs => "\uE74E",
        ProjectType.TypeScript => "\uE7A8",
        ProjectType.React => "\uE74E",
        ProjectType.Vue => "\uE74E",
        ProjectType.NextJs => "\uE74E",
        ProjectType.DotNet => "\uE756",
        ProjectType.Python => "\uE756",
        ProjectType.Docker => "\uE7B8",
        _ => "\uE74E"
    };

    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task LoadProjectsAsync()
    {
        try
        {
            if (File.Exists(_projectsFilePath))
            {
                var json = await File.ReadAllTextAsync(_projectsFilePath);
                _projects = JsonSerializer.Deserialize<List<Project>>(json, JsonOptions) ?? new();
            }
        }
        catch (Exception ex)
        {
            _projects = new();
            System.Diagnostics.Debug.WriteLine($"[ProjectService.Load] {ex}");
            LoadFailed?.Invoke(ex);
        }
        finally
        {
            _isLoaded = true;
        }
    }

    private async Task SaveProjectsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_projects, JsonOptions);
            await File.WriteAllTextAsync(_projectsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectService.Save] {ex}");
            SaveFailed?.Invoke(ex);
        }
    }
}
