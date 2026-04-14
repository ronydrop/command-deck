using CommunityToolkit.Mvvm.ComponentModel;
using CommandDeck.Models;

namespace CommandDeck.ViewModels;

/// <summary>
/// Observable wrapper around <see cref="GitFileChange"/> that adds two-way binding support
/// for the IsStaged toggle in the Git widget file list.
/// The underlying POCO is not modified — keeping serialization clean.
/// </summary>
public partial class GitFileChangeViewModel : ObservableObject
{
    public GitFileChange Change { get; }

    /// <summary>File path relative to repository root.</summary>
    public string FilePath => Change.FilePath;

    /// <summary>Short status code (M, A, D, R, ?, ...).</summary>
    public string Status => Change.Status;

    /// <summary>Human-readable status label.</summary>
    public string StatusDisplay => Change.StatusDisplay;

    [ObservableProperty] private bool _isStaged;

    public GitFileChangeViewModel(GitFileChange change, bool isStaged = false)
    {
        Change = change;
        _isStaged = isStaged;
    }
}
