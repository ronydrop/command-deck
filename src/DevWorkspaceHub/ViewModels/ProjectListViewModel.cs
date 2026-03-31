using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.Services;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// ViewModel for the project list sidebar.
/// </summary>
public partial class ProjectListViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private ObservableCollection<Project> _projects = new();

    [ObservableProperty]
    private Project? _selectedProject;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<Project> _filteredProjects = new();

    /// <summary>
    /// Raised when a project is selected to open.
    /// </summary>
    public event Action<Project>? ProjectSelected;

    /// <summary>
    /// Raised when the user wants to edit a project.
    /// </summary>
    public event Action<Project>? EditProjectRequested;

    /// <summary>
    /// Raised when the user wants to add a new project.
    /// </summary>
    public event Action? AddProjectRequested;

    public ProjectListViewModel(IProjectService projectService, ISettingsService settingsService)
    {
        _projectService = projectService;
        _settingsService = settingsService;

        _projectService.ProjectsChanged += async () =>
        {
            try { await LoadProjectsAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ProjectsChanged] {ex}"); }
        };
    }

    /// <summary>
    /// Loads all projects from the service.
    /// </summary>
    [RelayCommand]
    public async Task LoadProjectsAsync()
    {
        IsLoading = true;
        try
        {
            var projects = await _projectService.GetAllProjectsAsync();
            // Repopulate in-place to preserve bindings and avoid full visual rebuild
            Projects.Clear();
            foreach (var p in projects) Projects.Add(p);
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Opens the selected project (creates terminal, shows dashboard).
    /// </summary>
    [RelayCommand]
    private void OpenProject(Project? project)
    {
        if (project == null) return;
        SelectedProject = project;
        project.LastOpened = DateTime.Now;
        _ = _projectService.UpdateProjectAsync(project);
        ProjectSelected?.Invoke(project);
    }

    /// <summary>
    /// Adds a new project manually.
    /// </summary>
    [RelayCommand]
    private void AddProject()
    {
        AddProjectRequested?.Invoke();
    }

    /// <summary>
    /// Edits an existing project.
    /// </summary>
    [RelayCommand]
    private void EditProject(Project? project)
    {
        if (project == null) return;
        EditProjectRequested?.Invoke(project);
    }

    /// <summary>
    /// Deletes a project.
    /// </summary>
    [RelayCommand]
    private async Task DeleteProject(Project? project)
    {
        if (project == null) return;
        await _projectService.DeleteProjectAsync(project.Id);
    }

    /// <summary>
    /// Toggles favorite status.
    /// </summary>
    [RelayCommand]
    private async Task ToggleFavorite(Project? project)
    {
        if (project == null) return;
        project.IsFavorite = !project.IsFavorite;
        await _projectService.UpdateProjectAsync(project);
    }

    /// <summary>
    /// Scans the configured directory for projects.
    /// </summary>
    [RelayCommand]
    private async Task ScanForProjects()
    {
        IsScanning = true;
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            var detected = await _projectService.ScanForProjectsAsync(
                settings.ProjectScanDirectory,
                settings.ProjectScanMaxDepth);

            foreach (var project in detected)
            {
                await _projectService.AddProjectAsync(project);
            }
        }
        finally
        {
            IsScanning = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var source = string.IsNullOrWhiteSpace(SearchText)
            ? Projects
            : (IEnumerable<Project>)Projects.Where(p =>
                p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                p.Path.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        // Repopulate in-place to preserve existing bindings
        FilteredProjects.Clear();
        foreach (var p in source) FilteredProjects.Add(p);
    }
}
