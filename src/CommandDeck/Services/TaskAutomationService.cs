using System.IO;
using CommandDeck.Helpers;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Implementation of <see cref="ITaskAutomationService"/>.
/// Subscribes to <see cref="IMcpServerService"/> events at construction time
/// so that agents can report progress/completion back to the kanban board.
/// </summary>
public sealed class TaskAutomationService : ITaskAutomationService, IDisposable
{
    private readonly IKanbanService   _kanbanService;
    private readonly ITerminalService _terminalService;
    private readonly IMcpServerService _mcpServer;
    private readonly ISettingsService  _settingsService;

    public TaskAutomationService(
        IKanbanService kanbanService,
        ITerminalService terminalService,
        IMcpServerService mcpServer,
        ISettingsService settingsService)
    {
        _kanbanService   = kanbanService;
        _terminalService = terminalService;
        _mcpServer       = mcpServer;
        _settingsService = settingsService;

        // Subscribe once; events carry cardId for routing.
        _mcpServer.CardCompleted += OnCardCompleted;
        _mcpServer.CardUpdated   += OnCardProgressUpdated;
        _mcpServer.CardError     += OnCardError;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> CanLaunchCardAsync(string boardId, string cardId, CancellationToken ct = default)
    {
        var cards = await _kanbanService.GetCardsForBoardAsync(boardId, ct).ConfigureAwait(false);
        var card  = cards.FirstOrDefault(c => c.Id == cardId);
        if (card is null) return false;
        if (card.CardRefs.Count == 0) return true;

        var doneIds = cards
            .Where(c => c.ColumnId.Equals("done", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Id)
            .ToHashSet();

        return card.CardRefs.All(dep => doneIds.Contains(dep));
    }

    /// <inheritdoc/>
    public async Task<string> LaunchCardAsync(
        string boardId, string cardId,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        // 1. Load cards and find the target
        var cards = await _kanbanService.GetCardsForBoardAsync(boardId, ct).ConfigureAwait(false);
        var card  = cards.FirstOrDefault(c => c.Id == cardId)
                    ?? throw new InvalidOperationException($"Card '{cardId}' not found in board '{boardId}'.");

        // 2. Check dependencies
        var doneIds = cards
            .Where(c => c.ColumnId.Equals("done", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Id)
            .ToHashSet();

        var unmet = card.CardRefs.Where(dep => !doneIds.Contains(dep)).ToList();
        if (unmet.Count > 0)
        {
            var depTitles = cards
                .Where(c => unmet.Contains(c.Id))
                .Select(c => c.Title);
            throw new InvalidOperationException(
                $"Dependências não concluídas: {string.Join(", ", depTitles)}");
        }

        // 3. Write brief to disk
        var briefPath = await AgentBriefBuilder.WriteAsync(card, cards).ConfigureAwait(false);

        // 4. Build agent command (shell-aware)
        var settings = _settingsService.CurrentSettings;
        var command  = BuildAgentCommand(card, briefPath, settings.DefaultShell);

        // 5. Create dedicated terminal session
        var session = await _terminalService.CreateSessionAsync(
            settings.DefaultShell,
            workingDirectory: workingDirectory).ConfigureAwait(false);

        // Small delay to let the shell initialise before sending the command
        await Task.Delay(800, ct).ConfigureAwait(false);
        await _terminalService.WriteAsync(session.Id, command + "\r").ConfigureAwait(false);

        // 6. Move card to "running" and mark as launched
        card.Launched  = true;
        card.UpdatedAt = DateTime.UtcNow;
        await _kanbanService.UpdateCardAsync(card, ct).ConfigureAwait(false);
        await _kanbanService.MoveCardAsync(cardId, "running", ct).ConfigureAwait(false);

        // 7. Add kick-off comment
        await _kanbanService.AddCommentAsync(new KanbanComment
        {
            CardId        = cardId,
            Text          = $"Agente `{card.Agent}` iniciado. Terminal: `{session.Id[..8]}`\nBrief: `{briefPath}`",
            IsAgentOutput = false
        }, ct).ConfigureAwait(false);

        return session.Id;
    }

    // ── MCP event handlers ────────────────────────────────────────────────────

    private void OnCardCompleted(string cardId, string summary)
    {
        _ = HandleCardCompletedAsync(cardId, summary);
    }

    private async Task HandleCardCompletedAsync(string cardId, string summary)
    {
        try
        {
            await _kanbanService.MoveCardAsync(cardId, "review").ConfigureAwait(false);
            await _kanbanService.AddCommentAsync(new KanbanComment
            {
                CardId        = cardId,
                Text          = $"Concluído pelo agente:\n\n{summary}",
                IsAgentOutput = true
            }).ConfigureAwait(false);
        }
        catch { /* Silently ignore — the card may have been deleted */ }
    }

    private void OnCardProgressUpdated(string cardId, string note)
    {
        _ = HandleCardProgressAsync(cardId, note);
    }

    private async Task HandleCardProgressAsync(string cardId, string note)
    {
        try
        {
            await _kanbanService.AddCommentAsync(new KanbanComment
            {
                CardId        = cardId,
                Text          = note,
                IsAgentOutput = true
            }).ConfigureAwait(false);
        }
        catch { }
    }

    private void OnCardError(string cardId, string reason)
    {
        _ = HandleCardErrorAsync(cardId, reason);
    }

    private async Task HandleCardErrorAsync(string cardId, string reason)
    {
        try
        {
            await _kanbanService.AddCommentAsync(new KanbanComment
            {
                CardId        = cardId,
                Text          = $"Erro do agente:\n\n{reason}",
                IsAgentOutput = true
            }).ConfigureAwait(false);
        }
        catch { }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the shell command to invoke the AI agent CLI with the brief as input.
    /// Handles path conversion for WSL vs Windows shells.
    /// </summary>
    private static string BuildAgentCommand(KanbanCard card, string briefPath, ShellType shellType)
    {
        // WSL and GitBash both use Unix-style paths; PowerShell and CMD use Windows paths.
        bool isUnixShell = shellType is ShellType.WSL or ShellType.GitBash;
        string pathArg   = isUnixShell ? AgentBriefBuilder.ToWslPath(briefPath) : briefPath;

        // Per-agent command pattern
        string agentName = card.Agent?.ToLowerInvariant() ?? "claude";

        return agentName switch
        {
            "aider"  => isUnixShell
                            ? $"aider --message-file '{pathArg}'"
                            : $"aider --message-file \"{pathArg}\"",
            "codex"  => isUnixShell
                            ? $"codex \"$(cat '{pathArg}')\""
                            : $"codex (Get-Content '{pathArg}' -Raw)",
            "gemini" => isUnixShell
                            ? $"gemini -p \"$(cat '{pathArg}')\""
                            : $"gemini -p (Get-Content '{pathArg}' -Raw)",
            _        => isUnixShell  // claude (default) and others
                            ? $"claude -p \"$(cat '{pathArg}')\""
                            : $"claude -p (Get-Content '{pathArg}' -Raw)"
        };
    }

    // ── Dispose ────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _mcpServer.CardCompleted -= OnCardCompleted;
        _mcpServer.CardUpdated   -= OnCardProgressUpdated;
        _mcpServer.CardError     -= OnCardError;
    }
}
