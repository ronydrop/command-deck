namespace CommandDeck.Models;

/// <summary>
/// Represents a Git worktree entry.
/// </summary>
public class GitWorktreeInfo
{
    public string Path { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string CommitHash { get; set; } = string.Empty;
    public bool IsMain { get; set; }
    public bool IsBare { get; set; }
}
