using CommandDeck.Models;
using CommandDeck.ViewModels;

namespace CommandDeck.Services;

/// <summary>
/// Creates fully initialised <see cref="CanvasItemViewModel"/> instances.
/// Receives <see cref="IWorkspaceService"/> as <see cref="Lazy{T}"/> to break the
/// circular dependency: WorkspaceService → CanvasItemFactory → IWorkspaceService.
/// </summary>
public class CanvasItemFactory
{
    private readonly IGitService _gitService;
    private readonly IProcessMonitorService _processMonitorService;
    private readonly INotificationService _notificationService;
    private readonly Lazy<IWorkspaceService> _workspaceService;
    private readonly IKanbanService _kanbanService;
    private readonly IAssistantService _assistantService;
    private readonly ITaskAutomationService _taskAutomationService;
    private readonly IClaudeUsageService _claudeUsageService;
    private readonly ISettingsService _settingsService;
    private readonly ISecretStorageService _secretStorageService;
    private readonly IClaudeOAuthService _claudeOAuthService;
    private readonly IDatabaseService _db;
    private readonly AssistantSettings _assistantSettings;
    private readonly IEventBusService _eventBus;
    private readonly ITileContextService _tileContext;

    public CanvasItemFactory(
        IGitService gitService,
        IProcessMonitorService processMonitorService,
        INotificationService notificationService,
        Lazy<IWorkspaceService> workspaceService,
        IKanbanService kanbanService,
        IAssistantService assistantService,
        ITaskAutomationService taskAutomationService,
        IClaudeUsageService claudeUsageService,
        ISettingsService settingsService,
        ISecretStorageService secretStorageService,
        IClaudeOAuthService claudeOAuthService,
        IDatabaseService db,
        AssistantSettings assistantSettings,
        IEventBusService eventBus,
        ITileContextService tileContext)
    {
        _gitService = gitService;
        _processMonitorService = processMonitorService;
        _notificationService = notificationService;
        _workspaceService = workspaceService;
        _kanbanService = kanbanService;
        _assistantService = assistantService;
        _taskAutomationService = taskAutomationService;
        _claudeUsageService = claudeUsageService;
        _settingsService = settingsService;
        _secretStorageService = secretStorageService;
        _claudeOAuthService = claudeOAuthService;
        _db = db;
        _assistantSettings = assistantSettings;
        _eventBus = eventBus;
        _tileContext = tileContext;
    }

    // ─── Terminal ───────────────────────────────────────────────────────────────

    public TerminalCanvasItemViewModel CreateTerminalItem(
        TerminalViewModel terminal, double x = 40, double y = 40)
    {
        var model = new CanvasItemModel
        {
            Type = CanvasItemType.Terminal,
            X = x,
            Y = y,
            Width = 780,
            Height = 520
        };

        if (terminal.Session is not null)
        {
            model.Metadata["terminalId"] = terminal.Session.Id;
            if (!string.IsNullOrEmpty(terminal.Session.WorkingDirectory))
                model.Metadata["workingDirectory"] = terminal.Session.WorkingDirectory;
        }

        model.Metadata["shellType"] = terminal.ShellType.ToString();

        return new TerminalCanvasItemViewModel(terminal, model);
    }

    /// <summary>
    /// Creates a <see cref="TerminalCanvasItemViewModel"/> using a pre-existing
    /// <see cref="CanvasItemModel"/> (for layout restoration).
    /// </summary>
    public TerminalCanvasItemViewModel CreateTerminalItemFromModel(
        TerminalViewModel terminal, CanvasItemModel model)
    {
        // Update metadata with the new session info
        if (terminal.Session is not null)
        {
            model.Metadata["terminalId"] = terminal.Session.Id;
            if (!string.IsNullOrEmpty(terminal.Session.WorkingDirectory))
                model.Metadata["workingDirectory"] = terminal.Session.WorkingDirectory;
        }
        model.Metadata["shellType"] = terminal.ShellType.ToString();

        return new TerminalCanvasItemViewModel(terminal, model);
    }

    // ─── Widgets ────────────────────────────────────────────────────────────────

    public WidgetCanvasItemViewModel CreateWidgetItem(
        WidgetType type, double x = 40, double y = 40)
    {
        var (w, h, canvasType) = type switch
        {
            WidgetType.Git           => (320.0, 280.0, CanvasItemType.GitWidget),
            WidgetType.Process       => (400.0, 350.0, CanvasItemType.ProcessWidget),
            WidgetType.Note          => (260.0, 260.0, CanvasItemType.NoteWidget),
            WidgetType.Image         => (400.0, 300.0, CanvasItemType.ImageWidget),
            WidgetType.Kanban        => (600.0, 440.0, CanvasItemType.KanbanWidget),
            WidgetType.Chat          => (380.0, 500.0, CanvasItemType.ChatWidget),
            WidgetType.SystemMonitor => (360.0, 260.0, CanvasItemType.SystemMonitorWidget),
            WidgetType.TokenCounter  => (340.0, 320.0, CanvasItemType.TokenCounterWidget),
            WidgetType.Pomodoro      => (280.0, 340.0, CanvasItemType.PomodoroWidget),
            _                        => (300.0, 200.0, CanvasItemType.ShortcutWidget)
        };

        var model = new CanvasItemModel
        {
            Type = canvasType,
            X = x,
            Y = y,
            Width = w,
            Height = h
        };

        return new WidgetCanvasItemViewModel(
            type, model,
            gitService: _gitService,
            processMonitorService: _processMonitorService,
            workspaceService: _workspaceService.Value,
            notificationService: _notificationService,
            kanbanService: _kanbanService,
            assistantService: _assistantService,
            taskAutomationService: _taskAutomationService,
            claudeUsageService: _claudeUsageService);
    }

    // ─── Chat Tile ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a dedicated <see cref="ChatCanvasItemViewModel"/> tile with full chat functionality.
    /// Each instance has its own conversation history and can be placed multiple times on the canvas.
    /// </summary>
    public ChatCanvasItemViewModel CreateChatTileItem(double x = 40, double y = 40)
    {
        var model = new CanvasItemModel
        {
            Type = CanvasItemType.ChatWidget,
            X = x,
            Y = y,
            Width = 400,
            Height = 540
        };

        return new ChatCanvasItemViewModel(
            model,
            _assistantService,
            _notificationService,
            _settingsService,
            _secretStorageService,
            _claudeOAuthService,
            _db,
            _assistantSettings);
    }

    /// <summary>
    /// Creates a <see cref="ChatCanvasItemViewModel"/> from a pre-existing
    /// <see cref="CanvasItemModel"/> (for layout restoration).
    /// </summary>
    public ChatCanvasItemViewModel CreateChatTileItemFromModel(CanvasItemModel model)
    {
        return new ChatCanvasItemViewModel(
            model,
            _assistantService,
            _notificationService,
            _settingsService,
            _secretStorageService,
            _claudeOAuthService,
            _db,
            _assistantSettings);
    }

    // ─── Code Editor ────────────────────────────────────────────────────────────

    /// <summary>Creates a new Code Editor canvas tile at the given position.</summary>
    public CodeEditorCanvasItemViewModel CreateCodeEditorItem(double x = 40, double y = 40)
    {
        var model = new CanvasItemModel
        {
            Type = CanvasItemType.CodeEditorWidget,
            X = x, Y = y,
            Width = 680, Height = 500
        };
        return new CodeEditorCanvasItemViewModel(model, _notificationService, _eventBus, _tileContext);
    }

    /// <summary>Restores a Code Editor tile from a persisted <see cref="CanvasItemModel"/>.</summary>
    public CodeEditorCanvasItemViewModel CreateCodeEditorItemFromModel(CanvasItemModel model)
        => new(model, _notificationService, _eventBus, _tileContext);

    // ─── File Explorer ───────────────────────────────────────────────────────────

    /// <summary>Creates a new File Explorer canvas tile at the given position.</summary>
    public FileExplorerCanvasItemViewModel CreateFileExplorerItem(double x = 40, double y = 40)
    {
        var model = new CanvasItemModel
        {
            Type = CanvasItemType.FileExplorerWidget,
            X = x, Y = y,
            Width = 300, Height = 480
        };
        return new FileExplorerCanvasItemViewModel(model, _notificationService, _eventBus, _tileContext);
    }

    /// <summary>Restores a File Explorer tile from a persisted <see cref="CanvasItemModel"/>.</summary>
    public FileExplorerCanvasItemViewModel CreateFileExplorerItemFromModel(CanvasItemModel model)
        => new(model, _notificationService, _eventBus, _tileContext);

    // ─── Browser ─────────────────────────────────────────────────────────────

    /// <summary>Creates a new Browser canvas tile at the given position.</summary>
    public BrowserCanvasItemViewModel CreateBrowserItem(double x = 40, double y = 40)
    {
        var model = new CanvasItemModel
        {
            Type = CanvasItemType.BrowserWidget,
            X = x, Y = y,
            Width = 720, Height = 500
        };
        return new BrowserCanvasItemViewModel(model, _notificationService, _eventBus, _tileContext);
    }

    /// <summary>Restores a Browser tile from a persisted <see cref="CanvasItemModel"/>.</summary>
    public BrowserCanvasItemViewModel CreateBrowserItemFromModel(CanvasItemModel model)
        => new(model, _notificationService, _eventBus, _tileContext);
}
