using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Unified persistence interface backed by SQLite.
/// Replaces direct JSON file I/O with a single, thread-safe database.
/// </summary>
public interface IPersistenceService : IDisposable
{
    // ─── Lifecycle ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the database (if it doesn't exist) and runs any pending migrations.
    /// Must be called once during application startup before any other operations.
    /// </summary>
    Task InitAsync();

    // ─── Workspaces ─────────────────────────────────────────────────────────

    /// <summary>Upserts a workspace (camera state + canvas items).</summary>
    Task SaveWorkspaceAsync(WorkspaceModel workspace);

    /// <summary>Returns a workspace by id, or null if not found.</summary>
    Task<WorkspaceModel?> LoadWorkspaceAsync(string id);

    /// <summary>Returns all persisted workspaces.</summary>
    Task<IReadOnlyList<WorkspaceModel>> ListWorkspacesAsync();

    /// <summary>Deletes a workspace by id. Returns true if deleted.</summary>
    Task<bool> DeleteWorkspaceAsync(string id);

    /// <summary>Returns the currently active workspace, or null if none.</summary>
    Task<WorkspaceModel?> GetActiveWorkspaceAsync();

    /// <summary>Sets a workspace as active (deactivating all others).</summary>
    Task SetActiveWorkspaceAsync(string id);

    // ─── Projects ───────────────────────────────────────────────────────────

    /// <summary>Upserts a project.</summary>
    Task SaveProjectAsync(Project project);

    /// <summary>Returns a project by id, or null if not found.</summary>
    Task<Project?> LoadProjectAsync(string id);

    /// <summary>Returns all projects, optionally ordered by most-recently-opened.</summary>
    Task<IReadOnlyList<Project>> ListProjectsAsync(bool orderByLastOpened = false);

    /// <summary>Deletes a project by id. Returns true if deleted.</summary>
    Task<bool> DeleteProjectAsync(string id);

    // ─── Terminal Sessions ──────────────────────────────────────────────────

    /// <summary>Upserts a terminal session and its command history.</summary>
    Task SaveTerminalSessionAsync(TerminalSessionModel session);

    /// <summary>Returns a terminal session by id, or null if not found.</summary>
    Task<TerminalSessionModel?> LoadTerminalSessionAsync(string id);

    /// <summary>Returns all sessions associated with a workspace (via canvas items).</summary>
    Task<IReadOnlyList<TerminalSessionModel>> ListSessionsByWorkspaceAsync(string workspaceId);

    /// <summary>Deletes sessions older than <paramref name="maxAge"/>. Returns count deleted.</summary>
    Task<int> DeleteOldSessionsAsync(TimeSpan maxAge);

    // ─── App Settings (Key-Value) ──────────────────────────────────────────

    /// <summary>Sets a single setting key. Value is serialized as JSON string.</summary>
    Task SaveSettingAsync(string key, object value);

    /// <summary>Gets a single setting value, deserialized to <typeparamref name="T"/>.</summary>
    Task<T?> LoadSettingAsync<T>(string key);

    /// <summary>Gets the raw string value for a setting key, or null.</summary>
    Task<string?> LoadSettingRawAsync(string key);

    /// <summary>Returns all settings as key-value pairs.</summary>
    Task<IReadOnlyDictionary<string, string>> LoadAllSettingsAsync();

    // ─── Command History ───────────────────────────────────────────────────

    /// <summary>Appends a command entry for a session.</summary>
    Task SaveCommandHistoryEntryAsync(string sessionId, string command, DateTime executedAt);

    /// <summary>Returns command history for a session, most-recent first.</summary>
    Task<IReadOnlyList<(string Command, DateTime ExecutedAt)>> LoadCommandHistoryAsync(
        string sessionId, int limit = 500);

    /// <summary>Deletes all command history for a session.</summary>
    Task DeleteCommandHistoryAsync(string sessionId);

    // ─── Transactions ──────────────────────────────────────────────────────

    /// <summary>Begins a transaction. Call <see cref="CommitAsync"/> or <see cref="RollbackAsync"/>.</summary>
    Task BeginTransactionAsync();

    /// <summary>Commits the current transaction.</summary>
    Task CommitAsync();

    /// <summary>Rolls back the current transaction.</summary>
    Task RollbackAsync();

    // ─── Maintenance ───────────────────────────────────────────────────────

    /// <summary>Runs VACUUM to reclaim disk space. Call during idle periods.</summary>
    Task VacuumAsync();
}
