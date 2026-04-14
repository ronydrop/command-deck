namespace CommandDeck.Models;

/// <summary>
/// Represents a Kanban board scoped to a workspace.
/// Each workspace has at most one board. Columns are persisted separately.
/// </summary>
public class KanbanBoard
{
    /// <summary>Unique identifier (hex GUID, no dashes).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Display name shown in the board header.</summary>
    public string Name { get; set; } = "Board";

    /// <summary>The workspace this board belongs to.</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>
    /// Default column set used when creating a new board.
    /// Populated from the DB on load; pre-populated with defaults at creation time.
    /// </summary>
    public List<KanbanColumn> Columns { get; set; } = new()
    {
        new() { Id = "backlog", Title = "Backlog",    SortOrder = 0 },
        new() { Id = "running", Title = "Executando", SortOrder = 1 },
        new() { Id = "review",  Title = "Revisão",    SortOrder = 2 },
        new() { Id = "done",    Title = "Concluído",  SortOrder = 3 }
    };

    /// <summary>UTC timestamp when the board was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A vertical column within a <see cref="KanbanBoard"/> (e.g. Backlog, In Progress).
/// </summary>
public class KanbanColumn
{
    /// <summary>Unique identifier. May be a semantic slug ("backlog") or a hex GUID.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The board this column belongs to.</summary>
    public string BoardId { get; set; } = string.Empty;

    /// <summary>Display title shown in the column header.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Zero-based left-to-right sort position.</summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// A task card within a Kanban column. Supports AI-agent metadata for
/// autonomous execution via MCP.
/// </summary>
public class KanbanCard
{
    /// <summary>Unique identifier (hex GUID, no dashes).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The board this card belongs to.</summary>
    public string BoardId { get; set; } = string.Empty;

    /// <summary>The column this card currently occupies.</summary>
    public string ColumnId { get; set; } = string.Empty;

    /// <summary>Short display title for the card.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Free-form description of the task.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Structured prompt / instructions forwarded to the AI agent when the card is launched.
    /// </summary>
    public string Instructions { get; set; } = string.Empty;

    /// <summary>Accent color in hex notation. Default: Catppuccin Blue (#89b4fa).</summary>
    public string Color { get; set; } = "#89b4fa";

    /// <summary>
    /// Target AI agent identifier: "claude", "codex", "aider", "gemini", or "copilot".
    /// </summary>
    public string Agent { get; set; } = "claude";

    /// <summary>Optional model override (e.g. "claude-opus-4-5"). Empty = agent default.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>True once the card has been dispatched to its agent session.</summary>
    public bool Launched { get; set; }

    /// <summary>Zero-based top-to-bottom sort position within the column.</summary>
    public int SortOrder { get; set; }

    /// <summary>UTC timestamp when the card was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last modification.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Paths or URIs of files attached to this card.
    /// Stored as a JSON array in the DB column FileRefs.
    /// </summary>
    public List<string> FileRefs { get; set; } = new();

    /// <summary>
    /// IDs of cards this card depends on.
    /// Stored as a JSON array in the DB column CardRefs.
    /// </summary>
    public List<string> CardRefs { get; set; } = new();

    /// <summary>
    /// Comments attached to this card. Loaded separately from the KanbanComments table
    /// and populated by <c>GetCardsForBoardAsync</c>.
    /// </summary>
    public List<KanbanComment> Comments { get; set; } = new();
}

/// <summary>
/// A comment or AI-agent output attached to a <see cref="KanbanCard"/>.
/// </summary>
public class KanbanComment
{
    /// <summary>Unique identifier (hex GUID, no dashes).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The card this comment belongs to.</summary>
    public string CardId { get; set; } = string.Empty;

    /// <summary>Comment body text (plain text or Markdown).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the comment was posted.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True when this comment was written by an AI agent via MCP; false for human input.</summary>
    public bool IsAgentOutput { get; set; }
}
