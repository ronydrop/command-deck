using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Provides Kanban board persistence and in-process change notifications.
/// Each workspace owns at most one board; columns and cards are scoped to that board.
/// </summary>
public interface IKanbanService
{
    // ── Board ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the board associated with <paramref name="workspaceId"/>,
    /// or <c>null</c> if none has been created yet.
    /// </summary>
    Task<KanbanBoard?> GetBoardForWorkspaceAsync(string workspaceId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new board (with its four default columns) for the given workspace
    /// and persists it to the database.
    /// </summary>
    Task<KanbanBoard> CreateBoardAsync(string workspaceId, string name = "Board", CancellationToken ct = default);

    // ── Cards ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a new card. <c>card.Id</c>, <c>card.BoardId</c>, and <c>card.ColumnId</c>
    /// must be set before calling.
    /// </summary>
    Task<KanbanCard> CreateCardAsync(KanbanCard card, CancellationToken ct = default);

    /// <summary>
    /// Replaces all mutable fields of an existing card and fires <see cref="CardUpdated"/>.
    /// </summary>
    Task UpdateCardAsync(KanbanCard card, CancellationToken ct = default);

    /// <summary>
    /// Moves a card to a different column and fires <see cref="CardUpdated"/>.
    /// </summary>
    Task MoveCardAsync(string cardId, string columnId, CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes a card and all its comments, then fires <see cref="CardDeleted"/>
    /// with the deleted card's ID.
    /// </summary>
    Task DeleteCardAsync(string cardId, CancellationToken ct = default);

    /// <summary>
    /// Returns all cards for the given board, each with its <see cref="KanbanCard.Comments"/>
    /// populated, ordered by column then by <see cref="KanbanCard.SortOrder"/>.
    /// </summary>
    Task<List<KanbanCard>> GetCardsForBoardAsync(string boardId, CancellationToken ct = default);

    // ── Comments ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends a comment to the card identified by <c>comment.CardId</c>.
    /// </summary>
    Task AddCommentAsync(KanbanComment comment, CancellationToken ct = default);

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised after a card is updated (via <see cref="UpdateCardAsync"/> or
    /// <see cref="MoveCardAsync"/>). Provides the refreshed card.
    /// </summary>
    event Action<KanbanCard>? CardUpdated;

    /// <summary>
    /// Raised after a card is deleted. Provides the deleted card's ID.
    /// </summary>
    event Action<string>? CardDeleted;
}
