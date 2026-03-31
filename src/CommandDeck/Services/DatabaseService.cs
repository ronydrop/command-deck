using System.IO;
using Microsoft.Data.Sqlite;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// SQLite-backed persistence service.
/// Database file: %APPDATA%\CommandDeck\devworkspace.db
///
/// Uses one SqliteConnection per operation (opened/closed with using).
/// Thread-safe via SemaphoreSlim(1,1).
/// No ORM -- plain parameterised SQL.
/// </summary>
public sealed class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DatabaseService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "CommandDeck");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "devworkspace.db");
        _connectionString = $"Data Source={dbPath}";
    }

    // ── Initialization ────────────────────────────────────────────────────

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await ExecuteNonQueryAsync(conn, ct, @"
                CREATE TABLE IF NOT EXISTS CommandHistory (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    SessionId   TEXT    NOT NULL,
                    Command     TEXT    NOT NULL,
                    ExecutedAt  TEXT    NOT NULL,
                    Success     INTEGER NOT NULL DEFAULT 1
                );");

            await ExecuteNonQueryAsync(conn, ct, @"
                CREATE TABLE IF NOT EXISTS AssistantPreferences (
                    Id           INTEGER PRIMARY KEY CHECK (Id = 1),
                    ProviderName TEXT    NOT NULL,
                    Model        TEXT    NOT NULL,
                    ApiKey       TEXT
                );");

            await ExecuteNonQueryAsync(conn, ct, @"
                CREATE TABLE IF NOT EXISTS NodeMetadata (
                    NodeId TEXT NOT NULL,
                    Key    TEXT NOT NULL,
                    Value  TEXT NOT NULL,
                    PRIMARY KEY (NodeId, Key)
                );");
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Command History ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<CommandHistoryEntry>> GetCommandHistoryAsync(
        string sessionId,
        int limit = 50,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, SessionId, Command, ExecutedAt, Success
                FROM CommandHistory
                WHERE SessionId = @sessionId
                ORDER BY ExecutedAt DESC
                LIMIT @limit;";
            cmd.Parameters.AddWithValue("@sessionId", sessionId);
            cmd.Parameters.AddWithValue("@limit", limit);

            var results = new List<CommandHistoryEntry>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(new CommandHistoryEntry
                {
                    Id         = reader.GetInt64(0),
                    SessionId  = reader.GetString(1),
                    Command    = reader.GetString(2),
                    ExecutedAt = DateTime.Parse(reader.GetString(3)),
                    Success    = reader.GetInt32(4) != 0
                });
            }

            return results.AsReadOnly();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddCommandHistoryAsync(
        string sessionId,
        string command,
        bool success = true,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO CommandHistory (SessionId, Command, ExecutedAt, Success)
                VALUES (@sessionId, @command, @executedAt, @success);";
            cmd.Parameters.AddWithValue("@sessionId", sessionId);
            cmd.Parameters.AddWithValue("@command", command);
            cmd.Parameters.AddWithValue("@executedAt", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@success", success ? 1 : 0);

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Assistant Preferences ─────────────────────────────────────────────

    public async Task SaveAssistantPreferencesAsync(
        string providerName,
        string model,
        string? apiKey = null,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO AssistantPreferences (Id, ProviderName, Model, ApiKey)
                VALUES (1, @providerName, @model, @apiKey);";
            cmd.Parameters.AddWithValue("@providerName", providerName);
            cmd.Parameters.AddWithValue("@model", model);
            cmd.Parameters.AddWithValue("@apiKey", (object?)apiKey ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<(string providerName, string model, string? apiKey)?> GetAssistantPreferencesAsync(
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ProviderName, Model, ApiKey
                FROM AssistantPreferences
                WHERE Id = 1;";

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return null;

            var providerName = reader.GetString(0);
            var model        = reader.GetString(1);
            var apiKey       = reader.IsDBNull(2) ? null : reader.GetString(2);

            return (providerName, model, apiKey);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Node Metadata ─────────────────────────────────────────────────────

    public async Task SetNodeMetadataAsync(
        string nodeId,
        string key,
        string value,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO NodeMetadata (NodeId, Key, Value)
                VALUES (@nodeId, @key, @value);";
            cmd.Parameters.AddWithValue("@nodeId", nodeId);
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> GetNodeMetadataAsync(
        string nodeId,
        string key,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Value
                FROM NodeMetadata
                WHERE NodeId = @nodeId AND Key = @key;";
            cmd.Parameters.AddWithValue("@nodeId", nodeId);
            cmd.Parameters.AddWithValue("@key", key);

            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return result is DBNull or null ? null : (string)result;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection conn,
        CancellationToken ct,
        string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
