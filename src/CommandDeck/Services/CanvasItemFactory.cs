using CommandDeck.Models;
using CommandDeck.ViewModels;

namespace CommandDeck.Services;

/// <summary>
/// Creates fully initialised <see cref="CanvasItemViewModel"/> instances.
/// Not an interface — used directly by WorkspaceService to avoid circular deps.
/// </summary>
public class CanvasItemFactory
{
    private readonly IGitService _gitService;
    private readonly IProcessMonitorService _processMonitorService;
    private readonly INotificationService _notificationService;

    // WorkspaceService is set after construction to break the circular dependency:
    // WorkspaceService → CanvasItemFactory → IWorkspaceService
    private IWorkspaceService? _workspaceServiceRef;
    public void SetWorkspaceService(IWorkspaceService ws) => _workspaceServiceRef = ws;

    public CanvasItemFactory(
        IGitService gitService,
        IProcessMonitorService processMonitorService,
        INotificationService notificationService)
    {
        _gitService = gitService;
        _processMonitorService = processMonitorService;
        _notificationService = notificationService;
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
            WidgetType.Git => (320.0, 280.0, CanvasItemType.GitWidget),
            WidgetType.Process => (400.0, 350.0, CanvasItemType.ProcessWidget),
            WidgetType.Note => (260.0, 260.0, CanvasItemType.NoteWidget),
            WidgetType.Image => (400.0, 300.0, CanvasItemType.ImageWidget),
            _ => (300.0, 200.0, CanvasItemType.ShortcutWidget)
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
            workspaceService: _workspaceServiceRef,
            notificationService: _notificationService);
    }
}
