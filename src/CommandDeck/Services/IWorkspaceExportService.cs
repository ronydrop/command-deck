using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Exports and imports complete workspaces and settings as portable .dwhz archives.
/// 
/// ARCHITECTURE:
///   - Export: reads workspace + items from persistence, serializes to JSON,
///     packages into a ZIP archive (.dwhz) with a SHA-256 checksummed manifest.
///   - Import: validates the archive, creates an automatic backup, then
///     applies the content according to the chosen MergeStrategy.
///   - Settings-only export/import is also supported.
/// 
/// THREAD-SAFETY:
///   All public methods are protected by a SemaphoreSlim.
///   Export is atomic: writes to a temp file first, then moves into place.
///   Import creates a full backup before any state mutation.
/// </summary>
public interface IWorkspaceExportService
{
    // ─── Workspace export/import ──────────────────────────────────────────

    /// <summary>
    /// Exports a complete workspace (items, hierarchy, sessions, settings) to a .dwhz file.
    /// The export is atomic: if anything fails, no file is created at the target path.
    /// </summary>
    /// <param name="workspaceId">The workspace to export.</param>
    /// <param name="filePath">Destination .dwhz file path.</param>
    /// <exception cref="FileNotFoundException">Workspace not found.</exception>
    /// <exception cref="IOException">Filesystem error during export.</exception>
    Task ExportWorkspaceAsync(string workspaceId, string filePath);

    /// <summary>
    /// Imports a workspace from a .dwhz file.
    /// Validates the archive first. Creates a backup of the current state before applying.
    /// </summary>
    /// <param name="filePath">Source .dwhz file path.</param>
    /// <param name="mergeStrategy">How to handle conflicts with existing data.</param>
    /// <returns>The manifest from the imported archive.</returns>
    /// <exception cref="InvalidOperationException">Validation failed.</exception>
    Task<WorkspaceManifest> ImportWorkspaceAsync(string filePath, MergeStrategy mergeStrategy);

    // ─── Settings-only export/import ─────────────────────────────────────

    /// <summary>
    /// Exports only the application settings to a .dwhz file.
    /// </summary>
    /// <param name="filePath">Destination .dwhz file path.</param>
    Task ExportSettingsAsync(string filePath);

    /// <summary>
    /// Imports settings from a .dwhz file.
    /// The file must contain a settings.json entry (validated before applying).
    /// </summary>
    /// <param name="filePath">Source .dwhz file path.</param>
    /// <param name="mergeStrategy">How to handle existing settings.</param>
    Task ImportSettingsAsync(string filePath, MergeStrategy mergeStrategy);

    // ─── Validation ──────────────────────────────────────────────────────

    /// <summary>
    /// Validates a .dwhz import file without applying any changes.
    /// Checks: file exists, valid ZIP, manifest present and parseable,
    /// checksum integrity, required entries present.
    /// </summary>
    /// <param name="filePath">Path to the .dwhz file to validate.</param>
    Task<ValidationResult> ValidateImportFile(string filePath);

    // ─── Events ──────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when an export or import operation completes.
    /// Parameters: (operation type, target path, success flag).
    /// </summary>
    event Action<string, string, bool>? OperationCompleted;

    /// <summary>
    /// Raised during long operations to report progress (0.0 to 1.0).
    /// </summary>
    event Action<double>? ProgressChanged;
}
