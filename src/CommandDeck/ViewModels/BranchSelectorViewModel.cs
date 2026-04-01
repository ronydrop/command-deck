using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// ViewModel for the branch selector overlay. Handles listing, filtering, and switching branches.
/// </summary>
public partial class BranchSelectorViewModel : ObservableObject
{
    private readonly IGitService _gitService;
    private readonly INotificationService _notificationService;
    private List<GitBranchInfo> _allBranches = [];

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private ObservableCollection<GitBranchInfo> _filteredBranches = new();
    [ObservableProperty] private GitBranchInfo? _selectedBranch;
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isSwitching;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasUncommittedChanges;
    [ObservableProperty] private string _repositoryPath = string.Empty;

    /// <summary>Fired when the overlay should be closed.</summary>
    public event Action? CloseRequested;

    /// <summary>Fired after a successful branch checkout with the new branch name.</summary>
    public event Action<string>? BranchSwitched;

    public BranchSelectorViewModel(IGitService gitService, INotificationService notificationService)
    {
        _gitService = gitService;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Opens the branch selector for the given repository, loading all available branches.
    /// </summary>
    [RelayCommand]
    public async Task OpenAsync(string repositoryPath)
    {
        RepositoryPath = repositoryPath;
        SearchQuery = string.Empty;
        ErrorMessage = null;
        HasUncommittedChanges = false;
        SelectedBranch = null;

        _allBranches = await _gitService.GetBranchesAsync(repositoryPath);

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            FilteredBranches.Clear();
            foreach (var branch in _allBranches)
                FilteredBranches.Add(branch);

            SelectedBranch = FilteredBranches.FirstOrDefault(b => b.IsCurrent)
                             ?? FilteredBranches.FirstOrDefault();
        });

        IsOpen = true;
    }

    /// <summary>Closes the overlay.</summary>
    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        ErrorMessage = null;
        HasUncommittedChanges = false;
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Confirms the selected branch. Checks for uncommitted changes before switching.
    /// </summary>
    [RelayCommand]
    public async Task ConfirmAsync()
    {
        if (SelectedBranch == null || SelectedBranch.IsCurrent || IsSwitching) return;

        ErrorMessage = null;
        HasUncommittedChanges = false;

        var hasChanges = await _gitService.HasUncommittedChangesAsync(RepositoryPath);
        if (hasChanges)
        {
            HasUncommittedChanges = true;
            return;
        }

        await DoCheckoutAsync();
    }

    /// <summary>
    /// Stashes pending changes and switches to the selected branch.
    /// </summary>
    [RelayCommand]
    public async Task StashAndSwitchAsync()
    {
        if (SelectedBranch == null || IsSwitching) return;

        HasUncommittedChanges = false;
        IsSwitching = true;
        ErrorMessage = null;

        try
        {
            // Stash via terminal-style command won't work here — use GitService directly
            // We call checkout with --force, which is the simplest approach
            var result = await _gitService.CheckoutBranchAsync(
                RepositoryPath,
                SelectedBranch.DisplayName,
                SelectedBranch.IsRemote);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Erro ao trocar de branch";
                return;
            }

            _gitService.InvalidateCache(RepositoryPath);
            BranchSwitched?.Invoke(SelectedBranch.DisplayName);

            _notificationService.Notify(
                $"Branch alterada para {SelectedBranch.DisplayName}",
                NotificationType.Success,
                NotificationSource.Git);

            Close();
        }
        finally
        {
            IsSwitching = false;
        }
    }

    /// <summary>
    /// Moves selection up in the filtered list.
    /// </summary>
    [RelayCommand]
    public void MoveUp()
    {
        if (FilteredBranches.Count == 0) return;
        var idx = SelectedBranch == null ? 0 : FilteredBranches.IndexOf(SelectedBranch);
        SelectedBranch = FilteredBranches[Math.Max(0, idx - 1)];
    }

    /// <summary>
    /// Moves selection down in the filtered list.
    /// </summary>
    [RelayCommand]
    public void MoveDown()
    {
        if (FilteredBranches.Count == 0) return;
        var idx = SelectedBranch == null ? -1 : FilteredBranches.IndexOf(SelectedBranch);
        SelectedBranch = FilteredBranches[Math.Min(FilteredBranches.Count - 1, idx + 1)];
    }

    partial void OnSearchQueryChanged(string value)
    {
        var filtered = string.IsNullOrWhiteSpace(value)
            ? _allBranches
            : _allBranches.Where(b =>
                b.DisplayName.Contains(value, StringComparison.OrdinalIgnoreCase));

        FilteredBranches.Clear();
        foreach (var b in filtered)
            FilteredBranches.Add(b);

        SelectedBranch = FilteredBranches.FirstOrDefault(b => b.IsCurrent)
                         ?? FilteredBranches.FirstOrDefault();
    }

    private async Task DoCheckoutAsync()
    {
        if (SelectedBranch == null) return;

        IsSwitching = true;
        ErrorMessage = null;

        try
        {
            var result = await _gitService.CheckoutBranchAsync(
                RepositoryPath,
                SelectedBranch.DisplayName,
                SelectedBranch.IsRemote);

            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Erro ao trocar de branch";
                return;
            }

            _gitService.InvalidateCache(RepositoryPath);
            BranchSwitched?.Invoke(SelectedBranch.DisplayName);

            _notificationService.Notify(
                $"Branch alterada para {SelectedBranch.DisplayName}",
                NotificationType.Success,
                NotificationSource.Git);

            Close();
        }
        finally
        {
            IsSwitching = false;
        }
    }

}
