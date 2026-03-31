using Microsoft.Data.Sqlite;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Schema migrations for PersistenceService.
/// Each migration creates/alters tables to evolve the database schema.
/// Migrations are tracked in the schema_version table and only run once.
/// </summary>
public sealed partial class PersistenceService
{
    // ─── Migration Definitions ──────────────────────────────────────────────

    private static readonly (string Version, string Description, Func<SqliteConnection, Task> Up)[] Migrations =
    {
        ("1.0.0", "Initial schema: workspaces, projects, terminal_sessions, command_history, app_settings",
            Migrate_1_0_0),

        ("1.1.0", "Add multi-workspace fields: color, icon, is_active, settings_json, last_accessed_at",
            Migrate_1_1_0),
    };

    private static async Task Migrate_1_0_0(SqliteConnection conn)
    {
        await using var cmd = conn.CreateCommand();

        // ─── workspaces ──────────────────────────────────────────────────
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS workspaces (
                id          TEXT PRIMARY KEY,
                name        TEXT    NOT NULL DEFAULT 'Workspace',
                camera_json TEXT    NOT NULL DEFAULT '{}',
                items_json  TEXT    NOT NULL DEFAULT '[]',
                created_at  TEXT    NOT NULL DEFAULT (datetime('now')),
                updated_at  TEXT    NOT NULL DEFAULT (datetime('now'))
            );";
        await cmd.ExecuteNonQueryAsync();

        // ─── projects ────────────────────────────────────────────────────
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS projects (
                id                  TEXT PRIMARY KEY,
                name                TEXT    NOT NULL DEFAULT '',
                path                TEXT    NOT NULL DEFAULT '',
                default_shell       TEXT    NOT NULL DEFAULT 'WSL',
                startup_commands_json TEXT  NOT NULL DEFAULT '[]',
                color               TEXT    NOT NULL DEFAULT '#7C3AED',
                icon                TEXT    NOT NULL DEFAULT '',
                last_opened         TEXT,
                is_favorite         INTEGER NOT NULL DEFAULT 0,
                project_type        TEXT    NOT NULL DEFAULT 'Unknown',
                created_at          TEXT    NOT NULL DEFAULT (datetime('now')),
                updated_at          TEXT    NOT NULL DEFAULT (datetime('now'))
            );
            CREATE INDEX IF NOT EXISTS idx_projects_last_opened ON projects(last_opened);
            CREATE INDEX IF NOT EXISTS idx_projects_favorite   ON projects(is_favorite);";
        await cmd.ExecuteNonQueryAsync();

        // ─── terminal_sessions ───────────────────────────────────────────
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS terminal_sessions (
                id                 TEXT PRIMARY KEY,
                title              TEXT    NOT NULL DEFAULT 'Terminal',
                shell_type         TEXT    NOT NULL DEFAULT 'WSL',
                project_id         TEXT,
                working_directory  TEXT    NOT NULL DEFAULT '',
                session_state      TEXT    NOT NULL DEFAULT 'Starting',
                last_activity      TEXT    NOT NULL DEFAULT (datetime('now')),
                created_at         TEXT    NOT NULL DEFAULT (datetime('now')),
                closed_at          TEXT,
                error_code         INTEGER NOT NULL DEFAULT 0,
                error_message      TEXT    NOT NULL DEFAULT '',
                updated_at         TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE SET NULL
            );
            CREATE INDEX IF NOT EXISTS idx_sessions_project  ON terminal_sessions(project_id);
            CREATE INDEX IF NOT EXISTS idx_sessions_activity ON terminal_sessions(last_activity);";
        await cmd.ExecuteNonQueryAsync();

        // ─── command_history ─────────────────────────────────────────────
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS command_history (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id   TEXT    NOT NULL,
                command      TEXT    NOT NULL,
                executed_at  TEXT    NOT NULL,
                FOREIGN KEY (session_id) REFERENCES terminal_sessions(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_cmdhist_session ON command_history(session_id);
            CREATE INDEX IF NOT EXISTS idx_cmdhist_time    ON command_history(executed_at);";
        await cmd.ExecuteNonQueryAsync();

        // ─── app_settings ────────────────────────────────────────────────
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS app_settings (
                key         TEXT PRIMARY KEY,
                value       TEXT    NOT NULL DEFAULT '',
                updated_at  TEXT    NOT NULL DEFAULT (datetime('now'))
            );";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task Migrate_1_1_0(SqliteConnection conn)
    {
        await using var cmd = conn.CreateCommand();

        // Add new columns to workspaces table for multi-workspace support
        cmd.CommandText = @"
            ALTER TABLE workspaces ADD COLUMN color           TEXT NOT NULL DEFAULT '#CBA6F7';
            ALTER TABLE workspaces ADD COLUMN icon            TEXT NOT NULL DEFAULT 'FolderIcon';
            ALTER TABLE workspaces ADD COLUMN is_active       INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE workspaces ADD COLUMN settings_json   TEXT NOT NULL DEFAULT '{}';
            ALTER TABLE workspaces ADD COLUMN last_accessed_at TEXT NOT NULL DEFAULT (datetime('now'));";
        await cmd.ExecuteNonQueryAsync();

        // Mark the first workspace as active (migration from single to multi-workspace)
        cmd.CommandText = @"
            UPDATE workspaces SET is_active = 1
            WHERE id = (SELECT id FROM workspaces ORDER BY updated_at DESC LIMIT 1);";
        await cmd.ExecuteNonQueryAsync();

        // Index for quick lookup of the active workspace
        cmd.CommandText = @"
            CREATE INDEX IF NOT EXISTS idx_workspaces_active ON workspaces(is_active);";
        await cmd.ExecuteNonQueryAsync();
    }

    // ─── Migration Runner ───────────────────────────────────────────────────

    private async Task RunMigrationsAsync()
    {
        // Get the last applied version
        string? lastVersion = null;

        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT version FROM schema_version ORDER BY applied_at DESC LIMIT 1;";
            var result = await cmd.ExecuteScalarAsync();
            lastVersion = result as string;
        }

        // Determine which migrations need to run
        var startIndex = 0;
        if (lastVersion is not null)
        {
            for (int i = 0; i < Migrations.Length; i++)
            {
                if (Migrations[i].Version == lastVersion)
                {
                    startIndex = i + 1;
                    break;
                }
            }
        }

        // Run pending migrations in order
        for (int i = startIndex; i < Migrations.Length; i++)
        {
            var (version, description, migrateFn) = Migrations[i];

            await migrateFn(_connection!);

            // Record the migration
            await using var recordCmd = _connection!.CreateCommand();
            recordCmd.CommandText = @"
                INSERT INTO schema_version (version, description, applied_at)
                VALUES ($version, $desc, datetime('now'));";
            recordCmd.Parameters.AddWithValue("$version", version);
            recordCmd.Parameters.AddWithValue("$desc", description);
            await recordCmd.ExecuteNonQueryAsync();

            System.Diagnostics.Debug.WriteLine(
                $"[PersistenceService] Migration {version} applied: {description}");
        }
    }
}
