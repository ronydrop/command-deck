using System.Data;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.Models;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Services;

/// <summary>
/// SQLite-backed implementation of <see cref="IPersistenceService"/>.
/// Uses WAL journal mode, a single connection with async methods, and a
/// version-controlled migration system for schema evolution.
/// </summary>
public sealed partial class PersistenceService : IPersistenceService
{
    private const string CurrentSchemaVersion = "1.0.0";
    private const int DefaultCommandTimeout = 30;

    private readonly string _dbPath;
    private SqliteConnection? _connection;
    private SqliteTransaction? _currentTransaction;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _disposed;

    // JSON serialization options — shared, camelCase, enum-as-string
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public PersistenceService(string? dbPath = null)
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CommandDeck");
        Directory.CreateDirectory(appData);
        _dbPath = dbPath ?? Path.Combine(appData, "commanddeck.db");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Lifecycle
    // ═══════════════════════════════════════════════════════════════════════

    public async Task InitAsync()
    {
        await EnsureConnectionAsync();

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandTimeout = DefaultCommandTimeout;

        // Enable WAL mode for concurrent read/write performance
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = "PRAGMA synchronous=NORMAL;";
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = "PRAGMA foreign_keys=ON;";
        await cmd.ExecuteNonQueryAsync();

        // Create schema version tracking table
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS schema_version (
                version       TEXT    NOT NULL,
                applied_at    TEXT    NOT NULL DEFAULT (datetime('now')),
                description   TEXT
            );";
        await cmd.ExecuteNonQueryAsync();

        // Run pending migrations
        await RunMigrationsAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Workspaces
    // ═══════════════════════════════════════════════════════════════════════

    public async Task SaveWorkspaceAsync(WorkspaceModel workspace)
    {
        var cameraJson = JsonSerializer.Serialize(workspace.Camera, JsonOpts);
        var itemsJson = JsonSerializer.Serialize(workspace.Items, JsonOpts);
        var settingsJson = JsonSerializer.Serialize(workspace.Settings, JsonOpts);

        await using var cmd = CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workspaces (id, name, color, icon, is_active, camera_json, items_json, settings_json, last_accessed_at, updated_at)
            VALUES ($id, $name, $color, $icon, $isActive, $camera, $items, $settings, $lastAccessed, $updated)
            ON CONFLICT(id) DO UPDATE SET
                name             = excluded.name,
                color            = excluded.color,
                icon             = excluded.icon,
                is_active        = excluded.is_active,
                camera_json      = excluded.camera_json,
                items_json       = excluded.items_json,
                settings_json    = excluded.settings_json,
                last_accessed_at = excluded.last_accessed_at,
                updated_at       = excluded.updated_at;";
        cmd.Parameters.AddWithValue("$id", workspace.Id);
        cmd.Parameters.AddWithValue("$name", workspace.Name);
        cmd.Parameters.AddWithValue("$color", workspace.Color);
        cmd.Parameters.AddWithValue("$icon", workspace.Icon);
        cmd.Parameters.AddWithValue("$isActive", workspace.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$camera", cameraJson);
        cmd.Parameters.AddWithValue("$items", itemsJson);
        cmd.Parameters.AddWithValue("$settings", settingsJson);
        cmd.Parameters.AddWithValue("$lastAccessed", workspace.LastAccessedAt.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<WorkspaceModel?> LoadWorkspaceAsync(string id)
    {
        await using var cmd = CreateCommand();
        cmd.CommandText = @"
            SELECT name, camera_json, items_json, color, icon, is_active,
                   settings_json, created_at, last_accessed_at
            FROM workspaces WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return MapWorkspaceFromReader(id, reader);
    }

    public async Task<IReadOnlyList<WorkspaceModel>> ListWorkspacesAsync()
    {
        var results = new List<WorkspaceModel>();

        await using var cmd = CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, camera_json, items_json, color, icon, is_active,
                   settings_json, created_at, last_accessed_at
            FROM workspaces
            ORDER BY is_active DESC, last_accessed_at DESC;";
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var id = reader.GetString(0);
            results.Add(MapWorkspaceFromReader(id, reader, idOffset: 1));
        }

        return results;
    }

    public async Task<WorkspaceModel?> GetActiveWorkspaceAsync()
    {
        await using var cmd = CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, camera_json, items_json, color, icon, is_active,
                   settings_json, created_at, last_accessed_at
            FROM workspaces WHERE is_active = 1 LIMIT 1;";
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        var id = reader.GetString(0);
        return MapWorkspaceFromReader(id, reader, idOffset: 1);
    }

    public async Task SetActiveWorkspaceAsync(string id)
    {
        await EnsureConnectionAsync();
        await using var transaction = _connection!.BeginTransaction();
        try
        {
            await using var deactivate = CreateCommand();
            deactivate.Transaction = transaction;
            deactivate.CommandText = "UPDATE workspaces SET is_active = 0 WHERE is_active = 1;";
            await deactivate.ExecuteNonQueryAsync();

            await using var activate = CreateCommand();
            activate.Transaction = transaction;
            activate.CommandText = @"
                UPDATE workspaces SET is_active = 1, last_accessed_at = $now
                WHERE id = $id;";
            activate.Parameters.AddWithValue("$id", id);
            activate.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            await activate.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> DeleteWorkspaceAsync(string id)
    {
        await using var cmd = CreateCommand();
        cmd.CommandText = "DELETE FROM workspaces WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Projects
    // ═══════════════════════════════════════════════════════════════════════

    public async Task SaveProjectAsync(Project project)
    {
        var commandsJson = JsonSerializer.Serialize(project.StartupCommands, JsonOpts);

        await using var cmd = CreateCommand();
        cmd.CommandText = @"
            INSERT INTO projects (
                id, name, path, default_shell, startup_commands_json,
                color, icon, last_opened, is_favorite, project_type, updated_at)
            VALUES ($id, $name, $path, $shell, $commands,
                    $color, $icon, $lastOpened, $fav, $type, $updated)
            ON CONFLICT(id) DO UPDATE SET
                name                = excluded.name,
                path                = excluded.path,
                default_shell       = excluded.default_shell,
                startup_commands_json = excluded.startup_commands_json,
                color               = excluded.color,
                icon                = excluded.icon,
                last_opened         = excluded.last_opened,
                is_favorite         = excluded.is_favorite,
                project_type        = excluded.project_type,
                updated_at          = excluded.updated_at;";
        cmd.Parameters.AddWithValue("$id", project.Id);
        cmd.Parameters.AddWithValue("$name", project.Name);
        cmd.Parameters.AddWithValue("$path", project.Path);
        cmd.Parameters.AddWithValue("$shell", project.DefaultShell.ToString());
        cmd.Parameters.AddWithValue("$commands", commandsJson);
        cmd.Parameters.AddWithValue("$color", project.Color);
        cmd.Parameters.AddWithValue("$icon", project.Icon);
        cmd.Parameters.AddWithValue("$lastOpened", project.LastOpened == DateTime.MinValue
            ? (object)DBNull.Value
            : project.LastOpened.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("$fav", project.IsFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$type", project.ProjectType.ToString());
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Project?> LoadProjectAsync(string id)
    {
        await using var cmd = CreateCommand();
        cmd.CommandText = @"
            SELECT name, path, default_shell, startup_commands_json,
                   color, icon, last_opened, is_favorite, project_type
            FROM projects WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapProjectFromReader(id, reader);
    }

    public async Task<IReadOnlyList<Project>> ListProjectsAsync(bool orderByLastOpened = false)
    {
        var results = new List<Project>();
        var orderBy = orderByLastOpened
            ? "ORDER BY CASE WHEN last_opened IS NULL THEN 1 ELSE 0 END, last_opened DESC"
            : "ORDER BY name ASC";

        await using var cmd = CreateCommand();
        cmd.CommandText = $"SELECT id, name, path, default_shell, startup_commands_json, " +
                          $"color, icon, last_opened, is_favorite, project_type FROM projects {orderBy};";
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var id = reader.GetString(0);
            results.Add(MapProjectFromReader(id, reader, idOffset: 1));
        }

        return results;
    }

    public async Task<bool> DeleteProjectAsync(string id)
    {
        await using var cmd = CreateCommand();
        cmd.CommandText = "DELETE FROM projects WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Terminal Sessions
    // ═══════════════════════════════════════════════════════════════════════

    public async Task SaveTerminalSessionAsync(TerminalSessionModel session)
    {
        await using var cmd = CreateCommand();
        cmd.CommandText = @"
            INSERT INTO terminal_sessions (
                id, title, shell_type, project_id, working_directory,
                session_state, last_activity, created_at, closed_at,
                error_code, error_message, updated_at)
            VALUES ($id, $title, $shell, $projId, $workDir,
                    $state, $lastAct, $createdAt, $closedAt,
                    $errCode, $errMsg, $updated)
            ON CONFLICT(id) DO UPDATE SET
                title             = excluded.title,
                shell_type        = excluded.shell_type,
                project_id        = excluded.project_id,
                working_directory = excluded.working_directory,
                session_state     = excluded.session_state,
                last_activity     = excluded.last_activity,
                closed_at         = excluded.closed_at,
                error_code        = excluded.error_code,
                error_message     = excluded.error_message,
                updated_at        = excluded.updated_at;";
        cmd.Parameters.AddWithValue("$id", session.Id);
        cmd.Parameters.AddWithValue("$title", session.Title);
        cmd.Parameters.AddWithValue("$shell", session.ShellType.ToString());
        cmd.Parameters.AddWithValue("$projId", (object?)session.ProjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$workDir", session.WorkingDirectory);
        cmd.Parameters.AddWithValue("$state", session.SessionState.ToString());
        cmd.Parameters.AddWithValue("$lastAct", session.LastActivityTimestamp.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("$createdAt", session.CreatedAt.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("$closedAt", session.ClosedAt?.ToUniversalTime().ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$errCode", session.ErrorCode);
        cmd.Parameters.AddWithValue("$errMsg", session.ErrorMessage);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();

        // Persist command history entries
        // Delete old entries for this session and re-insert current ones
        await DeleteCommandHistoryAsync(session.Id);

        foreach (var cmdText in session.CommandHistory.GetAll())
        {
            await SaveCommandHistoryEntryAsync(session.Id, cmdText, DateTime.UtcNow);
        }
    }

    public async Task<TerminalSessionModel?> LoadTerminalSessionAsync(string id)
    {
        await using var cmd = CreateCommand();
        cmd.CommandText = @"
            SELECT title, shell_type, project_id, working_directory,
                   session_state, last_activity, created_at, closed_at,
                   error_code, error_message
            FROM terminal_sessions WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        var session = new TerminalSessionModel
        {
            Id = id,
            Title = reader.GetString(0),
            ShellType = Enum.Parse<ShellType>(reader.GetString(1)),
            ProjectId = reader.IsDBNull(2) ? null : reader.GetString(2),
            WorkingDirectory = reader.GetString(3),
            SessionState = Enum.Parse<SessionState>(reader.GetString(4)),
            LastActivityTimestamp = DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
            CreatedAt = DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
            ErrorCode = reader.GetInt32(8),
            ErrorMessage = reader.GetString(9)
        };

        if (!reader.IsDBNull(7))
            session.ClosedAt = DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind);

        // Load command history
        var history = await LoadCommandHistoryAsync(id);
        foreach (var (command, _) in history)
            session.RecordCommand(command);

        return session;
    }

    public async Task<IReadOnlyList<TerminalSessionModel>> ListSessionsByWorkspaceAsync(string workspaceId)
    {
        // Load the workspace to find terminal item project IDs
        var workspace = await LoadWorkspaceAsync(workspaceId);
        if (workspace is null)
            return Array.Empty<TerminalSessionModel>();

        // Find project IDs from terminal canvas items
        var projectIds = workspace.Items
            .Where(i => i.Type == CanvasItemType.Terminal)
            .Select(i => i.Metadata.GetValueOrDefault("projectId"))
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToList();

        if (projectIds.Count == 0)
            return Array.Empty<TerminalSessionModel>();

        var results = new List<TerminalSessionModel>();
        var inClause = string.Join(",", projectIds.Select((_, i) => $"$p{i}"));

        await using var cmd = CreateCommand();
        cmd.CommandText = $@"
            SELECT id, title, shell_type, project_id, working_directory,
                   session_state, last_activity, created_at, closed_at,
                   error_code, error_message
            FROM terminal_sessions
            WHERE project_id IN ({inClause})
            ORDER BY last_activity DESC;";
        for (int i = 0; i < projectIds.Count; i++)
            cmd.Parameters.AddWithValue($"$p{i}", projectIds[i]);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var sessionId = reader.GetString(0);
            var session = new TerminalSessionModel
            {
                Id = sessionId,
                Title = reader.GetString(1),
                ShellType = Enum.Parse<ShellType>(reader.GetString(2)),
                ProjectId = reader.IsDBNull(3) ? null : reader.GetString(3),
                WorkingDirectory = reader.GetString(4),
                SessionState = Enum.Parse<SessionState>(reader.GetString(5)),
                LastActivityTimestamp = DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
                CreatedAt = DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind),
                ErrorCode = reader.GetInt32(9),
                ErrorMessage = reader.GetString(10)
            };

            if (!reader.IsDBNull(8))
                session.ClosedAt = DateTime.Parse(reader.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind);

            results.Add(session);
        }

        return results;
    }

    public async Task<int> DeleteOldSessionsAsync(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow.Subtract(maxAge);

        await using var cmd = CreateCommand();
        cmd.CommandText = @"
            DELETE FROM command_history WHERE session_id IN (
                SELECT id FROM terminal_sessions WHERE last_activity < $cutoff
            );
            DELETE FROM terminal_sessions WHERE last_activity < $cutoff;";
        cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("o"));
        var result = await cmd.ExecuteNonQueryAsync();
        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // App Settings (Key-Value)
    // ═══════════════════════════════════════════════════════════════════════

    public async Task SaveSettingAsync(string key, object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOpts);
        await SaveSettingRawAsync(key, json);
    }

    public async Task<T?> LoadSettingAsync<T>(string key)
    {
        var raw = await LoadSettingRawAsync(key);
        if (raw is null) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(raw, JsonOpts);
        }
        catch
        {
            return default;
        }
    }

    public async Task<string?> LoadSettingRawAsync(string key)
    {
        await using var cmd = CreateCommand();
        cmd.CommandText = "SELECT value FROM app_settings WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadAllSettingsAsync()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = CreateCommand();
        cmd.CommandText = "SELECT key, value FROM app_settings;";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            dict[reader.GetString(0)] = reader.GetString(1);
        return dict;
    }

    private async Task SaveSettingRawAsync(string key, string value)
    {
        await using var cmd = CreateCommand();
        cmd.CommandText = @"
            INSERT INTO app_settings (key, value, updated_at)
            VALUES ($key, $value, $updated)
            ON CONFLICT(key) DO UPDATE SET
                value      = excluded.value,
                updated_at = excluded.updated_at;";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Command History
    // ═══════════════════════════════════════════════════════════════════════

    public async Task SaveCommandHistoryEntryAsync(string sessionId, string command, DateTime executedAt)
    {
        await using var cmd = CreateCommand();
        cmd.CommandText = @"
            INSERT INTO command_history (session_id, command, executed_at)
            VALUES ($sessionId, $command, $executedAt);";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$command", command);
        cmd.Parameters.AddWithValue("$executedAt", executedAt.ToUniversalTime().ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<(string Command, DateTime ExecutedAt)>> LoadCommandHistoryAsync(
        string sessionId, int limit = 500)
    {
        var results = new List<(string Command, DateTime ExecutedAt)>();
        await using var cmd = CreateCommand();
        cmd.CommandText = @"
            SELECT command, executed_at FROM command_history
            WHERE session_id = $sessionId
            ORDER BY executed_at DESC
            LIMIT $limit;";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add((
                reader.GetString(0),
                DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind)
            ));
        }

        // Reverse so index 0 = oldest (matching in-memory queue order)
        results.Reverse();
        return results;
    }

    public async Task DeleteCommandHistoryAsync(string sessionId)
    {
        await using var cmd = CreateCommand();
        cmd.CommandText = "DELETE FROM command_history WHERE session_id = $sessionId;";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Transactions
    // ═══════════════════════════════════════════════════════════════════════

    public async Task BeginTransactionAsync()
    {
        await EnsureConnectionAsync();
        if (_currentTransaction is not null)
            throw new InvalidOperationException("A transaction is already active.");
        _currentTransaction = _connection!.BeginTransaction();
    }

    public async Task CommitAsync()
    {
        if (_currentTransaction is null)
            throw new InvalidOperationException("No active transaction to commit.");
        await _currentTransaction.CommitAsync();
        await _currentTransaction.DisposeAsync();
        _currentTransaction = null;
    }

    public async Task RollbackAsync()
    {
        if (_currentTransaction is null)
            throw new InvalidOperationException("No active transaction to rollback.");
        await _currentTransaction.RollbackAsync();
        await _currentTransaction.DisposeAsync();
        _currentTransaction = null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Maintenance
    // ═══════════════════════════════════════════════════════════════════════

    public async Task VacuumAsync()
    {
        await using var cmd = CreateCommand();
        cmd.CommandTimeout = 120;
        cmd.CommandText = "VACUUM;";
        await cmd.ExecuteNonQueryAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Dispose
    // ═══════════════════════════════════════════════════════════════════════

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_currentTransaction is not null)
        {
            _currentTransaction.Rollback();
            _currentTransaction.Dispose();
            _currentTransaction = null;
        }

        _connection?.Close();
        _connection?.Dispose();
        _connectionLock.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Private Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private async Task EnsureConnectionAsync()
    {
        if (_connection is not null) return;

        await _connectionLock.WaitAsync();
        try
        {
            if (_connection is not null) return; // double-check

            _connection = new SqliteConnection($"Data Source={_dbPath}");
            await _connection.OpenAsync();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Creates a command bound to the current connection/transaction.
    /// </summary>
    private SqliteCommand CreateCommand()
    {
        if (_connection is null)
            throw new InvalidOperationException("PersistenceService not initialized. Call InitAsync() first.");

        var cmd = _connection.CreateCommand();
        cmd.CommandTimeout = DefaultCommandTimeout;
        if (_currentTransaction is not null)
            cmd.Transaction = _currentTransaction;
        return cmd;
    }

    /// <summary>
    /// Maps a DbDataReader row to a WorkspaceModel.
    /// Expected column order: name(0), camera_json(1), items_json(2), color(3), icon(4),
    /// is_active(5), settings_json(6), created_at(7), last_accessed_at(8).
    /// When idOffset is provided (e.g. List includes id in col 0), all indices shift.
    /// </summary>
    private static WorkspaceModel MapWorkspaceFromReader(string id, SqliteDataReader reader, int idOffset = 0)
    {
        var cameraJson = reader.GetString(1 + idOffset);
        var itemsJson = reader.GetString(2 + idOffset);
        var settingsJson = reader.IsDBNull(6 + idOffset) ? "{}" : reader.GetString(6 + idOffset);

        return new WorkspaceModel
        {
            Id = id,
            Name = reader.GetString(0 + idOffset),
            Camera = JsonSerializer.Deserialize<CameraStateModel>(cameraJson, JsonOpts) ?? new(),
            Items = JsonSerializer.Deserialize<List<CanvasItemModel>>(itemsJson, JsonOpts) ?? new(),
            Color = reader.IsDBNull(3 + idOffset) ? "#CBA6F7" : reader.GetString(3 + idOffset),
            Icon = reader.IsDBNull(4 + idOffset) ? "FolderIcon" : reader.GetString(4 + idOffset),
            IsActive = reader.GetInt32(5 + idOffset) != 0,
            Settings = JsonSerializer.Deserialize<WorkspaceSettings>(settingsJson, JsonOpts) ?? new(),
            CreatedAt = reader.IsDBNull(7 + idOffset)
                ? DateTime.UtcNow
                : DateTime.Parse(reader.GetString(7 + idOffset), null, System.Globalization.DateTimeStyles.RoundtripKind),
            LastAccessedAt = reader.IsDBNull(8 + idOffset)
                ? DateTime.UtcNow
                : DateTime.Parse(reader.GetString(8 + idOffset), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }

    /// <summary>
    /// Maps a DbDataReader row to a Project model.
    /// Expected column order: name(0), path(1), default_shell(2), startup_commands_json(3),
    /// color(4), icon(5), last_opened(6), is_favorite(7), project_type(8).
    /// When idOffset is provided (e.g. ListProjects includes id in col 0), all indices shift.
    /// </summary>
    private static Project MapProjectFromReader(string id, SqliteDataReader reader, int idOffset = 0)
    {
        return new Project
        {
            Id = id,
            Name = reader.GetString(0 + idOffset),
            Path = reader.GetString(1 + idOffset),
            DefaultShell = Enum.Parse<ShellType>(reader.GetString(2 + idOffset)),
            StartupCommands = JsonSerializer.Deserialize<List<string>>(
                reader.GetString(3 + idOffset), JsonOpts) ?? new(),
            Color = reader.GetString(4 + idOffset),
            Icon = reader.GetString(5 + idOffset),
            LastOpened = reader.IsDBNull(6 + idOffset)
                ? DateTime.MinValue
                : DateTime.Parse(reader.GetString(6 + idOffset), null,
                    System.Globalization.DateTimeStyles.RoundtripKind),
            IsFavorite = reader.GetInt32(7 + idOffset) != 0,
            ProjectType = Enum.Parse<ProjectType>(reader.GetString(8 + idOffset))
        };
    }
}
