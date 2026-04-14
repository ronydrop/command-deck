using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;

namespace CommandDeck.ViewModels;

/// <summary>
/// Partial class — Advanced Git operations for <see cref="WidgetCanvasItemViewModel"/>.
/// Exposes per-file staging, commit, and stash management through the Git widget UI.
/// All git operations go through <see cref="Services.IGitService"/> and invalidate the cache
/// followed by a full <see cref="RefreshGitAsync"/> to keep the header stats in sync.
/// </summary>
public partial class WidgetCanvasItemViewModel
{
    // ─── Observable collections ───────────────────────────────────────────────

    public ObservableCollection<GitFileChangeViewModel> GitChangedFiles { get; } = new();
    public ObservableCollection<GitStashEntry>         GitStashEntries  { get; } = new();

    [ObservableProperty] private string _commitMessage = string.Empty;
    [ObservableProperty] private bool   _gitIsLoading;
    [ObservableProperty] private bool   _stashSectionExpanded;

    // ─── Refresh ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads (or refreshes) the file change list and stash list for the current repository.
    /// Automatically called after any mutating git operation.
    /// </summary>
    public async Task RefreshGitChangedFilesAsync()
    {
        if (_gitService is null || string.IsNullOrEmpty(RepositoryPath)) return;

        GitIsLoading = true;
        try
        {
            var changes = await _gitService.GetChangedFilesAsync(RepositoryPath).ConfigureAwait(false);
            var staged  = await _gitService.GetStagedFilesAsync(RepositoryPath).ConfigureAwait(false);
            var stagedSet = new HashSet<string>(staged, StringComparer.OrdinalIgnoreCase);

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                GitChangedFiles.Clear();
                foreach (var c in changes)
                    GitChangedFiles.Add(new GitFileChangeViewModel(c, isStaged: stagedSet.Contains(c.FilePath)));
            });

            await RefreshStashListAsync().ConfigureAwait(false);
        }
        finally
        {
            GitIsLoading = false;
        }
    }

    // ─── Per-file staging ─────────────────────────────────────────────────────

    /// <summary>Toggles the staged state of a single file (stage if not staged, unstage if staged).</summary>
    public async Task ToggleStageFileAsync(GitFileChangeViewModel fileVm)
    {
        if (_gitService is null || string.IsNullOrEmpty(RepositoryPath)) return;

        if (fileVm.IsStaged)
            await _gitService.UnstageFileAsync(RepositoryPath, fileVm.FilePath).ConfigureAwait(false);
        else
            await _gitService.StageFileAsync(RepositoryPath, fileVm.FilePath).ConfigureAwait(false);

        _gitService.InvalidateCache(RepositoryPath);
        await RefreshGitChangedFilesAsync().ConfigureAwait(false);
        _ = RefreshGitAsync();
    }

    // ─── Commit ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task CommitChanges()
    {
        if (_gitService is null || string.IsNullOrEmpty(RepositoryPath)) return;
        if (string.IsNullOrWhiteSpace(CommitMessage)) return;

        var result = await _gitService.CommitAsync(RepositoryPath, CommitMessage).ConfigureAwait(false);
        if (result.Success)
        {
            CommitMessage = string.Empty;
            _gitService.InvalidateCache(RepositoryPath);
            await RefreshGitChangedFilesAsync().ConfigureAwait(false);
            _ = RefreshGitAsync();
        }
        else
        {
            _notificationService?.Notify(
                title:   "Git: erro no commit",
                type:    NotificationType.Error,
                source:  NotificationSource.Git,
                message: result.ErrorMessage);
        }
    }

    [RelayCommand]
    private async Task StageAll()
    {
        if (_gitService is null || string.IsNullOrEmpty(RepositoryPath)) return;
        await _gitService.StageAllAsync(RepositoryPath).ConfigureAwait(false);
        _gitService.InvalidateCache(RepositoryPath);
        await RefreshGitChangedFilesAsync().ConfigureAwait(false);
    }

    // ─── Stash ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task StashChanges()
    {
        if (_gitService is null || string.IsNullOrEmpty(RepositoryPath)) return;
        await _gitService.StashSaveAsync(RepositoryPath).ConfigureAwait(false);
        _gitService.InvalidateCache(RepositoryPath);
        await RefreshGitChangedFilesAsync().ConfigureAwait(false);
        _ = RefreshGitAsync();
    }

    [RelayCommand]
    private async Task PopStash()
    {
        if (_gitService is null || string.IsNullOrEmpty(RepositoryPath)) return;
        var result = await _gitService.StashPopAsync(RepositoryPath).ConfigureAwait(false);
        if (!result.Success)
        {
            _notificationService?.Notify(
                title:   "Git: erro ao pop stash",
                type:    NotificationType.Error,
                source:  NotificationSource.Git,
                message: result.ErrorMessage);
        }

        _gitService.InvalidateCache(RepositoryPath);
        await RefreshGitChangedFilesAsync().ConfigureAwait(false);
        _ = RefreshGitAsync();
    }

    private async Task RefreshStashListAsync()
    {
        if (_gitService is null || string.IsNullOrEmpty(RepositoryPath)) return;

        var entries = await _gitService.GetStashListAsync(RepositoryPath).ConfigureAwait(false);

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            GitStashEntries.Clear();
            foreach (var e in entries)
                GitStashEntries.Add(e);
        });
    }
}
