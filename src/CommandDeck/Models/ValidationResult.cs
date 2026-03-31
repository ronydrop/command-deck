namespace CommandDeck.Models;

/// <summary>
/// Result of validating a .dwhz import file before applying it.
/// Carries errors (blocking), warnings (non-blocking), and the parsed manifest.
/// </summary>
public class ValidationResult
{
    /// <summary>True when the file is structurally valid and the checksum matches.</summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Blocking issues that prevent the import from proceeding.
    /// Examples: missing manifest, checksum mismatch, unsupported version.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Non-blocking informational messages about the import.
    /// Examples: workspace will be merged, sessions were not included.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// The parsed manifest from the file. Null if the manifest could not be read.
    /// </summary>
    public WorkspaceManifest? Manifest { get; set; }

    /// <summary>Convenience: returns a pre-built "valid" result.</summary>
    public static ValidationResult Valid(WorkspaceManifest manifest) => new()
    {
        IsValid = true,
        Manifest = manifest
    };

    /// <summary>Convenience: returns a pre-built "invalid" result with errors.</summary>
    public static ValidationResult Invalid(params string[] errors) => new()
    {
        IsValid = false,
        Errors = new List<string>(errors)
    };
}
