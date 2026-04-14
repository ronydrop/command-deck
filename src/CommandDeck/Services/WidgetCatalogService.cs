using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// In-memory + JSON-persisted registry of built-in widgets.
/// Persists the enabled/disabled state to <c>%APPDATA%\CommandDeck\widget-catalog.json</c>.
/// </summary>
public sealed class WidgetCatalogService : IWidgetCatalogService
{
    private static readonly string PersistPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "CommandDeck", "widget-catalog.json");

    private readonly List<WidgetCatalogEntry> _entries;

    public event Action? CatalogChanged;

    // ─── Built-in registry ────────────────────────────────────────────────────

    private static readonly WidgetCatalogEntry[] DefaultEntries =
    [
        new()
        {
            Key = "terminal",
            Name = "Terminal",
            Description = "Sessão de terminal completa usando ConPTY. Suporta PowerShell, WSL, CMD e Git Bash.",
            Icon = "⬛",
            AccentColor = "#a6e3a1",
            Category = "Core",
            IsCore = true,
            CanvasItemType = CanvasItemType.Terminal,
            PreviewHint = "PowerShell · WSL · CMD · Git Bash"
        },
        new()
        {
            Key = "browser",
            Name = "Browser",
            Description = "Browser embutido com suporte a múltiplas instâncias, modo Desktop/Mobile e navegação livre.",
            Icon = "🌐",
            AccentColor = "#89b4fa",
            Category = "Dev",
            IsCore = false,
            CanvasItemType = CanvasItemType.BrowserWidget,
            PreviewHint = "WebView2 · Desktop/Mobile · Multi-instância"
        },
        new()
        {
            Key = "chat_ai",
            Name = "Chat IA",
            Description = "Chat com IA diretamente no canvas. Suporta múltiplas instâncias simultâneas, histórico por tile e streaming.",
            Icon = "💬",
            AccentColor = "#cba6f7",
            Category = "IA",
            IsCore = false,
            CanvasItemType = CanvasItemType.ChatWidget,
            PreviewHint = "Ollama · OpenAI · Anthropic · OpenRouter"
        },
        new()
        {
            Key = "code_editor",
            Name = "Editor de Código",
            Description = "Monaco Editor (mesmo engine do VS Code) com sintaxe para 13 linguagens, minimap e atalhos.",
            Icon = "⌨",
            AccentColor = "#89b4fa",
            Category = "Dev",
            IsCore = false,
            CanvasItemType = CanvasItemType.CodeEditorWidget,
            PreviewHint = "C# · JS · TS · Python · JSON · Markdown · +"
        },
        new()
        {
            Key = "file_explorer",
            Name = "Explorador de Arquivos",
            Description = "Árvore de arquivos com lazy loading, busca em tempo real e ícones por tipo de arquivo.",
            Icon = "📁",
            AccentColor = "#a6e3a1",
            Category = "Dev",
            IsCore = false,
            CanvasItemType = CanvasItemType.FileExplorerWidget,
            PreviewHint = "Abre pasta do projeto automaticamente"
        },
        new()
        {
            Key = "git",
            Name = "Git Status",
            Description = "Painel com status do repositório, branch atual, commits ahead/behind, diffs e ações rápidas.",
            Icon = "🌿",
            AccentColor = "#fab387",
            Category = "Dev",
            IsCore = false,
            WidgetType = Models.WidgetType.Git,
            PreviewHint = "Branch · Staged · Unstaged · Diff"
        },
        new()
        {
            Key = "process_monitor",
            Name = "Monitor de Processos",
            Description = "Monitora processos em tempo real via WMI: Node.js, PHP, Python, Docker e mais.",
            Icon = "📊",
            AccentColor = "#89dceb",
            Category = "Dev",
            IsCore = false,
            WidgetType = Models.WidgetType.Process,
            PreviewHint = "node · php · python · docker · artisan"
        },
        new()
        {
            Key = "note",
            Name = "Nota",
            Description = "Post-it rápido para lembretes, TODOs e anotações durante o desenvolvimento. Cor customizável.",
            Icon = "📝",
            AccentColor = "#f9e2af",
            Category = "Produtividade",
            IsCore = false,
            WidgetType = Models.WidgetType.Note,
            PreviewHint = "Texto livre · Cor personalizada"
        },
        new()
        {
            Key = "shortcuts",
            Name = "Atalhos",
            Description = "Botões de atalho personalizados para abrir URLs, executar comandos ou acionar scripts.",
            Icon = "⚡",
            AccentColor = "#f9e2af",
            Category = "Produtividade",
            IsCore = false,
            WidgetType = Models.WidgetType.Shortcut,
            PreviewHint = "URLs · Comandos · Scripts"
        },
        new()
        {
            Key = "system_monitor",
            Name = "Monitor do Sistema",
            Description = "CPU, memória RAM, rede e disco em tempo real com gráficos de histórico.",
            Icon = "🖥",
            AccentColor = "#89dceb",
            Category = "Sistema",
            IsCore = false,
            WidgetType = Models.WidgetType.SystemMonitor,
            PreviewHint = "CPU · RAM · Disco · Rede"
        },
        new()
        {
            Key = "kanban",
            Name = "Kanban",
            Description = "Quadro Kanban leve para organizar tarefas do projeto diretamente no canvas.",
            Icon = "🗂",
            AccentColor = "#b4befe",
            Category = "Produtividade",
            IsCore = false,
            WidgetType = Models.WidgetType.Kanban,
            PreviewHint = "Todo · Em Andamento · Feito"
        },
        new()
        {
            Key = "pomodoro",
            Name = "Pomodoro",
            Description = "Timer Pomodoro integrado com notificações e contagem de sessões.",
            Icon = "🍅",
            AccentColor = "#f38ba8",
            Category = "Produtividade",
            IsCore = false,
            WidgetType = Models.WidgetType.Pomodoro,
            PreviewHint = "25min foco · 5min pausa · Notificações"
        },
        new()
        {
            Key = "token_counter",
            Name = "Contador de Tokens",
            Description = "Conta tokens de texto para estimar custos de chamadas à API de IA.",
            Icon = "🔢",
            AccentColor = "#cba6f7",
            Category = "IA",
            IsCore = false,
            WidgetType = Models.WidgetType.TokenCounter,
            PreviewHint = "GPT-4 · Claude · Llama · custo estimado"
        },
        new()
        {
            Key = "activity_feed",
            Name = "Activity Feed",
            Description = "Log em tempo real de todas as ações do projeto: terminais, git, IA, arquivos e mais.",
            Icon = "📋",
            AccentColor = "#94e2d5",
            Category = "Sistema",
            IsCore = false,
            CanvasItemType = CanvasItemType.ActivityFeedWidget,
            PreviewHint = "Terminal · Git · IA · Browser · Widgets"
        },
        new()
        {
            Key = "image",
            Name = "Imagem",
            Description = "Exibe uma imagem no canvas. Útil para wireframes, diagramas ou referências visuais.",
            Icon = "🖼",
            AccentColor = "#94e2d5",
            Category = "Conteúdo",
            IsCore = false,
            WidgetType = Models.WidgetType.Image,
            PreviewHint = "PNG · JPG · SVG · Gif"
        },
    ];

    // ─── Constructor ──────────────────────────────────────────────────────────

    public WidgetCatalogService()
    {
        _entries = DefaultEntries.Select(e => new WidgetCatalogEntry
        {
            Key = e.Key,
            Name = e.Name,
            Description = e.Description,
            Icon = e.Icon,
            AccentColor = e.AccentColor,
            Category = e.Category,
            IsCore = e.IsCore,
            WidgetType = e.WidgetType,
            CanvasItemType = e.CanvasItemType,
            PreviewHint = e.PreviewHint,
            IsEnabled = true
        }).ToList();

        LoadPersistedState();
    }

    // ─── Interface implementation ─────────────────────────────────────────────

    public IReadOnlyList<WidgetCatalogEntry> All => _entries;

    public IReadOnlyList<WidgetCatalogEntry> Enabled
        => _entries.Where(e => e.IsEnabled).ToList();

    public WidgetCatalogEntry? Get(string key)
        => _entries.FirstOrDefault(e => e.Key == key);

    public bool IsEnabled(string key)
        => _entries.FirstOrDefault(e => e.Key == key)?.IsEnabled ?? false;

    public void SetEnabled(string key, bool enabled)
    {
        var entry = _entries.FirstOrDefault(e => e.Key == key);
        if (entry is null || entry.IsCore) return;
        entry.IsEnabled = enabled;
        PersistState();
        CatalogChanged?.Invoke();
    }

    public void ResetToDefaults()
    {
        foreach (var e in _entries)
            e.IsEnabled = true;
        PersistState();
        CatalogChanged?.Invoke();
    }

    // ─── Persistence ──────────────────────────────────────────────────────────

    private void LoadPersistedState()
    {
        try
        {
            if (!File.Exists(PersistPath)) return;
            var json = File.ReadAllText(PersistPath);
            var state = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
            if (state is null) return;

            foreach (var (key, enabled) in state)
            {
                var entry = _entries.FirstOrDefault(e => e.Key == key);
                if (entry is not null && !entry.IsCore)
                    entry.IsEnabled = enabled;
            }
        }
        catch { /* ignore corrupt file */ }
    }

    private void PersistState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PersistPath)!);
            var state = _entries
                .Where(e => !e.IsCore)
                .ToDictionary(e => e.Key, e => e.IsEnabled);
            File.WriteAllText(PersistPath, JsonSerializer.Serialize(state,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best-effort */ }
    }
}
