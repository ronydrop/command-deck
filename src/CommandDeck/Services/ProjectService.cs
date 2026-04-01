using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Manages development projects with JSON persistence in AppData.
/// Detection and scanning logic is delegated to <see cref="IProjectDetectionService"/>.
/// </summary>
public class ProjectService : IProjectService, IDisposable
{
    private readonly IProjectDetectionService _detection;
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

    public ProjectService(IProjectDetectionService detection)
    {
        _detection = detection;

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
                            .ThenBy(p => p.SortOrder)
                            .ThenBy(p => p.Name)
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

        var paths = await _detection.ScanForProjectPathsAsync(rootDirectory, maxDepth);
        foreach (var directory in paths)
        {
            var projectType = _detection.DetectProjectType(directory);
            detectedProjects.Add(new Project
            {
                Name = Path.GetFileName(directory),
                Path = directory,
                ProjectType = projectType,
                DefaultShell = ShellType.WSL,
                Color = _detection.GetProjectTypeColor(projectType),
                Icon = _detection.GetProjectTypeIcon(projectType)
            });
        }

        return detectedProjects;
    }

    /// <inheritdoc />
    public ProjectType DetectProjectType(string projectPath)
        => _detection.DetectProjectType(projectPath);

    public async Task ReorderProjectsAsync(List<string> orderedIds)
    {
        await _lock.WaitAsync();
        try
        {
            for (int i = 0; i < orderedIds.Count; i++)
            {
                var project = _projects.FirstOrDefault(p => p.Id == orderedIds[i]);
                if (project != null)
                    project.SortOrder = i + 1;
            }
            await SaveProjectsAsync();
            ProjectsChanged?.Invoke();
        }
        finally
        {
            _lock.Release();
        }
    }

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

        // Migrate: assign SortOrder based on file order if not yet set
        bool needsMigration = _projects.All(p => p.SortOrder == 0);
        if (needsMigration && _projects.Count > 0)
        {
            for (int i = 0; i < _projects.Count; i++)
                _projects[i].SortOrder = i + 1;
            await SaveProjectsAsync();
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
