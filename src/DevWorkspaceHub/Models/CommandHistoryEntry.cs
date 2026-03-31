namespace DevWorkspaceHub.Models;

/// <summary>
/// Represents a single terminal command execution stored in SQLite CommandHistory table.
/// </summary>
public class CommandHistoryEntry
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; }
    public bool Success { get; set; } = true;
}
