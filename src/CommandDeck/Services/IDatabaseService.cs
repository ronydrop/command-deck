using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Provides SQLite-backed persistence for command history, assistant preferences,
/// and extended node metadata. Runs alongside the existing JSON persistence — zero impact.
/// </summary>
public interface IDatabaseService
{
    /// <summary>Ensures the database file and all tables exist.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    // ── Command History ───────────────────────────────────────────────────

    /// <summary>Returns the most recent commands for a terminal session.</summary>
    Task<IReadOnlyList<CommandHistoryEntry>> GetCommandHistoryAsync(
        string sessionId,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>Persists a new command execution record.</summary>
    Task AddCommandHistoryAsync(
        string sessionId,
        string command,
        bool success = true,
        CancellationToken ct = default);

    // ── Assistant Preferences ─────────────────────────────────────────────

    /// <summary>Saves (upserts) the active AI provider configuration.</summary>
    Task SaveAssistantPreferencesAsync(
        string providerName,
        string model,
        string? apiKey = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the stored AI provider configuration, or null if none saved.
    /// </summary>
    Task<(string providerName, string model, string? apiKey)?> GetAssistantPreferencesAsync(
        CancellationToken ct = default);

    // ── Node Metadata ─────────────────────────────────────────────────────

    /// <summary>Upserts a key/value metadata pair for a workspace node.</summary>
    Task SetNodeMetadataAsync(
        string nodeId,
        string key,
        string value,
        CancellationToken ct = default);

    /// <summary>Returns the stored metadata value, or null if not found.</summary>
    Task<string?> GetNodeMetadataAsync(
        string nodeId,
        string key,
        CancellationToken ct = default);

    // ── Chat History ─────────────────────────────────────────────────────

    /// <summary>Persists a chat message to a conversation.</summary>
    Task SaveChatMessageAsync(
        string conversationId, string role, string content,
        string? model = null, string? provider = null,
        CancellationToken ct = default);

    /// <summary>Returns messages for a conversation, ordered by timestamp.</summary>
    Task<IReadOnlyList<ChatMessageRecord>> GetChatMessagesAsync(
        string conversationId, int limit = 200, CancellationToken ct = default);

    /// <summary>Returns the most recent conversation ID, or null if none.</summary>
    Task<string?> GetLastConversationIdAsync(CancellationToken ct = default);

    /// <summary>Deletes all messages for a conversation.</summary>
    Task DeleteConversationAsync(string conversationId, CancellationToken ct = default);
}
