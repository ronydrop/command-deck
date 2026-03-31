namespace CommandDeck.Models;

/// <summary>
/// Represents a Git branch (local or remote).
/// </summary>
public class GitBranchInfo
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty; // without "remotes/origin/" prefix
    public bool IsRemote { get; set; }
    public bool IsCurrent { get; set; }
    public string? RemoteName { get; set; } // e.g. "origin"
}
