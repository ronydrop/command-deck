using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <summary>
/// One-time migration service that imports existing JSON data into SQLite.
/// Detects workspaces/*.json and settings.json in %APPDATA%/DevWorkspaceHub/,
/// imports their contents into the database, and renames originals to .json.bak.
/// </summary>
/// <remarks>
/// Migration is idempotent — it skips files that have already been migrated
/// (detected by the .json.bak suffix or by checking if data already exists
/// in the database for the given id/key).
/// </remarks>
public sealed class MigrationService
{
    private readonly IPersistenceService _persistence;
    private readonly string _appDataPath;
    private readonly string _workspacesDir;
    private readonly string _settingsFilePath;
    private readonly string _migrationFlagPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public MigrationService(IPersistenceService persistence, string? appDataPath = null)
    {
        _persistence = persistence;
        _appDataPath = appDataPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DevWorkspaceHub");
        _workspacesDir = Path.Combine(_appDataPath, "workspaces");
        _settingsFilePath = Path.Combine(_appDataPath, "settings.json");
        _migrationFlagPath = Path.Combine(_appDataPath, ".migration_v1_complete");
    }

    /// <summary>
    /// Runs the one-time migration from JSON files to SQLite.
    /// Safe to call multiple times — uses a flag file to prevent re-import.
    /// </summary>
    /// <param name="force">If true, re-runs migration even if flag file exists.</param>
    public async Task<bool> MigrateAsync(bool force = false)
    {
        // Check if migration already completed
        if (!force && File.Exists(_migrationFlagPath))
        {
            System.Diagnostics.Debug.WriteLine("[Migration] Already completed, skipping.");
            return false;
        }

        int importedWorkspaces = 0;
        int importedSettings = 0;

        // ─── Migrate Workspace JSON files ───────────────────────────────
        if (Directory.Exists(_workspacesDir))
        {
            var jsonFiles = Directory.GetFiles(_workspacesDir, "*.json");
            foreach (var filePath in jsonFiles)
            {
                try
                {
                    var fileName = Path.GetFileName(filePath);
                    var workspaceId = Path.GetFileNameWithoutExtension(fileName);

                    // Skip .bak files
                    if (fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var json = await File.ReadAllTextAsync(filePath);
                    if (string.IsNullOrWhiteSpace(json))
                        continue;

                    var workspace = JsonSerializer.Deserialize<WorkspaceModel>(json, JsonOptions);
                    if (workspace is null)
                        continue;

                    // Ensure the Id matches the filename
                    workspace.Id = workspaceId;

                    // Check if already in DB
                    var existing = await _persistence.LoadWorkspaceAsync(workspaceId);
                    if (existing is not null)
                    {
                        // Already imported — just backup the file
                        BackupFile(filePath);
                        continue;
                    }

                    await _persistence.SaveWorkspaceAsync(workspace);
                    BackupFile(filePath);
                    importedWorkspaces++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Migration] Error migrating workspace file '{filePath}': {ex.Message}");
                }
            }
        }

        // ─── Migrate Settings JSON ──────────────────────────────────────
        if (File.Exists(_settingsFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_settingsFilePath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (settings is not null)
                    {
                        // Store each setting property as a key-value pair
                        await SaveSettingsToDbAsync(settings);
                        BackupFile(_settingsFilePath);
                        importedSettings++;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Migration] Error migrating settings: {ex.Message}");
            }
        }

        // ─── Write completion flag ──────────────────────────────────────
        if (importedWorkspaces > 0 || importedSettings > 0 || force)
        {
            try
            {
                await File.WriteAllTextAsync(_migrationFlagPath,
                    $"Migrated at {DateTime.UtcNow:O}\n" +
                    $"Workspaces: {importedWorkspaces}\n" +
                    $"Settings: {importedSettings}\n");
            }
            catch { /* best effort */ }
        }

        System.Diagnostics.Debug.WriteLine(
            $"[Migration] Complete. Workspaces: {importedWorkspaces}, Settings: {importedSettings}");

        return importedWorkspaces > 0 || importedSettings > 0;
    }

    // ─── Private Helpers ───────────────────────────────────────────────────

    private async Task SaveSettingsToDbAsync(AppSettings settings)
    {
        // Store the entire AppSettings as a single serialized object
        // under a known key, plus individual keys for quick access
        await _persistence.SaveSettingAsync("_appsettings_full", settings);

        // Also store individual values for granular access
        await _persistence.SaveSettingAsync("TerminalFontFamily", settings.TerminalFontFamily);
        await _persistence.SaveSettingAsync("TerminalFontSize", settings.TerminalFontSize);
        await _persistence.SaveSettingAsync("DefaultShell", settings.DefaultShell.ToString());
        await _persistence.SaveSettingAsync("ProjectScanDirectory", settings.ProjectScanDirectory);
        await _persistence.SaveSettingAsync("ProjectScanMaxDepth", settings.ProjectScanMaxDepth);
        await _persistence.SaveSettingAsync("GitRefreshIntervalSeconds", settings.GitRefreshIntervalSeconds);
        await _persistence.SaveSettingAsync("ProcessMonitorIntervalSeconds", settings.ProcessMonitorIntervalSeconds);
        await _persistence.SaveSettingAsync("StartWithLastProject", settings.StartWithLastProject);
        await _persistence.SaveSettingAsync("LastOpenedProjectId", settings.LastOpenedProjectId);
        await _persistence.SaveSettingAsync("WindowWidth", settings.WindowWidth);
        await _persistence.SaveSettingAsync("WindowHeight", settings.WindowHeight);
        await _persistence.SaveSettingAsync("IsMaximized", settings.IsMaximized);
    }

    private static void BackupFile(string filePath)
    {
        var bakPath = filePath + ".bak";

        // Don't overwrite existing backups
        if (File.Exists(bakPath))
            return;

        try
        {
            File.Copy(filePath, bakPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Migration] Could not backup '{filePath}': {ex.Message}");
        }
    }
}
