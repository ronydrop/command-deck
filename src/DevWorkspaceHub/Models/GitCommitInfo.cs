namespace DevWorkspaceHub.Models;

/// <summary>
/// Represents a single Git commit entry for display.
/// </summary>
public class GitCommitInfo
{
    public string Hash { get; set; } = "";
    public string Message { get; set; } = "";
    public string Author { get; set; } = "";
    public string RelativeTime { get; set; } = "";
}
