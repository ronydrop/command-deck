using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// ViewModel for the project list sidebar.
/// </summary>
public partial class ProjectListViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly ITerminalService _terminalService;

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

    [ObservableProperty]
    private ObservableCollection<Project> _activeProjects = new();

    [ObservableProperty]
    private ObservableCollection<Project> _inactiveProjects = new();

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

    public ProjectListViewModel(IProjectService projectService, ISettingsService settingsService, INotificationService notificationService, ITerminalService terminalService)
    {
        _projectService = projectService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _terminalService = terminalService;

        _projectService.ProjectsChanged += async () =>
        {
            try { await LoadProjectsAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ProjectsChanged] {ex}"); }
        };

        _terminalService.SessionCreated += _ => RefreshActiveState();
        _terminalService.SessionExited += _ => RefreshActiveState();
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
            var selectedId = SelectedProject?.Id;
            var projects = await _projectService.GetAllProjectsAsync();
            // Repopulate in-place to preserve bindings and avoid full visual rebuild
            Projects.Clear();
            foreach (var p in projects) Projects.Add(p);
            ApplyFilter();
            // Restore selection using ID match so the visual selected state survives a list rebuild
            if (selectedId is not null)
                SelectedProject = FilteredProjects.FirstOrDefault(p => p.Id == selectedId);
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
        var name = project.Name;
        await _projectService.DeleteProjectAsync(project.Id);
        _notificationService.Notify(
            "Projeto removido",
            NotificationType.Info,
            NotificationSource.System,
            message: name);
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

            _notificationService.Notify(
                detected.Count > 0 ? $"{detected.Count} projeto(s) encontrado(s)" : "Nenhum projeto novo encontrado",
                detected.Count > 0 ? NotificationType.Success : NotificationType.Info,
                NotificationSource.System);
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task MoveProjectUp(Project? project)
    {
        if (project == null) return;
        var idx = FilteredProjects.IndexOf(project);
        if (idx <= 0) return;
        FilteredProjects.Move(idx, idx - 1);
        var orderedIds = FilteredProjects.Select(p => p.Id).ToList();
        await _projectService.ReorderProjectsAsync(orderedIds);
    }

    [RelayCommand]
    private async Task MoveProjectDown(Project? project)
    {
        if (project == null) return;
        var idx = FilteredProjects.IndexOf(project);
        if (idx < 0 || idx >= FilteredProjects.Count - 1) return;
        FilteredProjects.Move(idx, idx + 1);
        var orderedIds = FilteredProjects.Select(p => p.Id).ToList();
        await _projectService.ReorderProjectsAsync(orderedIds);
    }

    public async Task MoveProjectAsync(Project dragged, Project target)
    {
        var draggedIdx = FilteredProjects.IndexOf(dragged);
        var targetIdx = FilteredProjects.IndexOf(target);
        if (draggedIdx < 0 || targetIdx < 0 || draggedIdx == targetIdx) return;
        FilteredProjects.Move(draggedIdx, targetIdx);
        var orderedIds = FilteredProjects.Select(p => p.Id).ToList();
        await _projectService.ReorderProjectsAsync(orderedIds);
    }

    /// <summary>
    /// Recalculates which projects are active (have terminal sessions) vs inactive.
    /// Safe to call from any thread — marshals to UI thread if needed.
    /// </summary>
    public void RefreshActiveState()
    {
        if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(SplitByActiveState);
            return;
        }
        SplitByActiveState();
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

        SplitByActiveState();
    }

    private void SplitByActiveState()
    {
        var activeProjectIds = _terminalService.GetSessions()
            .Where(s => s.Status is TerminalStatus.Running or TerminalStatus.Starting)
            .Select(s => s.ProjectId)
            .Where(id => id != null)
            .ToHashSet();

        ActiveProjects.Clear();
        InactiveProjects.Clear();
        foreach (var p in FilteredProjects)
        {
            if (activeProjectIds.Contains(p.Id))
                ActiveProjects.Add(p);
            else
                InactiveProjects.Add(p);
        }
    }
}
