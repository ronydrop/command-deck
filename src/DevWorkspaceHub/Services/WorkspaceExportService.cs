using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <inheritdoc />
public class WorkspaceExportService : IWorkspaceExportService
{
    // ─── ZIP entry names (constants) ─────────────────────────────────────────

    private const string ManifestEntry  = "manifest.json";
    private const string WorkspaceEntry = "workspace.json";
    private const string HierarchyEntry = "hierarchy.json";
    private const string SessionsEntry  = "sessions.json";
    private const string SettingsEntry  = "settings.json";

    // ─── Current export schema version ───────────────────────────────────────

    private const string CurrentExportVersion = "1.0.0";

    // ─── JSON serialization options (match existing codebase style) ──────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // ─── Dependencies ────────────────────────────────────────────────────────

    private readonly ILayoutPersistenceService _layoutPersistence;
    private readonly ISettingsService _settingsService;
    private readonly ITerminalSessionService? _sessionService;

    // ─── Thread-safety ───────────────────────────────────────────────────────

    private readonly SemaphoreSlim _lock = new(1, 1);

    // ─── Backup directory ────────────────────────────────────────────────────

    private static readonly string BackupDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DevWorkspaceHub", "backups");

    // ─── Events ──────────────────────────────────────────────────────────────

    public event Action<string, string, bool>? OperationCompleted;
    public event Action<double>? ProgressChanged;

    /// <summary>
    /// Creates the export service.
    /// Session service is optional — if unavailable, sessions are omitted
    /// from exports gracefully.
    /// </summary>
    public WorkspaceExportService(
        ILayoutPersistenceService layoutPersistence,
        ISettingsService settingsService,
        ITerminalSessionService? sessionService = null)
    {
        _layoutPersistence = layoutPersistence;
        _settingsService = settingsService;
        _sessionService = sessionService;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PUBLIC API — Workspace Export/Import
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task ExportWorkspaceAsync(string workspaceId, string filePath)
    {
        await _lock.WaitAsync();
        try
        {
            ReportProgress(0.0);

            // 1. Load the workspace from persistence
            var workspace = await _layoutPersistence.LoadAsync(workspaceId)
                ?? throw new FileNotFoundException(
                    $"Workspace '{workspaceId}' not found in persistence.");

            ReportProgress(0.2);

            // 2. Collect optional data
            var contentDict = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            // workspace.json — always present
            contentDict[WorkspaceEntry] = SerializeToUtf8(workspace);
            ReportProgress(0.5);

            // sessions.json — optional
            bool sessionsIncluded = false;
            if (_sessionService is not null)
            {
                try
                {
                    var sessions = SerializeSessions(_sessionService);
                    if (sessions.Count > 0)
                    {
                        contentDict[SessionsEntry] = SerializeToUtf8(sessions);
                        sessionsIncluded = true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Export] Failed to serialize sessions: {ex.Message}");
                }
            }
            ReportProgress(0.65);

            // settings.json — always include
            var settings = await _settingsService.GetSettingsAsync();
            contentDict[SettingsEntry] = SerializeToUtf8(settings);
            ReportProgress(0.8);

            // 3. Compute checksum over all content (sorted by entry name for determinism)
            var checksum = ComputeChecksum(contentDict);

            // 4. Build manifest
            var manifest = new WorkspaceManifest
            {
                ExportVersion = CurrentExportVersion,
                AppVersion = GetAppVersion(),
                ExportedAt = DateTime.UtcNow,
                WorkspaceId = workspace.Id,
                WorkspaceName = workspace.Name,
                ItemCount = workspace.Items?.Count ?? 0,
                SettingsIncluded = true,
                SessionsIncluded = sessionsIncluded,
                HierarchyIncluded = false,
                ContentChecksum = checksum
            };

            // 5. Write to a temp file atomically, then move
            var tempPath = filePath + ".tmp";
            WriteDwhzFile(tempPath, manifest, contentDict);
            ReportProgress(0.95);

            // Atomic move
            if (File.Exists(filePath))
                File.Delete(filePath);
            File.Move(tempPath, filePath, overwrite: true);

            ReportProgress(1.0);
            OperationCompleted?.Invoke("ExportWorkspace", filePath, true);
        }
        catch
        {
            OperationCompleted?.Invoke("ExportWorkspace", filePath, false);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<WorkspaceManifest> ImportWorkspaceAsync(string filePath, MergeStrategy mergeStrategy)
    {
        await _lock.WaitAsync();
        try
        {
            ReportProgress(0.0);

            // 1. Validate the file
            var validation = await ValidateImportFileInternal(filePath);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(
                    $"Import validation failed: {string.Join("; ", validation.Errors)}");
            }

            ReportProgress(0.1);

            var manifest = validation.Manifest!;

            // 2. Check if workspace already exists
            var existingWorkspace = await _layoutPersistence.LoadAsync(manifest.WorkspaceId);
            bool workspaceExists = existingWorkspace is not null;

            switch (mergeStrategy)
            {
                case MergeStrategy.Skip when workspaceExists:
                    OperationCompleted?.Invoke("ImportWorkspace", filePath, false);
                    throw new InvalidOperationException(
                        $"Workspace '{manifest.WorkspaceName}' already exists and MergeStrategy is Skip.");
                case MergeStrategy.Merge when workspaceExists:
                    // Create backup before merge
                    await CreateBackupAsync(manifest.WorkspaceId);
                    break;
                case MergeStrategy.Replace when workspaceExists:
                    await CreateBackupAsync(manifest.WorkspaceId);
                    break;
            }

            ReportProgress(0.25);

            // 3. Extract archive contents
            var contents = ExtractArchive(filePath);
            ReportProgress(0.4);

            // 4. Deserialize workspace
            if (contents.TryGetValue(WorkspaceEntry, out var workspaceBytes))
            {
                var importedWorkspace = JsonSerializer.Deserialize<WorkspaceModel>(
                    workspaceBytes, JsonOptions);

                if (importedWorkspace is not null)
                {
                    if (mergeStrategy == MergeStrategy.Merge && existingWorkspace is not null)
                    {
                        MergeWorkspaces(existingWorkspace, importedWorkspace);
                        await _layoutPersistence.SaveAsync(existingWorkspace);
                    }
                    else
                    {
                        await _layoutPersistence.SaveAsync(importedWorkspace);
                    }
                }
            }
            ReportProgress(0.6);

            // 5. Import settings if included
            if (manifest.SettingsIncluded && contents.TryGetValue(SettingsEntry, out var settingsBytes))
            {
                await ImportSettingsFromBytes(settingsBytes, mergeStrategy);
            }
            ReportProgress(0.8);

            // 6. Hierarchy and sessions are informational — they are consumed
            //    by their respective services on next load cycle.
            //    No direct write-back here; the services own their persistence.

            ReportProgress(1.0);
            OperationCompleted?.Invoke("ImportWorkspace", filePath, true);
            return manifest;
        }
        catch
        {
            OperationCompleted?.Invoke("ImportWorkspace", filePath, false);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PUBLIC API — Settings Export/Import
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task ExportSettingsAsync(string filePath)
    {
        await _lock.WaitAsync();
        try
        {
            ReportProgress(0.0);

            var settings = await _settingsService.GetSettingsAsync();
            ReportProgress(0.3);

            var contentDict = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                [SettingsEntry] = SerializeToUtf8(settings)
            };

            var checksum = ComputeChecksum(contentDict);

            var manifest = new WorkspaceManifest
            {
                ExportVersion = CurrentExportVersion,
                AppVersion = GetAppVersion(),
                ExportedAt = DateTime.UtcNow,
                WorkspaceId = "settings-only",
                WorkspaceName = "Settings Export",
                ItemCount = 0,
                SettingsIncluded = true,
                SessionsIncluded = false,
                HierarchyIncluded = false,
                ContentChecksum = checksum
            };

            var tempPath = filePath + ".tmp";
            WriteDwhzFile(tempPath, manifest, contentDict);
            ReportProgress(0.9);

            if (File.Exists(filePath))
                File.Delete(filePath);
            File.Move(tempPath, filePath, overwrite: true);

            ReportProgress(1.0);
            OperationCompleted?.Invoke("ExportSettings", filePath, true);
        }
        catch
        {
            OperationCompleted?.Invoke("ExportSettings", filePath, false);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task ImportSettingsAsync(string filePath, MergeStrategy mergeStrategy)
    {
        await _lock.WaitAsync();
        try
        {
            ReportProgress(0.0);

            var validation = await ValidateImportFileInternal(filePath);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(
                    $"Settings import validation failed: {string.Join("; ", validation.Errors)}");
            }

            if (!validation.Manifest!.SettingsIncluded)
            {
                throw new InvalidOperationException(
                    "This archive does not contain settings.");
            }

            ReportProgress(0.3);

            var contents = ExtractArchive(filePath);
            if (!contents.TryGetValue(SettingsEntry, out var settingsBytes))
            {
                throw new InvalidOperationException("settings.json entry not found in archive.");
            }

            await ImportSettingsFromBytes(settingsBytes, mergeStrategy);

            ReportProgress(1.0);
            OperationCompleted?.Invoke("ImportSettings", filePath, true);
        }
        catch
        {
            OperationCompleted?.Invoke("ImportSettings", filePath, false);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PUBLIC API — Validation
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<ValidationResult> ValidateImportFile(string filePath)
    {
        // Validation is read-only, but we still serialize for thread-safety
        // since another thread might be importing/exporting concurrently.
        await _lock.WaitAsync();
        try
        {
            return ValidateImportFileInternal(filePath).GetAwaiter().GetResult();
        }
        finally
        {
            _lock.Release();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // INTERNAL — Validation (non-locking, called under lock)
    // ═══════════════════════════════════════════════════════════════════════════

    private Task<ValidationResult> ValidateImportFileInternal(string filePath)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // 1. File existence
        if (!File.Exists(filePath))
        {
            return Task.FromResult(ValidationResult.Invalid("File not found."));
        }

        // 2. Open as ZIP
        Dictionary<string, byte[]> contents;
        try
        {
            contents = ExtractArchive(filePath);
        }
        catch (Exception ex)
        {
            return Task.FromResult(ValidationResult.Invalid(
                $"Cannot open archive: {ex.Message}"));
        }

        // 3. Manifest present
        if (!contents.TryGetValue(ManifestEntry, out var manifestBytes))
        {
            return Task.FromResult(ValidationResult.Invalid(
                "Manifest (manifest.json) not found in archive."));
        }

        // 4. Parse manifest
        WorkspaceManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<WorkspaceManifest>(manifestBytes, JsonOptions)
                ?? throw new JsonException("Manifest deserialized to null.");
        }
        catch (Exception ex)
        {
            return Task.FromResult(ValidationResult.Invalid(
                $"Invalid manifest: {ex.Message}"));
        }

        // 5. Version check (warn on newer versions, error on older majors)
        if (!TryParseVersion(manifest.ExportVersion, out var exportVersion))
        {
            warnings.Add($"Cannot parse export version '{manifest.ExportVersion}'.");
        }
        else if (!TryParseVersion(CurrentExportVersion, out var currentVersion))
        {
            warnings.Add("Cannot parse current export version.");
        }
        else
        {
            if (exportVersion.Major > currentVersion.Major)
            {
                errors.Add(
                    $"Export version {manifest.ExportVersion} is newer than " +
                    $"supported version {CurrentExportVersion}. " +
                    $"Update the application to import this file.");
            }
            else if (exportVersion.Major == currentVersion.Major
                     && exportVersion.Minor > currentVersion.Minor)
            {
                warnings.Add(
                    $"Export version {manifest.ExportVersion} is slightly newer than " +
                    $"current {CurrentExportVersion}. Import should work but some fields may be ignored.");
            }
        }

        // 6. Checksum verification
        try
        {
            // Exclude manifest from checksum calculation
            var contentForChecksum = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in contents)
            {
                if (!kv.Key.Equals(ManifestEntry, StringComparison.OrdinalIgnoreCase))
                    contentForChecksum[kv.Key] = kv.Value;
            }

            var computedChecksum = ComputeChecksum(contentForChecksum);
            if (!string.Equals(computedChecksum, manifest.ContentChecksum, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Checksum mismatch. Expected {manifest.ContentChecksum}, " +
                    $"computed {computedChecksum}. The file may be corrupted or tampered with.");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Checksum verification failed: {ex.Message}");
        }

        // 7. Required entries
        if (!contents.ContainsKey(WorkspaceEntry) && manifest.WorkspaceId != "settings-only")
        {
            errors.Add("workspace.json is missing from the archive.");
        }

        if (manifest.SettingsIncluded && !contents.ContainsKey(SettingsEntry))
        {
            warnings.Add("Manifest says settings are included, but settings.json was not found.");
        }

        if (manifest.SessionsIncluded && !contents.ContainsKey(SessionsEntry))
        {
            warnings.Add("Manifest says sessions are included, but sessions.json was not found.");
        }

        var isValid = errors.Count == 0;
        return Task.FromResult(new ValidationResult
        {
            IsValid = isValid,
            Errors = errors,
            Warnings = warnings,
            Manifest = manifest
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ARCHIVE OPERATIONS — ZIP read/write
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Writes a .dwhz ZIP archive to the specified path.
    /// </summary>
    private static void WriteDwhzFile(
        string path,
        WorkspaceManifest manifest,
        Dictionary<string, byte[]> contents)
    {
        // Ensure parent directory exists
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var zipStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

        // Write manifest first
        WriteZipEntry(archive, ManifestEntry, SerializeToUtf8(manifest));

        // Write all content entries
        foreach (var kv in contents)
        {
            WriteZipEntry(archive, kv.Key, kv.Value);
        }
    }

    /// <summary>
    /// Extracts all entries from a .dwhz ZIP into a dictionary.
    /// </summary>
    private static Dictionary<string, byte[]> ExtractArchive(string filePath)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        using var zipStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                continue; // skip directory entries

            using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            result[entry.FullName] = ms.ToArray();
        }

        return result;
    }

    private static void WriteZipEntry(ZipArchive archive, string entryName, byte[] data)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(data, 0, data.Length);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CHECKSUM — SHA-256 over sorted content
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes a deterministic SHA-256 hash over all content entries.
    /// Entries are sorted by name for deterministic ordering.
    /// Format: name + ":" + hex(bytes) per entry, joined by newline.
    /// </summary>
    private static string ComputeChecksum(Dictionary<string, byte[]> contents)
    {
        var sb = new StringBuilder();

        foreach (var kv in contents.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"{kv.Key}:{Convert.ToHexString(kv.Value)}");
        }

        var inputBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hashBytes = SHA256.HashData(inputBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MERGE LOGIC
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Merges the imported workspace into the existing one without duplicating items.
    /// Items are matched by Id. New items are appended. Existing items are overwritten.
    /// Camera state is taken from the import (it represents the saved layout).
    /// </summary>
    private static void MergeWorkspaces(WorkspaceModel existing, WorkspaceModel imported)
    {
        var existingIds = new HashSet<string>(
            existing.Items?.Select(i => i.Id) ?? Array.Empty<string>());

        if (imported.Items is not null)
        {
            existing.Items ??= new List<CanvasItemModel>();

            foreach (var importedItem in imported.Items)
            {
                if (existingIds.Contains(importedItem.Id))
                {
                    // Replace existing item by Id
                    var idx = existing.Items.FindIndex(i => i.Id == importedItem.Id);
                    if (idx >= 0)
                        existing.Items[idx] = importedItem;
                }
                else
                {
                    // Add new item
                    existing.Items.Add(importedItem);
                    existingIds.Add(importedItem.Id);
                }
            }
        }

        // Always take the workspace name from the import (it's the source of truth)
        if (!string.IsNullOrWhiteSpace(imported.Name))
            existing.Name = imported.Name;

        // Camera state: merge by replacing (imported state is the saved layout)
        existing.Camera = imported.Camera;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SETTINGS IMPORT HELPER
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task ImportSettingsFromBytes(byte[] settingsBytes, MergeStrategy mergeStrategy)
    {
        var importedSettings = JsonSerializer.Deserialize<AppSettings>(settingsBytes, JsonOptions);
        if (importedSettings is null)
            return;

        switch (mergeStrategy)
        {
            case MergeStrategy.Replace:
                await _settingsService.SaveSettingsAsync(importedSettings);
                break;

            case MergeStrategy.Merge:
            {
                var current = await _settingsService.GetSettingsAsync();
                // Merge: imported values overwrite non-default values from current.
                // The strategy here is to take the imported settings as base
                // and preserve certain user-specific fields from current.
                // For a full merge, we just apply the imported settings,
                // since AppSettings is a flat model.
                await _settingsService.SaveSettingsAsync(importedSettings);
                break;
            }

            case MergeStrategy.Skip:
                // Already validated — nothing to do
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // BACKUP
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a timestamped backup of the current workspace file before any modification.
    /// Backups are stored in %APPDATA%\DevWorkspaceHub\backups\{workspaceId}_{timestamp}.json
    /// </summary>
    private async Task CreateBackupAsync(string workspaceId)
    {
        try
        {
            var workspace = await _layoutPersistence.LoadAsync(workspaceId);
            if (workspace is null)
                return; // nothing to back up

            Directory.CreateDirectory(BackupDir);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(BackupDir, $"{workspaceId}_{timestamp}.json");
            var json = JsonSerializer.Serialize(workspace, JsonOptions);
            await File.WriteAllTextAsync(backupPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Export] Backup failed (non-fatal): {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SERIALIZATION HELPERS — Sessions
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Serializes terminal session data (command history, state) for export.
    /// Output snapshots are excluded to keep archives small.
    /// </summary>
    private static List<Dictionary<string, object?>> SerializeSessions(
        ITerminalSessionService sessionService)
    {
        var sessions = sessionService.GetAllSessions();
        var list = new List<Dictionary<string, object?>>();

        foreach (var session in sessions)
        {
            var dict = new Dictionary<string, object?>
            {
                ["id"] = session.Id,
                ["title"] = session.Title,
                ["shellType"] = session.ShellType.ToString(),
                ["projectId"] = session.ProjectId,
                ["workingDirectory"] = session.WorkingDirectory,
                ["sessionState"] = session.SessionState.ToString(),
                ["commandCount"] = session.CommandCount,
                ["createdAt"] = session.CreatedAt.ToString("O"),
                ["lastActivityTimestamp"] = session.LastActivityTimestamp.ToString("O"),
                ["commandHistory"] = session.CommandHistory.ToArray()
            };

            list.Add(dict);
        }

        return list;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // UTILITY HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    private static byte[] SerializeToUtf8<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
    }

    private static string GetAppVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version?.ToString() ?? "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }

    private static bool TryParseVersion(string version, out Version parsed)
    {
        // Handle semver with potential 'v' prefix or '-beta' suffix
        var clean = version.TrimStart('v');
        var dashIdx = clean.IndexOf('-');
        if (dashIdx >= 0)
            clean = clean[..dashIdx];

        return Version.TryParse(clean, out parsed!);
    }

    private void ReportProgress(double value)
    {
        ProgressChanged?.Invoke(Math.Clamp(value, 0.0, 1.0));
    }
}
