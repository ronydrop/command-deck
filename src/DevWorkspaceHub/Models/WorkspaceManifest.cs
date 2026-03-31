namespace DevWorkspaceHub.Models;

/// <summary>
/// Merge strategy used when importing a workspace or settings that already exist.
/// </summary>
public enum MergeStrategy
{
    /// <summary>Overwrites everything — existing workspace/settings are fully replaced.</summary>
    Replace,

    /// <summary>Adds new items without duplicating existing ones (matched by Id).</summary>
    Merge,

    /// <summary>Skips the import if the workspace or settings already exist.</summary>
    Skip
}

/// <summary>
/// Manifest embedded at the root of every .dwhz export file.
/// Contains metadata about the export: versioning, checksum, content flags.
/// </summary>
public class WorkspaceManifest
{
    /// <summary>
    /// Schema version of the .dwhz format (semver).
    /// Incremented when the export structure changes incompatibly.
    /// </summary>
    public string ExportVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Application version that created this export.
    /// Populated from assembly metadata at export time.
    /// </summary>
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of when the export was created (ISO 8601).
    /// </summary>
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Id of the workspace that was exported.
    /// </summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name of the workspace at export time.
    /// </summary>
    public string WorkspaceName { get; set; } = string.Empty;

    /// <summary>
    /// Total number of canvas items in the exported workspace.
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// Whether the export contains a settings.json entry.
    /// </summary>
    public bool SettingsIncluded { get; set; }

    /// <summary>
    /// Whether the export contains a sessions.json entry.
    /// </summary>
    public bool SessionsIncluded { get; set; }

    /// <summary>
    /// Whether the export contains a hierarchy.json entry.
    /// </summary>
    public bool HierarchyIncluded { get; set; }

    /// <summary>
    /// SHA-256 checksum of the concatenated content bytes (all JSON entries
    /// excluding manifest.json itself). Used to detect corruption or tampering.
    /// </summary>
    public string ContentChecksum { get; set; } = string.Empty;
}
