using System;
using System.Text.Json;
using CommandDeck.Models;
using CommandDeck.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CommandDeck.Helpers;

/// <summary>
/// Registers Kanban-related AI tools in the <see cref="IToolRegistry"/> so that the
/// assistant can create, list, move, and update cards via tool calls.
/// </summary>
public static class KanbanToolsRegistrar
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static void RegisterAll(IServiceProvider services)
    {
        var registry  = services.GetRequiredService<IToolRegistry>();
        var kanban    = services.GetRequiredService<IKanbanService>();
        var lifecycle = services.GetRequiredService<IWorkspaceLifecycleService>();

        // ── kanban_create_card ────────────────────────────────────────────────
        registry.Register(
            new ToolDefinition
            {
                Name = "kanban_create_card",
                Description = "Cria um novo card (tarefa) no quadro Kanban do workspace atual. " +
                              "Use quando o usuário pedir para criar, adicionar ou registrar uma tarefa.",
                InputSchema = JsonDocument.Parse("""
                {
                  "type": "object",
                  "required": ["title"],
                  "properties": {
                    "title":        { "type": "string",  "description": "Título curto da tarefa." },
                    "description":  { "type": "string",  "description": "Descrição detalhada da tarefa (opcional)." },
                    "instructions": { "type": "string",  "description": "Prompt/brief que o agente IA receberá ao executar a tarefa (opcional)." },
                    "column":       { "type": "string",  "description": "ID da coluna destino. Use 'backlog' (padrão), 'running', 'review' ou 'done'." },
                    "agent":        { "type": "string",  "description": "Agente IA para execução: 'claude' (padrão), 'codex', 'aider', 'gemini', 'copilot'." },
                    "color":        { "type": "string",  "description": "Cor hexadecimal do card (ex: '#89b4fa'). Opcional." }
                  }
                }
                """).RootElement
            },
            async (input, ct) =>
            {
                var workspaceId = lifecycle.CurrentWorkspace?.Id ?? "default";
                var board = await kanban.GetBoardForWorkspaceAsync(workspaceId, ct)
                         ?? await kanban.CreateBoardAsync(workspaceId, "Board", ct);

                var title       = input.TryGetString("title")        ?? "Nova tarefa";
                var description = input.TryGetString("description")  ?? string.Empty;
                var instructions= input.TryGetString("instructions") ?? string.Empty;
                var column      = input.TryGetString("column")       ?? "backlog";
                var agent       = input.TryGetString("agent")        ?? "claude";
                var color       = input.TryGetString("color")        ?? "#89b4fa";

                // Validate column exists in the board
                var validColumn = board.Columns.Any(c => c.Id.Equals(column, StringComparison.OrdinalIgnoreCase))
                    ? column
                    : "backlog";

                var card = new KanbanCard
                {
                    BoardId      = board.Id,
                    ColumnId     = validColumn,
                    Title        = title,
                    Description  = description,
                    Instructions = instructions,
                    Agent        = agent,
                    Color        = color
                };

                await kanban.CreateCardAsync(card, ct);
                // Workaround: fire CardUpdated so open Kanban widgets pick up the new card
                await kanban.UpdateCardAsync(card, ct);

                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    card_id = card.Id,
                    title = card.Title,
                    column = card.ColumnId,
                    board_id = board.Id,
                    message = $"Card '{card.Title}' criado na coluna '{card.ColumnId}'."
                }, _json);
            });

        // ── kanban_list_cards ─────────────────────────────────────────────────
        registry.Register(
            new ToolDefinition
            {
                Name = "kanban_list_cards",
                Description = "Lista os cards (tarefas) do quadro Kanban do workspace atual. " +
                              "Use para consultar o estado atual das tarefas antes de criar ou mover.",
                InputSchema = JsonDocument.Parse("""
                {
                  "type": "object",
                  "properties": {
                    "column": { "type": "string", "description": "Filtrar por coluna: 'backlog', 'running', 'review', 'done'. Omitir para listar todos." }
                  }
                }
                """).RootElement
            },
            async (input, ct) =>
            {
                var workspaceId = lifecycle.CurrentWorkspace?.Id ?? "default";
                var board = await kanban.GetBoardForWorkspaceAsync(workspaceId, ct);
                if (board is null)
                    return JsonSerializer.Serialize(new { ok = true, cards = Array.Empty<object>(), message = "Nenhum board encontrado para este workspace." }, _json);

                var allCards = await kanban.GetCardsForBoardAsync(board.Id, ct);
                var filterCol = input.TryGetString("column");

                var filtered = string.IsNullOrWhiteSpace(filterCol)
                    ? allCards
                    : allCards.Where(c => c.ColumnId.Equals(filterCol, StringComparison.OrdinalIgnoreCase)).ToList();

                var result = filtered.Select(c => new
                {
                    id          = c.Id,
                    title       = c.Title,
                    column      = c.ColumnId,
                    description = c.Description,
                    agent       = c.Agent,
                    launched    = c.Launched,
                    created_at  = c.CreatedAt.ToString("yyyy-MM-dd")
                });

                return JsonSerializer.Serialize(new { ok = true, total = filtered.Count, cards = result }, _json);
            });

        // ── kanban_move_card ──────────────────────────────────────────────────
        registry.Register(
            new ToolDefinition
            {
                Name = "kanban_move_card",
                Description = "Move um card para outra coluna do quadro Kanban. " +
                              "Use quando o usuário quiser mudar o status de uma tarefa.",
                InputSchema = JsonDocument.Parse("""
                {
                  "type": "object",
                  "required": ["card_id", "target_column"],
                  "properties": {
                    "card_id":       { "type": "string", "description": "ID do card a mover (obtido via kanban_list_cards)." },
                    "target_column": { "type": "string", "description": "ID da coluna destino: 'backlog', 'running', 'review' ou 'done'." }
                  }
                }
                """).RootElement
            },
            async (input, ct) =>
            {
                var cardId   = input.TryGetString("card_id")       ?? throw new ArgumentException("card_id é obrigatório.");
                var targetCol = input.TryGetString("target_column") ?? throw new ArgumentException("target_column é obrigatório.");

                await kanban.MoveCardAsync(cardId, targetCol, ct);

                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    card_id = cardId,
                    new_column = targetCol,
                    message = $"Card movido para '{targetCol}'."
                }, _json);
            });

        // ── kanban_update_card ────────────────────────────────────────────────
        registry.Register(
            new ToolDefinition
            {
                Name = "kanban_update_card",
                Description = "Atualiza campos de um card existente no Kanban (título, descrição, instruções, agente) " +
                              "e/ou adiciona um comentário. Use quando o usuário quiser editar uma tarefa.",
                InputSchema = JsonDocument.Parse("""
                {
                  "type": "object",
                  "required": ["card_id"],
                  "properties": {
                    "card_id":      { "type": "string", "description": "ID do card a atualizar." },
                    "title":        { "type": "string", "description": "Novo título (opcional)." },
                    "description":  { "type": "string", "description": "Nova descrição (opcional)." },
                    "instructions": { "type": "string", "description": "Novas instruções para o agente (opcional)." },
                    "agent":        { "type": "string", "description": "Novo agente: 'claude', 'codex', 'aider', 'gemini', 'copilot' (opcional)." },
                    "color":        { "type": "string", "description": "Nova cor hexadecimal (opcional)." },
                    "add_comment":  { "type": "string", "description": "Texto de comentário a acrescentar ao card (opcional)." }
                  }
                }
                """).RootElement
            },
            async (input, ct) =>
            {
                var cardId = input.TryGetString("card_id") ?? throw new ArgumentException("card_id é obrigatório.");

                var workspaceId = lifecycle.CurrentWorkspace?.Id ?? "default";
                var board = await kanban.GetBoardForWorkspaceAsync(workspaceId, ct);
                if (board is null)
                    return JsonSerializer.Serialize(new { ok = false, message = "Board não encontrado." }, _json);

                var cards = await kanban.GetCardsForBoardAsync(board.Id, ct);
                var card  = cards.FirstOrDefault(c => c.Id == cardId);
                if (card is null)
                    return JsonSerializer.Serialize(new { ok = false, message = $"Card '{cardId}' não encontrado." }, _json);

                // Apply only supplied fields
                if (input.TryGetString("title")        is { } t) card.Title        = t;
                if (input.TryGetString("description")  is { } d) card.Description  = d;
                if (input.TryGetString("instructions") is { } i) card.Instructions = i;
                if (input.TryGetString("agent")        is { } a) card.Agent        = a;
                if (input.TryGetString("color")        is { } c) card.Color        = c;
                card.UpdatedAt = DateTime.UtcNow;

                await kanban.UpdateCardAsync(card, ct);

                if (input.TryGetString("add_comment") is { Length: > 0 } commentText)
                {
                    await kanban.AddCommentAsync(new KanbanComment
                    {
                        CardId       = card.Id,
                        Text         = commentText,
                        IsAgentOutput = true
                    }, ct);
                }

                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    card_id = card.Id,
                    title   = card.Title,
                    message = $"Card '{card.Title}' atualizado."
                }, _json);
            });
    }
}

// Helper extension to safely read string properties from JsonElement
file static class JsonElementExtensions
{
    public static string? TryGetString(this JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var prop)
            && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }
}
