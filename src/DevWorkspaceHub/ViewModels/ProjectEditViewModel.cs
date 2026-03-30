using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.Services;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// ViewModel for the add/edit project dialog.
/// </summary>
public partial class ProjectEditViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private bool _isEditing;
    private string? _originalId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private ShellType _defaultShell = ShellType.WSL;

    [ObservableProperty]
    private string _color = "#7C3AED";

    [ObservableProperty]
    private string _icon = "\uE74E";

    [ObservableProperty]
    private ObservableCollection<string> _startupCommands = new();

    [ObservableProperty]
    private string _newStartupCommand = string.Empty;

    [ObservableProperty]
    private string _dialogTitle = "Add Project";

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Available shell types for the dropdown.
    /// </summary>
    public IReadOnlyList<ShellType> AvailableShells { get; } =
        Enum.GetValues<ShellType>().ToList();

    /// <summary>
    /// Available accent colors for projects.
    /// </summary>
    public IReadOnlyList<string> AvailableColors { get; } = new[]
    {
        "#7C3AED", // Purple
        "#2563EB", // Blue
        "#059669", // Green
        "#D97706", // Amber
        "#DC2626", // Red
        "#DB2777", // Pink
        "#0891B2", // Cyan
        "#4F46E5", // Indigo
        "#7C2D12", // Brown
        "#475569", // Slate
    };

    /// <summary>
    /// Raised when the dialog should be closed. True = saved, False = cancelled.
    /// </summary>
    public event Action<bool>? CloseRequested;

    public ProjectEditViewModel(IProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>
    /// Initializes for adding a new project.
    /// </summary>
    public void InitializeForAdd()
    {
        _isEditing = false;
        _originalId = null;
        DialogTitle = "Add Project";
        Name = string.Empty;
        Path = string.Empty;
        DefaultShell = ShellType.WSL;
        Color = "#7C3AED";
        Icon = "\uE74E";
        StartupCommands = new ObservableCollection<string>();
        ErrorMessage = null;
        ValidateFields();
    }

    /// <summary>
    /// Initializes for editing an existing project.
    /// </summary>
    public void InitializeForEdit(Project project)
    {
        _isEditing = true;
        _originalId = project.Id;
        DialogTitle = "Edit Project";
        Name = project.Name;
        Path = project.Path;
        DefaultShell = project.DefaultShell;
        Color = project.Color;
        Icon = project.Icon;
        StartupCommands = new ObservableCollection<string>(project.StartupCommands);
        ErrorMessage = null;
        ValidateFields();
    }

    /// <summary>
    /// Opens a folder browser dialog to select the project path.
    /// </summary>
    [RelayCommand]
    private void BrowsePath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Project Folder"
        };

        if (dialog.ShowDialog() == true)
        {
            Path = dialog.FolderName;
            if (string.IsNullOrEmpty(Name))
            {
                Name = System.IO.Path.GetFileName(Path);
            }

            // Auto-detect project type and suggest shell
            var projectType = _projectService.DetectProjectType(Path);
            if (projectType == ProjectType.Laravel || projectType == ProjectType.NodeJs ||
                projectType == ProjectType.TypeScript || projectType == ProjectType.React ||
                projectType == ProjectType.Vue || projectType == ProjectType.NextJs ||
                projectType == ProjectType.Python)
            {
                DefaultShell = ShellType.WSL;
            }

            ValidateFields();
        }
    }

    /// <summary>
    /// Adds a startup command to the list.
    /// </summary>
    [RelayCommand]
    private void AddStartupCommand()
    {
        if (string.IsNullOrWhiteSpace(NewStartupCommand)) return;
        StartupCommands.Add(NewStartupCommand.Trim());
        NewStartupCommand = string.Empty;
    }

    /// <summary>
    /// Removes a startup command by index.
    /// </summary>
    [RelayCommand]
    private void RemoveStartupCommand(string? command)
    {
        if (command != null)
            StartupCommands.Remove(command);
    }

    /// <summary>
    /// Saves the project (add or update).
    /// </summary>
    [RelayCommand]
    private async Task Save()
    {
        ValidateFields();
        if (!IsValid) return;

        var project = new Project
        {
            Id = _originalId ?? Guid.NewGuid().ToString("N"),
            Name = Name.Trim(),
            Path = Path.Trim(),
            DefaultShell = DefaultShell,
            Color = Color,
            Icon = Icon,
            StartupCommands = StartupCommands.ToList(),
            ProjectType = _projectService.DetectProjectType(Path)
        };

        if (_isEditing)
            await _projectService.UpdateProjectAsync(project);
        else
            await _projectService.AddProjectAsync(project);

        CloseRequested?.Invoke(true);
    }

    /// <summary>
    /// Cancels the dialog.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(false);
    }

    partial void OnNameChanged(string value) => ValidateFields();
    partial void OnPathChanged(string value) => ValidateFields();

    private void ValidateFields()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Project name is required.";
            IsValid = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(Path))
        {
            ErrorMessage = "Project path is required.";
            IsValid = false;
            return;
        }

        if (!System.IO.Directory.Exists(Path))
        {
            ErrorMessage = "Project path does not exist.";
            IsValid = false;
            return;
        }

        ErrorMessage = null;
        IsValid = true;
    }
}
