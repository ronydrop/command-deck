using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// ViewModel for the worktree selector popup. Handles listing, creating, and removing git worktrees.
/// </summary>
public partial class WorktreeSelectorViewModel : ObservableObject
{
    private readonly IGitService _gitService;
    private readonly INotificationService _notificationService;

    [ObservableProperty] private ObservableCollection<GitWorktreeInfo> _worktrees = new();
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isCreating;
    [ObservableProperty] private string _newWorktreePath = string.Empty;
    [ObservableProperty] private string _newWorktreeBranch = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _repositoryPath = string.Empty;

    /// <summary>Fired when the user selects an existing worktree path.</summary>
    public event Action<string>? WorktreeSelected;

    public WorktreeSelectorViewModel(IGitService gitService, INotificationService notificationService)
    {
        _gitService = gitService;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Loads worktrees for the given repository and opens the popup.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync(string repositoryPath)
    {
        RepositoryPath = repositoryPath;
        ErrorMessage = null;
        IsCreating = false;
        NewWorktreeBranch = string.Empty;
        NewWorktreePath = string.Empty;

        var list = await _gitService.GetWorktreesAsync(repositoryPath);

        Worktrees.Clear();
        foreach (var wt in list)
            Worktrees.Add(wt);
    }

    /// <summary>
    /// Selects a worktree and fires the WorktreeSelected event.
    /// </summary>
    [RelayCommand]
    public void SelectWorktree(GitWorktreeInfo worktree)
    {
        WorktreeSelected?.Invoke(worktree.Path);
        IsOpen = false;
    }

    /// <summary>
    /// Shows the inline form to create a new worktree.
    /// </summary>
    [RelayCommand]
    public void ShowCreateForm()
    {
        ErrorMessage = null;
        // Suggest a sibling directory: /path/to/repo-branchname
        var parentDir = Path.GetDirectoryName(RepositoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty;
        var repoName = Path.GetFileName(RepositoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        NewWorktreePath = Path.Combine(parentDir, $"{repoName}-worktree");
        NewWorktreeBranch = string.Empty;
        IsCreating = true;
    }

    /// <summary>
    /// Cancels the create form.
    /// </summary>
    [RelayCommand]
    public void CancelCreate()
    {
        IsCreating = false;
        ErrorMessage = null;
    }

    /// <summary>
    /// Creates a new worktree at the specified path with the given branch name.
    /// </summary>
    [RelayCommand]
    public async Task CreateWorktreeAsync()
    {
        if (string.IsNullOrWhiteSpace(NewWorktreePath) || string.IsNullOrWhiteSpace(NewWorktreeBranch))
        {
            ErrorMessage = "Informe o caminho e o nome da branch";
            return;
        }

        ErrorMessage = null;

        var result = await _gitService.CreateWorktreeAsync(RepositoryPath, NewWorktreePath, NewWorktreeBranch);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Erro ao criar worktree";
            return;
        }

        _notificationService.Notify(
            $"Worktree criada em {Path.GetFileName(NewWorktreePath)}",
            NotificationType.Success,
            NotificationSource.Git);

        IsCreating = false;
        await LoadAsync(RepositoryPath);
    }

    /// <summary>
    /// Removes a non-main worktree.
    /// </summary>
    [RelayCommand]
    public async Task RemoveWorktreeAsync(GitWorktreeInfo worktree)
    {
        if (worktree.IsMain) return;

        ErrorMessage = null;

        var result = await _gitService.RemoveWorktreeAsync(worktree.Path);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Erro ao remover worktree";
            return;
        }

        _notificationService.Notify(
            $"Worktree removida: {Path.GetFileName(worktree.Path)}",
            NotificationType.Info,
            NotificationSource.Git);

        await LoadAsync(RepositoryPath);
    }
}
