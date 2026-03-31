namespace CommandDeck.Models;

/// <summary>
/// Represents a changed file in the Git working tree.
/// </summary>
public class GitFileChange
{
    public string FilePath { get; set; } = "";
    public string Status { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
}
