using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// SQLite-backed implementation of <see cref="IKanbanService"/>.
/// Uses the same database file as <see cref="DatabaseService"/>
/// (<c>%APPDATA%\CommandDeck\devworkspace.db</c>) but manages its own
/// connection lifetime and semaphore — no dependency on <c>IDatabaseService</c>.
///
/// Tables are created lazily on first use via <c>EnsureTablesAsync</c>.
/// All public methods are thread-safe via <see cref="SemaphoreSlim"/>(1,1).
/// </summary>
public sealed class KanbanService : IKanbanService
{
    // ── Infrastructure ────────────────────────────────────────────────────────

    private readonly string _connectionString;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <inheritdoc/>
    public event Action<KanbanCard>? CardUpdated;

    /// <inheritdoc/>
    public event Action<string>? CardDeleted;

    public KanbanService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "CommandDeck");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "devworkspace.db");
        _connectionString = $"Data Source={dbPath}";
    }

    // ── Schema ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates all Kanban tables when they do not yet exist.
    /// Must be called inside the semaphore with an open connection.
    /// </summary>
    private async Task EnsureTablesAsync(SqliteConnection conn, CancellationToken ct)
    {
        if (_initialized) return;

        await ExecuteNonQueryAsync(conn, ct, @"
            CREATE TABLE IF NOT EXISTS KanbanBoards (
                Id          TEXT PRIMARY KEY NOT NULL,
                Name        TEXT NOT NULL,
                WorkspaceId TEXT NOT NULL,
                CreatedAt   TEXT NOT NULL
            );");

        await ExecuteNonQueryAsync(conn, ct, @"
            CREATE INDEX IF NOT EXISTS IX_KanbanBoards_WorkspaceId
            ON KanbanBoards(WorkspaceId);");

        await ExecuteNonQueryAsync(conn, ct, @"
            CREATE TABLE IF NOT EXISTS KanbanColumns (
                Id        TEXT PRIMARY KEY NOT NULL,
                BoardId   TEXT NOT NULL,
                Title     TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );");

        await ExecuteNonQueryAsync(conn, ct, @"
            CREATE INDEX IF NOT EXISTS IX_KanbanColumns_BoardId
            ON KanbanColumns(BoardId);");

        await ExecuteNonQueryAsync(conn, ct, @"
            CREATE TABLE IF NOT EXISTS KanbanCards (
                Id           TEXT PRIMARY KEY NOT NULL,
                BoardId      TEXT NOT NULL,
                ColumnId     TEXT NOT NULL,
                Title        TEXT NOT NULL,
                Description  TEXT NOT NULL DEFAULT '',
                Instructions TEXT NOT NULL DEFAULT '',
                Color        TEXT NOT NULL DEFAULT '#89b4fa',
                Agent        TEXT NOT NULL DEFAULT 'claude',
                Model        TEXT NOT NULL DEFAULT '',
                Launched     INTEGER NOT NULL DEFAULT 0,
                SortOrder    INTEGER NOT NULL DEFAULT 0,
                CreatedAt    TEXT NOT NULL,
                UpdatedAt    TEXT NOT NULL,
                FileRefs     TEXT NOT NULL DEFAULT '[]',
                CardRefs     TEXT NOT NULL DEFAULT '[]'
            );");

        await ExecuteNonQueryAsync(conn, ct, @"
            CREATE INDEX IF NOT EXISTS IX_KanbanCards_BoardId
            ON KanbanCards(BoardId);");

        await ExecuteNonQueryAsync(conn, ct, @"
            CREATE INDEX IF NOT EXISTS IX_KanbanCards_ColumnId
            ON KanbanCards(ColumnId);");

        await ExecuteNonQueryAsync(conn, ct, @"
            CREATE TABLE IF NOT EXISTS KanbanComments (
                Id            TEXT PRIMARY KEY NOT NULL,
                CardId        TEXT NOT NULL,
                Text          TEXT NOT NULL,
                CreatedAt     TEXT NOT NULL,
                IsAgentOutput INTEGER NOT NULL DEFAULT 0
            );");

        await ExecuteNonQueryAsync(conn, ct, @"
            CREATE INDEX IF NOT EXISTS IX_KanbanComments_CardId
            ON KanbanComments(CardId);");

        _initialized = true;
    }

    // ── Board ─────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<KanbanBoard?> GetBoardForWorkspaceAsync(
        string workspaceId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await EnsureTablesAsync(conn, ct).ConfigureAwait(false);

            // Load the board row
            KanbanBoard? board = null;

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Id, Name, WorkspaceId, CreatedAt
                    FROM KanbanBoards
                    WHERE WorkspaceId = @workspaceId
                    LIMIT 1;";
                cmd.Parameters.AddWithValue("@workspaceId", workspaceId);

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    board = new KanbanBoard
                    {
                        Id          = reader.GetString(0),
                        Name        = reader.GetString(1),
                        WorkspaceId = reader.GetString(2),
                        CreatedAt   = DateTime.Parse(reader.GetString(3)),
                        Columns     = new List<KanbanColumn>()
                    };
                }
            }

            if (board is null) return null;

            // Load columns for this board
            board.Columns = await LoadColumnsAsync(conn, board.Id, ct).ConfigureAwait(false);
            return board;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<KanbanBoard> CreateBoardAsync(
        string workspaceId, string name = "Board", CancellationToken ct = default)
    {
        var board = new KanbanBoard
        {
            Id          = Guid.NewGuid().ToString("N"),
            Name        = name,
            WorkspaceId = workspaceId,
            CreatedAt   = DateTime.UtcNow
        };

        // Assign board ID and sequential sort order to each default column
        for (int i = 0; i < board.Columns.Count; i++)
        {
            board.Columns[i].BoardId   = board.Id;
            board.Columns[i].SortOrder = i;
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await EnsureTablesAsync(conn, ct).ConfigureAwait(false);

            // Insert board
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO KanbanBoards (Id, Name, WorkspaceId, CreatedAt)
                    VALUES (@id, @name, @workspaceId, @createdAt);";
                cmd.Parameters.AddWithValue("@id",          board.Id);
                cmd.Parameters.AddWithValue("@name",        board.Name);
                cmd.Parameters.AddWithValue("@workspaceId", board.WorkspaceId);
                cmd.Parameters.AddWithValue("@createdAt",   board.CreatedAt.ToString("O"));
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // Insert each default column
            foreach (var col in board.Columns)
            {
                await InsertColumnAsync(conn, col, ct).ConfigureAwait(false);
            }

            return board;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Cards ─────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<KanbanCard> CreateCardAsync(KanbanCard card, CancellationToken ct = default)
    {
        card.CreatedAt = DateTime.UtcNow;
        card.UpdatedAt = DateTime.UtcNow;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await EnsureTablesAsync(conn, ct).ConfigureAwait(false);

            await InsertCardAsync(conn, card, ct).ConfigureAwait(false);
            return card;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task UpdateCardAsync(KanbanCard card, CancellationToken ct = default)
    {
        card.UpdatedAt = DateTime.UtcNow;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await EnsureTablesAsync(conn, ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE KanbanCards
                SET Title        = @title,
                    Description  = @description,
                    Instructions = @instructions,
                    Color        = @color,
                    Agent        = @agent,
                    Model        = @model,
                    Launched     = @launched,
                    ColumnId     = @columnId,
                    SortOrder    = @sortOrder,
                    UpdatedAt    = @updatedAt,
                    FileRefs     = @fileRefs,
                    CardRefs     = @cardRefs
                WHERE Id = @id;";
            BindCardParameters(cmd, card);
            cmd.Parameters.AddWithValue("@id", card.Id);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }

        CardUpdated?.Invoke(card);
    }

    /// <inheritdoc/>
    public async Task MoveCardAsync(string cardId, string columnId, CancellationToken ct = default)
    {
        KanbanCard? updated = null;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await EnsureTablesAsync(conn, ct).ConfigureAwait(false);

            var updatedAt = DateTime.UtcNow.ToString("O");

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    UPDATE KanbanCards
                    SET ColumnId  = @columnId,
                        UpdatedAt = @updatedAt
                    WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@columnId",  columnId);
                cmd.Parameters.AddWithValue("@updatedAt", updatedAt);
                cmd.Parameters.AddWithValue("@id",        cardId);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            updated = await LoadSingleCardAsync(conn, cardId, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }

        if (updated is not null)
            CardUpdated?.Invoke(updated);
    }

    /// <inheritdoc/>
    public async Task DeleteCardAsync(string cardId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await EnsureTablesAsync(conn, ct).ConfigureAwait(false);

            // Delete comments first (no FK cascade in SQLite without PRAGMA foreign_keys)
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM KanbanComments WHERE CardId = @cardId;";
                cmd.Parameters.AddWithValue("@cardId", cardId);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM KanbanCards WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", cardId);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }

        CardDeleted?.Invoke(cardId);
    }

    /// <inheritdoc/>
    public async Task<List<KanbanCard>> GetCardsForBoardAsync(
        string boardId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await EnsureTablesAsync(conn, ct).ConfigureAwait(false);

            var cards = await LoadCardsForBoardAsync(conn, boardId, ct).ConfigureAwait(false);
            await PopulateCommentsAsync(conn, cards, ct).ConfigureAwait(false);
            return cards;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Comments ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task AddCommentAsync(KanbanComment comment, CancellationToken ct = default)
    {
        comment.CreatedAt = DateTime.UtcNow;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await EnsureTablesAsync(conn, ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO KanbanComments (Id, CardId, Text, CreatedAt, IsAgentOutput)
                VALUES (@id, @cardId, @text, @createdAt, @isAgentOutput);";
            cmd.Parameters.AddWithValue("@id",            comment.Id);
            cmd.Parameters.AddWithValue("@cardId",        comment.CardId);
            cmd.Parameters.AddWithValue("@text",          comment.Text);
            cmd.Parameters.AddWithValue("@createdAt",     comment.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@isAgentOutput", comment.IsAgentOutput ? 1 : 0);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task InsertColumnAsync(
        SqliteConnection conn, KanbanColumn col, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO KanbanColumns (Id, BoardId, Title, SortOrder)
            VALUES (@id, @boardId, @title, @sortOrder);";
        cmd.Parameters.AddWithValue("@id",        col.Id);
        cmd.Parameters.AddWithValue("@boardId",   col.BoardId);
        cmd.Parameters.AddWithValue("@title",     col.Title);
        cmd.Parameters.AddWithValue("@sortOrder", col.SortOrder);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertCardAsync(
        SqliteConnection conn, KanbanCard card, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO KanbanCards
                (Id, BoardId, ColumnId, Title, Description, Instructions,
                 Color, Agent, Model, Launched, SortOrder,
                 CreatedAt, UpdatedAt, FileRefs, CardRefs)
            VALUES
                (@id, @boardId, @columnId, @title, @description, @instructions,
                 @color, @agent, @model, @launched, @sortOrder,
                 @createdAt, @updatedAt, @fileRefs, @cardRefs);";
        cmd.Parameters.AddWithValue("@id",           card.Id);
        cmd.Parameters.AddWithValue("@boardId",      card.BoardId);
        BindCardParameters(cmd, card);
        cmd.Parameters.AddWithValue("@createdAt",    card.CreatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Binds the mutable card parameters shared by INSERT and UPDATE statements.
    /// Does NOT bind @id or @createdAt (immutable after creation).
    /// </summary>
    private static void BindCardParameters(SqliteCommand cmd, KanbanCard card)
    {
        cmd.Parameters.AddWithValue("@columnId",     card.ColumnId);
        cmd.Parameters.AddWithValue("@title",        card.Title);
        cmd.Parameters.AddWithValue("@description",  card.Description);
        cmd.Parameters.AddWithValue("@instructions", card.Instructions);
        cmd.Parameters.AddWithValue("@color",        card.Color);
        cmd.Parameters.AddWithValue("@agent",        card.Agent);
        cmd.Parameters.AddWithValue("@model",        card.Model);
        cmd.Parameters.AddWithValue("@launched",     card.Launched ? 1 : 0);
        cmd.Parameters.AddWithValue("@sortOrder",    card.SortOrder);
        cmd.Parameters.AddWithValue("@updatedAt",    card.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@fileRefs",     SerializeJson(card.FileRefs));
        cmd.Parameters.AddWithValue("@cardRefs",     SerializeJson(card.CardRefs));
    }

    private static async Task<List<KanbanColumn>> LoadColumnsAsync(
        SqliteConnection conn, string boardId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, BoardId, Title, SortOrder
            FROM KanbanColumns
            WHERE BoardId = @boardId
            ORDER BY SortOrder ASC;";
        cmd.Parameters.AddWithValue("@boardId", boardId);

        var columns = new List<KanbanColumn>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            columns.Add(new KanbanColumn
            {
                Id        = reader.GetString(0),
                BoardId   = reader.GetString(1),
                Title     = reader.GetString(2),
                SortOrder = reader.GetInt32(3)
            });
        }

        return columns;
    }

    private static async Task<List<KanbanCard>> LoadCardsForBoardAsync(
        SqliteConnection conn, string boardId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, BoardId, ColumnId, Title, Description, Instructions,
                   Color, Agent, Model, Launched, SortOrder,
                   CreatedAt, UpdatedAt, FileRefs, CardRefs
            FROM KanbanCards
            WHERE BoardId = @boardId
            ORDER BY ColumnId ASC, SortOrder ASC;";
        cmd.Parameters.AddWithValue("@boardId", boardId);

        var cards = new List<KanbanCard>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            cards.Add(ReadCard(reader));
        }

        return cards;
    }

    private static async Task<KanbanCard?> LoadSingleCardAsync(
        SqliteConnection conn, string cardId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, BoardId, ColumnId, Title, Description, Instructions,
                   Color, Agent, Model, Launched, SortOrder,
                   CreatedAt, UpdatedAt, FileRefs, CardRefs
            FROM KanbanCards
            WHERE Id = @id
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", cardId);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;

        return ReadCard(reader);
    }

    private static KanbanCard ReadCard(SqliteDataReader reader)
    {
        return new KanbanCard
        {
            Id           = reader.GetString(0),
            BoardId      = reader.GetString(1),
            ColumnId     = reader.GetString(2),
            Title        = reader.GetString(3),
            Description  = reader.GetString(4),
            Instructions = reader.GetString(5),
            Color        = reader.GetString(6),
            Agent        = reader.GetString(7),
            Model        = reader.GetString(8),
            Launched     = reader.GetInt32(9) != 0,
            SortOrder    = reader.GetInt32(10),
            CreatedAt    = DateTime.Parse(reader.GetString(11)),
            UpdatedAt    = DateTime.Parse(reader.GetString(12)),
            FileRefs     = DeserializeJson<List<string>>(reader.GetString(13)) ?? new(),
            CardRefs     = DeserializeJson<List<string>>(reader.GetString(14)) ?? new(),
            Comments     = new List<KanbanComment>()
        };
    }

    private static async Task PopulateCommentsAsync(
        SqliteConnection conn, List<KanbanCard> cards, CancellationToken ct)
    {
        if (cards.Count == 0) return;

        // Build an index for O(1) lookup when assigning comments to cards
        var index = new Dictionary<string, KanbanCard>(cards.Count);
        foreach (var c in cards) index[c.Id] = c;

        // Parameterised IN clause: bind each card ID individually
        await using var cmd = conn.CreateCommand();
        var placeholders = new System.Text.StringBuilder();
        for (int i = 0; i < cards.Count; i++)
        {
            var p = $"@cid{i}";
            placeholders.Append(i == 0 ? p : $",{p}");
            cmd.Parameters.AddWithValue(p, cards[i].Id);
        }

        cmd.CommandText = $@"
            SELECT Id, CardId, Text, CreatedAt, IsAgentOutput
            FROM KanbanComments
            WHERE CardId IN ({placeholders})
            ORDER BY CreatedAt ASC;";

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var comment = new KanbanComment
            {
                Id            = reader.GetString(0),
                CardId        = reader.GetString(1),
                Text          = reader.GetString(2),
                CreatedAt     = DateTime.Parse(reader.GetString(3)),
                IsAgentOutput = reader.GetInt32(4) != 0
            };

            if (index.TryGetValue(comment.CardId, out var card))
                card.Comments.Add(comment);
        }
    }

    private static string SerializeJson<T>(T value) =>
        JsonSerializer.Serialize(value, _jsonOptions);

    private static T? DeserializeJson<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, _jsonOptions); }
        catch { return default; }
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection conn, CancellationToken ct, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
