namespace CommandDeck.Models;

/// <summary>
/// Represents the result of a Git mutation operation.
/// </summary>
public class GitOperationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static GitOperationResult Ok() => new() { Success = true };
    public static GitOperationResult Fail(string error) => new() { Success = false, ErrorMessage = error };
}
