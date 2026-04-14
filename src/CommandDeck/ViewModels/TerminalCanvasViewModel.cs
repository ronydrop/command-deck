using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// ViewModel for the spatial terminal canvas.
/// Exposes the item collection, camera state and canvas commands.
/// WPF transform animations remain in the code-behind (UI-only concern).
/// </summary>
public partial class TerminalCanvasViewModel : ObservableObject
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ICanvasCameraService _cameraService;
    private readonly ILayoutPersistenceService _persistenceService;
    private readonly IPaneStateService _paneStateService;
    private readonly IAiAgentStateService _aiAgentStateService;
    private readonly ISettingsService _settingsService;
    private readonly IUndoRedoService _undoRedo;
    private readonly TiledLayoutStrategy _tiledStrategy;
    private readonly FreeCanvasLayoutStrategy _freeStrategy;
    private readonly SplitPaneLayoutStrategy _splitPaneStrategy;
    private DispatcherTimer? _layoutDebounceTimer;
    private const int LayoutDebounceMs = 50;

    /// <summary>Mini-map overlay ViewModel — bound by MiniMapControl in the XAML.</summary>
    public MiniMapViewModel MiniMap { get; }

    /// <summary>Current project id used as the workspace persistence key.</summary>
    public string? CurrentProjectId { get; set; }

    // ─── Canvas items (forwarded from WorkspaceService) ──────────────────────

    /// <summary>All canvas items (terminals + widgets) bound to the ItemsControl.</summary>
    public ObservableCollection<CanvasItemViewModel> Items => _workspaceService.Items;

    /// <summary>Terminal-only items bound to the right sidebar.</summary>
    public ObservableCollection<TerminalCanvasItemViewModel> TerminalItems => _workspaceService.TerminalItems;

    // ─── Observable state ────────────────────────────────────────────────────

    [ObservableProperty] private bool _isFocusMode;

    [ObservableProperty] private TerminalCanvasItemViewModel? _focusedItem;

    [ObservableProperty] private TerminalCanvasItemViewModel? _activeTerminal;

    /// <summary>Camera offset X (pixels) — kept in sync with CanvasCameraService.</summary>
    [ObservableProperty] private double _cameraOffsetX;

    /// <summary>Camera offset Y (pixels) — kept in sync with CanvasCameraService.</summary>
    [ObservableProperty] private double _cameraOffsetY;

    /// <summary>Camera zoom multiplier — kept in sync with CanvasCameraService.</summary>
    [ObservableProperty] private double _cameraZoom = 1.0;

    /// <summary>Zoom as integer percentage for the toolbar label.</summary>
    [ObservableProperty] private int _zoomPercent = 100;

    // ─── Layout mode ────────────────────────────────────────────────────

    [ObservableProperty] private LayoutMode _layoutMode = LayoutMode.FreeCanvas;

    /// <summary>True when in free canvas mode (drag/pan/zoom enabled).</summary>
    public bool IsCanvasMode => LayoutMode == LayoutMode.FreeCanvas;

    /// <summary>True when in tiled grid mode (auto-arranged layout).</summary>
    public bool IsTiledMode => LayoutMode == LayoutMode.Tiled;

    /// <summary>True when in split-pane mode.</summary>
    public bool IsSplitPaneMode => LayoutMode == LayoutMode.SplitPane;

    /// <summary>Viewport width in pixels (fed from View SizeChanged).</summary>
    [ObservableProperty] private double _viewportWidth;

    /// <summary>Viewport height in pixels (fed from View SizeChanged).</summary>
    [ObservableProperty] private double _viewportHeight;

    /// <summary>View should apply tiled positions (event carries the placement list).</summary>
    public event Action<LayoutMode>? LayoutModeChanged;

    // ─── Canvas zoom mode ─────────────────────────────────────────────

    [ObservableProperty] private bool _zoomRequiresCtrl = true;

    /// <summary>True when at least one terminal/widget exists on the canvas.</summary>
    [ObservableProperty] private bool _hasTerminals;

    // ─── Canvas wallpaper ───────────────────────────────────────────────

    [ObservableProperty] private ImageSource? _wallpaperSource;
    [ObservableProperty] private double _wallpaperOpacity = 0.15;
    [ObservableProperty] private Stretch _wallpaperStretch = Stretch.UniformToFill;
    [ObservableProperty] private bool _hasWallpaper;

    // ─── Events (consumed by the View to drive animations) ───────────────────

    /// <summary>View should animate to focus on this item.</summary>
    public event Action<TerminalCanvasItemViewModel>? FocusItemRequested;

    /// <summary>View should animate to fit all items in the viewport.</summary>
    public event Action? FitAllRequested;

    /// <summary>View should animate back to the overview state.</summary>
    public event Action? ExitFocusModeRequested;

    // ─── Constructor ─────────────────────────────────────────────────────────

    public TerminalCanvasViewModel(
        IWorkspaceService workspaceService,
        ICanvasCameraService cameraService,
        ILayoutPersistenceService persistenceService,
        IPaneStateService paneStateService,
        IAiAgentStateService aiAgentStateService,
        ISettingsService settingsService,
        IUndoRedoService undoRedo,
        TiledLayoutStrategy tiledStrategy,
        FreeCanvasLayoutStrategy freeStrategy,
        SplitPaneLayoutStrategy splitPaneStrategy,
        MiniMapViewModel miniMap)
    {
        _workspaceService = workspaceService;
        _cameraService = cameraService;
        _persistenceService = persistenceService;
        _paneStateService = paneStateService;
        _aiAgentStateService = aiAgentStateService;
        _settingsService = settingsService;
        _undoRedo = undoRedo;
        _tiledStrategy = tiledStrategy;
        _freeStrategy = freeStrategy;
        _splitPaneStrategy = splitPaneStrategy;

        MiniMap = miniMap;

        _cameraService.CameraChanged += OnCameraChanged;

        // Notify undo/redo RelayCommands whenever the stack state changes
        _undoRedo.StateChanged += () =>
        {
            UndoActionCommand.NotifyCanExecuteChanged();
            RedoActionCommand.NotifyCanExecuteChanged();
        };

        // Propagate pane state changes to the matching canvas item
        _paneStateService.StateChanged += OnPaneStateChanged;

        // Propagate AI agent state changes to the matching canvas item
        _aiAgentStateService.StateChanged += OnAiAgentStateChanged;

        // Track whether any items exist (drives canvas lock overlay)
        _hasTerminals = Items.Count > 0;

        // Recalculate tiled layout when items change
        _workspaceService.Items.CollectionChanged += OnItemsCollectionChanged;

        // Load wallpaper from settings and subscribe to changes
        _settingsService.SettingsChanged += OnSettingsChanged;
        _ = LoadWallpaperFromSettingsAsync();
    }

    private void OnPaneStateChanged(Models.PaneStateInfo info)
    {
        // Find the terminal canvas item whose session ID matches the pane ID
        var item = _workspaceService.TerminalItems
            .FirstOrDefault(t => t.Terminal.Session?.Id == info.PaneId);
        item?.UpdatePaneState(info);
    }

    private void OnAiAgentStateChanged(Models.AiAgentStateChangedArgs args)
    {
        var item = _workspaceService.TerminalItems
            .FirstOrDefault(t => t.Terminal.Session?.Id == args.SessionId);
        item?.UpdateAiAgentState(args);
    }

    // ─── Layout mode commands ──────────────────────────────────────────────

    [RelayCommand]
    private void ToggleLayoutMode()
    {
        LayoutMode = LayoutMode == LayoutMode.FreeCanvas
            ? LayoutMode.Tiled
            : LayoutMode.FreeCanvas;
    }

    [RelayCommand]
    private void SetCanvasMode() => LayoutMode = LayoutMode.FreeCanvas;

    [RelayCommand]
    private void SetTiledMode() => LayoutMode = LayoutMode.Tiled;

    [RelayCommand]
    private void SetSplitPaneMode() => LayoutMode = LayoutMode.SplitPane;

    /// <summary>Called from View when the viewport size changes.</summary>
    public void OnViewportSizeChanged(double width, double height)
    {
        ViewportWidth = width;
        ViewportHeight = height;
        ScheduleTiledLayoutRecalculation();
        if (IsSplitPaneMode) RecalculateSplitPaneLayout();
    }

    /// <summary>
    /// Recalculates tiled positions for all terminal items and applies them.
    /// Widgets are hidden in tiled mode (their positions are not changed).
    /// </summary>
    public void RecalculateTiledLayout()
    {
        if (!IsTiledMode || ViewportWidth <= 0 || ViewportHeight <= 0) return;

        // Only terminals participate in tiled layout
        var terminals = _workspaceService.TerminalItems.ToList();
        if (terminals.Count == 0) return;

        var layout = _tiledStrategy.CalculateLayout(terminals.Count, ViewportWidth, ViewportHeight);

        for (int i = 0; i < terminals.Count && i < layout.Placements.Count; i++)
        {
            var p = layout.Placements[i];
            var item = terminals[i];
            item.X = p.X;
            item.Y = p.Y;
            item.Width = p.Width;
            item.Height = p.Height;
        }

        // Hide widgets in tiled mode by moving them off-screen
        foreach (var widget in _workspaceService.Items.OfType<WidgetCanvasItemViewModel>())
        {
            if (!widget.HasFreePositionStash) widget.StashFreePosition();
            widget.X = -9999;
            widget.Y = -9999;
            widget.Width = 0;
            widget.Height = 0;
        }
    }

    /// <summary>
    /// Calculates and applies the split-pane layout to all items.
    /// ALL items (terminals + widgets) participate in the split.
    /// </summary>
    public void RecalculateSplitPaneLayout()
    {
        if (!IsSplitPaneMode || ViewportWidth <= 0 || ViewportHeight <= 0) return;

        var allItems = _workspaceService.Items.ToList();
        if (allItems.Count == 0) return;

        var layout = _splitPaneStrategy.CalculateLayout(allItems.Count, ViewportWidth, ViewportHeight);

        for (int i = 0; i < allItems.Count && i < layout.Placements.Count; i++)
        {
            var p = layout.Placements[i];
            var item = allItems[i];
            item.X = p.X;
            item.Y = p.Y;
            item.Width = p.Width;
            item.Height = p.Height;
        }
    }

    /// <summary>
    /// Auto-arranges all items using the free canvas cascade layout.
    /// Widgets that were hidden (off-screen) get their dimensions restored first.
    /// </summary>
    private void RecalculateFreeCanvasLayout()
    {
        // Restore widget dimensions from stash before laying out
        foreach (var widget in _workspaceService.Items.OfType<WidgetCanvasItemViewModel>())
        {
            if (widget.HasFreePositionStash)
                widget.RestoreFreePosition();
        }

        var allItems = _workspaceService.Items.ToList();
        if (allItems.Count == 0) return;

        double vpW = ViewportWidth > 0 ? ViewportWidth : 1200;

        var widths = allItems.Select(i => i.Width).ToList();
        var heights = allItems.Select(i => i.Height).ToList();
        var layout = _freeStrategy.CalculateReflowPreserveSizes(widths, heights, vpW);

        for (int i = 0; i < allItems.Count && i < layout.Placements.Count; i++)
        {
            var p = layout.Placements[i];
            var item = allItems[i];
            item.X = p.X;
            item.Y = p.Y;
        }
    }

    /// <summary>
    /// Schedules a tiled layout recalculation with debounce to prevent thrashing.
    /// Multiple rapid calls within the debounce window coalesce into a single recalculation.
    /// </summary>
    private void ScheduleTiledLayoutRecalculation()
    {
        if (!IsTiledMode) return;

        if (_layoutDebounceTimer == null)
        {
            _layoutDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(LayoutDebounceMs)
            };
            _layoutDebounceTimer.Tick += (_, _) =>
            {
                _layoutDebounceTimer.Stop();
                RecalculateTiledLayout();
            };
        }

        _layoutDebounceTimer.Stop();
        _layoutDebounceTimer.Start();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasTerminals = Items.Count > 0;
        ScheduleTiledLayoutRecalculation();

        // Propagate tiled mode flag to newly added items
        if (IsTiledMode && e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (CanvasItemViewModel added in e.NewItems)
                added.IsTiledMode = true;
        }

        // Remove any deleted items from the selection set to avoid stale references
        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null)
        {
            foreach (CanvasItemViewModel removed in e.OldItems)
            {
                if (removed.IsSelected)
                    RemoveFromSelection(removed);
            }
        }
    }

    // ─── Layout mode partial callbacks ──────────────────────────────────────

    partial void OnLayoutModeChanged(LayoutMode oldValue, LayoutMode newValue)
    {
        OnPropertyChanged(nameof(IsCanvasMode));
        OnPropertyChanged(nameof(IsTiledMode));
        OnPropertyChanged(nameof(IsSplitPaneMode));

        if (newValue == LayoutMode.Tiled)
        {
            // Exit focus mode if active
            if (IsFocusMode) ExitFocusMode();

            // Save camera state for return
            _cameraService.SaveSnapshot();

            // Stash free-canvas positions so they can be restored later
            foreach (var item in Items)
            {
                item.StashFreePosition();
                item.IsTiledMode = true;
            }

            // Calculate and apply tiled positions
            RecalculateTiledLayout();
        }
        else if (newValue == LayoutMode.SplitPane)
        {
            // Exit focus mode if active
            if (IsFocusMode) ExitFocusMode();

            _cameraService.SaveSnapshot();

            // Stash free-canvas positions
            foreach (var item in Items)
            {
                item.StashFreePosition();
                item.IsTiledMode = true; // reuse tiled style (no drag/resize)
            }

            // Calculate split-pane positions
            RecalculateSplitPaneLayout();
        }
        else // Returning to FreeCanvas
        {
            // Restore the positions the user had before switching to tiled
            foreach (var item in Items)
            {
                item.IsTiledMode = false;
                item.RestoreFreePosition();
            }

            // Restore widgets that were hidden in tiled mode
            foreach (var widget in _workspaceService.Items.OfType<WidgetCanvasItemViewModel>())
            {
                if (widget.HasFreePositionStash)
                    widget.RestoreFreePosition();
            }
        }

        LayoutModeChanged?.Invoke(newValue);
    }

    // ─── Undo / Redo commands ────────────────────────────────────────────────

    /// <summary>Reverses the last recorded canvas operation (Ctrl+Z).</summary>
    [RelayCommand(CanExecute = nameof(CanUndoAction))]
    private void UndoAction() => _undoRedo.Undo();
    private bool CanUndoAction() => _undoRedo.CanUndo;

    /// <summary>Re-applies the last undone canvas operation (Ctrl+Shift+Z).</summary>
    [RelayCommand(CanExecute = nameof(CanRedoAction))]
    private void RedoAction() => _undoRedo.Redo();
    private bool CanRedoAction() => _undoRedo.CanRedo;

    // ─── Undo record helpers (called from CanvasCardControl code-behind) ─────

    /// <summary>
    /// Records a completed item move in the undo history.
    /// Call this after drag is released with the position the item was at before the drag.
    /// </summary>
    public void RecordMove(CanvasItemViewModel item, double oldX, double oldY)
        => _undoRedo.Record(new Models.MoveItemCommand(item, oldX, oldY, item.X, item.Y));

    /// <summary>
    /// Records a completed item resize in the undo history.
    /// Call this after resize is released with the dimensions the item had before the resize.
    /// </summary>
    public void RecordResize(CanvasItemViewModel item, double oldWidth, double oldHeight)
        => _undoRedo.Record(new Models.ResizeItemCommand(item, oldWidth, oldHeight, item.Width, item.Height));

    // ─── Commands ────────────────────────────────────────────────────────────

    /// <summary>
    /// Auto-arranges all items using the appropriate layout strategy for the current mode.
    /// In Canvas mode uses the free cascade layout; in Tiled mode recalculates the grid.
    /// </summary>
    [RelayCommand]
    private void AutoArrange()
    {
        if (IsTiledMode)
            RecalculateTiledLayout();
        else
            RecalculateFreeCanvasLayout();
    }

    [RelayCommand]
    private void ExitFocusMode()
    {
        IsFocusMode = false;
        _workspaceService.SetFocused(null);
        FocusedItem = null;
        ExitFocusModeRequested?.Invoke();
    }

    [RelayCommand]
    private void FitAll()
    {
        FitAllRequested?.Invoke();
    }

    [RelayCommand]
    private async Task SaveLayout()
    {
        await SaveCurrentLayoutAsync();
    }

    /// <summary>Saves the current canvas layout for the active project (or default).</summary>
    public async Task SaveCurrentLayoutAsync()
    {
        var workspace = BuildWorkspaceSnapshot();
        await _persistenceService.SaveAsync(workspace);
    }

    /// <summary>Loads a saved canvas layout for the given project.</summary>
    public async Task<WorkspaceModel?> LoadLayoutAsync(string? projectId)
    {
        return await _persistenceService.LoadAsync(projectId ?? "default");
    }

    /// <summary>Toggles the Git widget at the specified canvas coordinates.</summary>
    public void ToggleGitWidget(double canvasX, double canvasY)
        => _workspaceService.ToggleWidgetItem(WidgetType.Git, canvasX, canvasY);

    /// <summary>Toggles the Process widget at the specified canvas coordinates.</summary>
    public void ToggleProcessWidget(double canvasX, double canvasY)
        => _workspaceService.ToggleWidgetItem(WidgetType.Process, canvasX, canvasY);

    [RelayCommand]
    private void AddShortcutWidget()
        => _workspaceService.AddWidgetItem(WidgetType.Shortcut);

    /// <summary>Adds a new Note/Post-it widget at the specified canvas coordinates.</summary>
    public void AddNoteWidget(double canvasX, double canvasY)
    {
        var item = _workspaceService.AddWidgetItem(WidgetType.Note);
        item.X = canvasX;
        item.Y = canvasY;
    }

    /// <summary>Adds a new Image widget at the specified canvas coordinates.</summary>
    public WidgetCanvasItemViewModel AddImageWidget(double canvasX, double canvasY)
    {
        var item = _workspaceService.AddWidgetItem(WidgetType.Image);
        item.X = canvasX;
        item.Y = canvasY;
        return item;
    }

    /// <summary>Adds a Kanban board widget to the canvas at the given position.</summary>
    public void AddKanbanWidget(double canvasX = 40, double canvasY = 40)
    {
        var item = _workspaceService.AddWidgetItem(WidgetType.Kanban);
        item.X = canvasX;
        item.Y = canvasY;
    }

    /// <summary>Adds a Chat AI widget to the canvas at the given position.</summary>
    public void AddChatWidget(double canvasX = 40, double canvasY = 40)
    {
        _workspaceService.AddChatTile(canvasX, canvasY);
    }

    /// <summary>Adds a Code Editor widget to the canvas at the given position.</summary>
    public void AddCodeEditorWidget(double canvasX = 40, double canvasY = 40)
    {
        _workspaceService.AddCodeEditorTile(canvasX, canvasY);
    }

    /// <summary>Adds a File Explorer widget to the canvas at the given position.</summary>
    public void AddFileExplorerWidget(double canvasX = 40, double canvasY = 40)
    {
        _workspaceService.AddFileExplorerTile(canvasX, canvasY);
    }

    /// <summary>Adds a System Monitor widget to the canvas at the given position.</summary>
    public void AddSystemMonitorWidget(double canvasX = 40, double canvasY = 40)
    {
        var item = _workspaceService.AddWidgetItem(WidgetType.SystemMonitor);
        item.X = canvasX;
        item.Y = canvasY;
    }

    /// <summary>Adds a Token Counter widget to the canvas at the given position.</summary>
    public void AddTokenCounterWidget(double canvasX = 40, double canvasY = 40)
    {
        var item = _workspaceService.AddWidgetItem(WidgetType.TokenCounter);
        item.X = canvasX;
        item.Y = canvasY;
    }

    /// <summary>Adds a Pomodoro Timer widget to the canvas at the given position.</summary>
    public void AddPomodoroWidget(double canvasX = 40, double canvasY = 40)
    {
        var item = _workspaceService.AddWidgetItem(WidgetType.Pomodoro);
        item.X = canvasX;
        item.Y = canvasY;
    }

    /// <summary>Adds a Process Monitor widget to the canvas at the given position.</summary>
    public void AddProcessMonitorWidget(double canvasX = 40, double canvasY = 40)
    {
        var item = _workspaceService.AddWidgetItem(WidgetType.Process);
        item.X = canvasX;
        item.Y = canvasY;
    }

    /// <summary>Adds a Browser tile to the canvas at the given position.</summary>
    public void AddBrowserWidget(double canvasX = 40, double canvasY = 40)
    {
        _workspaceService.AddBrowserTile(canvasX, canvasY);
    }

    /// <summary>Adds an Activity Feed tile to the canvas at the given position.</summary>
    public void AddActivityFeedWidget(double canvasX = 40, double canvasY = 40)
    {
        _workspaceService.AddActivityFeedTile(canvasX, canvasY);
    }

    // ─── Multi-selection ─────────────────────────────────────────────────────

    /// <summary>All currently selected canvas items (drives accent border on cards).</summary>
    public ObservableCollection<CanvasItemViewModel> SelectedItems { get; } = new();

    /// <summary>Number of selected items — used for UI bindings (badge, context menu label).</summary>
    [ObservableProperty] private int _selectedCount;

    /// <summary>
    /// Selects a single terminal, deselecting everything else.
    /// Sets it as the keyboard-receiving ActiveTerminal.
    /// </summary>
    public void SelectSingle(TerminalCanvasItemViewModel item)
    {
        ClearSelection();
        AddToSelection(item);
        ActiveTerminal = item;
        _workspaceService.BringToFront(item.Model.Id);
        _workspaceService.ActiveTerminal = item;
    }

    /// <summary>
    /// Brings any canvas item to the front (highest ZIndex) without changing active terminal.
    /// </summary>
    public void BringToFront(CanvasItemViewModel item)
    {
        _workspaceService.BringToFront(item.Model.Id);
    }

    /// <summary>
    /// Toggles the given item in/out of the selection (Ctrl+Click behaviour).
    /// ActiveTerminal follows the last toggled-on terminal.
    /// </summary>
    public void ToggleSelection(CanvasItemViewModel item)
    {
        if (item.IsSelected)
            RemoveFromSelection(item);
        else
            AddToSelection(item);

        var lastSelected = SelectedItems.OfType<TerminalCanvasItemViewModel>().LastOrDefault();
        if (lastSelected is not null)
        {
            ActiveTerminal = lastSelected;
            _workspaceService.ActiveTerminal = lastSelected;
        }
    }

    /// <summary>
    /// Deselects all items. Does NOT null out ActiveTerminal (it keeps keyboard focus).
    /// </summary>
    public void ClearSelection()
    {
        foreach (var item in SelectedItems)
            item.IsSelected = false;
        SelectedItems.Clear();
        SelectedCount = 0;
    }

    /// <summary>
    /// Selects all items returned by <paramref name="items"/>.
    /// When <paramref name="additive"/> is true, existing selection is preserved.
    /// Used by rubber-band drag selection.
    /// </summary>
    public void SelectRange(System.Collections.Generic.IEnumerable<CanvasItemViewModel> items, bool additive)
    {
        if (!additive) ClearSelection();
        foreach (var item in items)
            AddToSelection(item);
    }

    private void AddToSelection(CanvasItemViewModel item)
    {
        if (item.IsSelected) return;
        item.IsSelected = true;
        SelectedItems.Add(item);
        SelectedCount = SelectedItems.Count;
    }

    private void RemoveFromSelection(CanvasItemViewModel item)
    {
        item.IsSelected = false;
        SelectedItems.Remove(item);
        SelectedCount = SelectedItems.Count;
    }

    // ─── Public methods called from the View ─────────────────────────────────

    /// <summary>
    /// Called when the user clicks on a terminal card or sidebar item.
    /// Sets it as the active (keyboard-receiving) terminal.
    /// </summary>
    public void SetActiveTerminal(TerminalCanvasItemViewModel item)
        => SelectSingle(item);

    /// <summary>
    /// Triggers animated focus on the item (double-click or sidebar click).
    /// </summary>
    public void RequestFocus(TerminalCanvasItemViewModel item)
    {
        _cameraService.SaveSnapshot();
        IsFocusMode = true;
        FocusedItem = item;
        _workspaceService.SetFocused(item.Model.Id);
        FocusItemRequested?.Invoke(item);
    }

    /// <summary>
    /// Pops the most recent camera snapshot from the service and returns it.
    /// The View uses the returned state to animate the camera back.
    /// Returns null when the stack is empty.
    /// </summary>
    public CameraStateModel? PopCameraSnapshot() => _cameraService.PopSnapshot();

    /// <summary>
    /// Forwards View-side transform values to the camera service so subscribers stay in sync.
    /// </summary>
    public void SyncCamera(double offsetX, double offsetY, double zoom)
        => _cameraService.SyncState(offsetX, offsetY, zoom);

    /// <summary>
    /// Requests the camera to centre on a specific sidebar item without entering focus mode.
    /// </summary>
    public void CenterOnTerminal(TerminalCanvasItemViewModel item)
    {
        // The View will read the camera values from CameraService after this call
        // and apply the transform (optionally animated).
        FocusItemRequested?.Invoke(item);
    }

    // ─── Camera sync ─────────────────────────────────────────────────────────

    private void OnCameraChanged()
    {
        CameraOffsetX = _cameraService.Current.OffsetX;
        CameraOffsetY = _cameraService.Current.OffsetY;
        CameraZoom = _cameraService.Current.Zoom;
        ZoomPercent = (int)Math.Round(_cameraService.Current.Zoom * 100);

        // Keep workspace model in sync so SaveCurrentAsync() persists the real camera state
        _workspaceService.UpdateCamera(new CameraStateModel
        {
            OffsetX = _cameraService.Current.OffsetX,
            OffsetY = _cameraService.Current.OffsetY,
            Zoom = _cameraService.Current.Zoom
        });
    }

    // ─── Persistence helpers ─────────────────────────────────────────────────

    private WorkspaceModel BuildWorkspaceSnapshot()
    {
        // When saving in tiled mode, temporarily restore free-canvas positions
        // so the persisted data always reflects the free layout
        if (IsTiledMode)
        {
            foreach (var item in Items.Where(i => i.HasFreePositionStash))
            {
                item.Model.X = item.FreeX;
                item.Model.Y = item.FreeY;
                item.Model.Width = item.FreeWidth;
                item.Model.Height = item.FreeHeight;
            }
        }

        var snapshot = new WorkspaceModel
        {
            Id = CurrentProjectId ?? "default",
            Name = "Workspace",
            LayoutMode = LayoutMode,
            Camera = new CameraStateModel
            {
                OffsetX = _cameraService.Current.OffsetX,
                OffsetY = _cameraService.Current.OffsetY,
                Zoom = _cameraService.Current.Zoom
            },
            Items = Items.Select(i => i.Model).ToList()
        };

        // Restore tiled positions after snapshot
        if (IsTiledMode)
            RecalculateTiledLayout();

        return snapshot;
    }

    // ─── Partial property callbacks ──────────────────────────────────────────

    partial void OnIsFocusModeChanged(bool value)
    {
        if (!value) _workspaceService.SetFocused(null);
    }

    // ─── Wallpaper helpers ──────────────────────────────────────────────

    private void OnSettingsChanged(AppSettings settings)
    {
        ApplyWallpaperSettings(settings.CanvasWallpaperPath, settings.CanvasWallpaperOpacity, settings.CanvasWallpaperStretch);
        ZoomRequiresCtrl = settings.CanvasZoomMode != "FreeScroll";
    }

    private async Task LoadWallpaperFromSettingsAsync()
    {
        var settings = await _settingsService.GetSettingsAsync();
        ApplyWallpaperSettings(settings.CanvasWallpaperPath, settings.CanvasWallpaperOpacity, settings.CanvasWallpaperStretch);
        ZoomRequiresCtrl = settings.CanvasZoomMode != "FreeScroll";
    }

    private void ApplyWallpaperSettings(string path, double opacity, string stretchName)
    {
        WallpaperOpacity = opacity;
        WallpaperStretch = Enum.TryParse<Stretch>(stretchName, out var s) ? s : Stretch.UniformToFill;

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                WallpaperSource = bitmap;
                HasWallpaper = true;
            }
            catch
            {
                WallpaperSource = null;
                HasWallpaper = false;
            }
        }
        else
        {
            WallpaperSource = null;
            HasWallpaper = false;
        }
    }

    /// <summary>Removes any canvas item by reference.</summary>
    public void RemoveItem(CanvasItemViewModel item)
    {
        _workspaceService.RemoveItem(item.Model.Id);
    }

    /// <summary>Triggers fit-all (fires FitAllRequested → View animates).</summary>
    public void RequestFitAll() => FitAllRequested?.Invoke();

    /// <summary>Adds a Git widget at the given canvas coordinates.</summary>
    public void AddGitWidget(double canvasX = 40, double canvasY = 40)
    {
        var item = _workspaceService.AddWidgetItem(WidgetType.Git);
        item.X = canvasX;
        item.Y = canvasY;
    }

    // ─── Fase 3.5: Arrange commands ──────────────────────────────────────────

    /// <summary>Aligns all selected items to the LEFT edge of the leftmost item.</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleSelected))]
    private void AlignLeft()
    {
        if (SelectedItems.Count < 2) return;
        double minX = SelectedItems.Min(i => i.X);
        foreach (var item in SelectedItems) item.X = minX;
        _workspaceService.NotifyChanged();
    }

    /// <summary>Aligns all selected items to the RIGHT edge of the rightmost item.</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleSelected))]
    private void AlignRight()
    {
        if (SelectedItems.Count < 2) return;
        double maxRight = SelectedItems.Max(i => i.X + i.Width);
        foreach (var item in SelectedItems) item.X = maxRight - item.Width;
        _workspaceService.NotifyChanged();
    }

    /// <summary>Aligns all selected items to the TOP edge of the topmost item.</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleSelected))]
    private void AlignTop()
    {
        if (SelectedItems.Count < 2) return;
        double minY = SelectedItems.Min(i => i.Y);
        foreach (var item in SelectedItems) item.Y = minY;
        _workspaceService.NotifyChanged();
    }

    /// <summary>Aligns all selected items to the BOTTOM edge of the bottommost item.</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleSelected))]
    private void AlignBottom()
    {
        if (SelectedItems.Count < 2) return;
        double maxBottom = SelectedItems.Max(i => i.Y + i.Height);
        foreach (var item in SelectedItems) item.Y = maxBottom - item.Height;
        _workspaceService.NotifyChanged();
    }

    /// <summary>Centers all selected items horizontally relative to each other.</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleSelected))]
    private void AlignCenterH()
    {
        if (SelectedItems.Count < 2) return;
        double midY = SelectedItems.Average(i => i.Y + i.Height / 2);
        foreach (var item in SelectedItems) item.Y = midY - item.Height / 2;
        _workspaceService.NotifyChanged();
    }

    /// <summary>Centers all selected items vertically relative to each other.</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleSelected))]
    private void AlignCenterV()
    {
        if (SelectedItems.Count < 2) return;
        double midX = SelectedItems.Average(i => i.X + i.Width / 2);
        foreach (var item in SelectedItems) item.X = midX - item.Width / 2;
        _workspaceService.NotifyChanged();
    }

    /// <summary>Distributes selected items with equal horizontal spacing.</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleSelected))]
    private void DistributeH()
    {
        if (SelectedItems.Count < 3) return;
        var sorted = SelectedItems.OrderBy(i => i.X).ToList();
        double left  = sorted.First().X;
        double right = sorted.Last().X + sorted.Last().Width;
        double totalWidth = sorted.Sum(i => i.Width);
        double gap = (right - left - totalWidth) / (sorted.Count - 1);
        double cursor = left;
        foreach (var item in sorted)
        {
            item.X = cursor;
            cursor += item.Width + gap;
        }
        _workspaceService.NotifyChanged();
    }

    /// <summary>Distributes selected items with equal vertical spacing.</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleSelected))]
    private void DistributeV()
    {
        if (SelectedItems.Count < 3) return;
        var sorted = SelectedItems.OrderBy(i => i.Y).ToList();
        double top    = sorted.First().Y;
        double bottom = sorted.Last().Y + sorted.Last().Height;
        double totalHeight = sorted.Sum(i => i.Height);
        double gap = (bottom - top - totalHeight) / (sorted.Count - 1);
        double cursor = top;
        foreach (var item in sorted)
        {
            item.Y = cursor;
            cursor += item.Height + gap;
        }
        _workspaceService.NotifyChanged();
    }

    /// <summary>Makes all selected items the same width as the first selected item.</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleSelected))]
    private void EqualizeWidth()
    {
        if (SelectedItems.Count < 2) return;
        double w = SelectedItems.First().Width;
        foreach (var item in SelectedItems) item.Width = w;
        _workspaceService.NotifyChanged();
    }

    /// <summary>Makes all selected items the same height as the first selected item.</summary>
    [RelayCommand(CanExecute = nameof(HasMultipleSelected))]
    private void EqualizeHeight()
    {
        if (SelectedItems.Count < 2) return;
        double h = SelectedItems.First().Height;
        foreach (var item in SelectedItems) item.Height = h;
        _workspaceService.NotifyChanged();
    }

    private bool HasMultipleSelected() => SelectedItems.Count >= 2;

    // ─── Fase 3.4: Per-tile customization commands ───────────────────────────

    /// <summary>Sets the accent color for all selected tiles (or a specific tile).</summary>
    public void SetTileAccentColor(CanvasItemViewModel? tile, string? hex)
    {
        var targets = tile is not null
            ? new[] { tile }
            : SelectedItems.ToArray();
        foreach (var t in targets)
            t.AccentColor = hex;
        _workspaceService.NotifyChanged();
    }

    /// <summary>Sets the label for a specific tile.</summary>
    public void SetTileLabel(CanvasItemViewModel tile, string? label)
    {
        tile.TileLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        _workspaceService.NotifyChanged();
    }

    /// <summary>Toggles HideTitlebar for a specific tile.</summary>
    public void ToggleTitlebar(CanvasItemViewModel tile)
    {
        tile.HideTitlebar = !tile.HideTitlebar;
        _workspaceService.NotifyChanged();
    }

    // ─── Fase 3.3: Connection management ─────────────────────────────────────

    /// <summary>Adds a visual Bézier connection from <paramref name="source"/> to <paramref name="targetId"/>.</summary>
    public void AddConnection(CanvasItemViewModel source, string targetId)
    {
        if (!source.ConnectionTargetIds.Contains(targetId))
        {
            source.ConnectionTargetIds.Add(targetId);
            source.Model.ConnectionTargetIds.Add(targetId);
            ConnectionsChanged?.Invoke();
            _workspaceService.NotifyChanged();
        }
    }

    /// <summary>Removes a visual connection from <paramref name="source"/> to <paramref name="targetId"/>.</summary>
    public void RemoveConnection(CanvasItemViewModel source, string targetId)
    {
        source.ConnectionTargetIds.Remove(targetId);
        source.Model.ConnectionTargetIds.Remove(targetId);
        ConnectionsChanged?.Invoke();
        _workspaceService.NotifyChanged();
    }

    /// <summary>Fired whenever connections are added or removed — View redraws the Bézier overlay.</summary>
    public event Action? ConnectionsChanged;

    // ─── Tile Grouping (Fase 3.2) ─────────────────────────────────────────────

    /// <summary>All active tile groups on the canvas.</summary>
    public System.Collections.ObjectModel.ObservableCollection<TileGroup> Groups { get; } = new();

    /// <summary>Groups the currently selected items into a new named group.</summary>
    public TileGroup? GroupSelected(string label = "Grupo", string color = "#89b4fa")
    {
        if (SelectedCount < 2) return null;

        var group = new TileGroup { Label = label, Color = color };
        foreach (var item in SelectedItems)
        {
            // Remove from any existing group
            if (item.GroupId is not null)
                GetGroup(item.GroupId)?.MemberIds.Remove(item.Id);

            group.MemberIds.Add(item.Id);
            item.GroupId = group.Id;
        }
        Groups.Add(group);
        _workspaceService.NotifyChanged();
        return group;
    }

    /// <summary>Removes all selected items from their groups.</summary>
    public void UngroupSelected()
    {
        foreach (var item in SelectedItems.ToList())
        {
            if (item.GroupId is null) continue;
            var group = GetGroup(item.GroupId);
            if (group is not null)
            {
                group.MemberIds.Remove(item.Id);
                if (group.MemberIds.Count == 0)
                    Groups.Remove(group);
            }
            item.GroupId = null;
        }
        _workspaceService.NotifyChanged();
    }

    /// <summary>Returns the group with the given id, or null.</summary>
    public TileGroup? GetGroup(string groupId)
        => Groups.FirstOrDefault(g => g.Id == groupId);

    /// <summary>
    /// Returns all items belonging to the same group as <paramref name="item"/>.
    /// Used by multi-drag to move group members together.
    /// </summary>
    public IEnumerable<CanvasItemViewModel> GetGroupMembers(CanvasItemViewModel item)
    {
        if (item.GroupId is null) return Enumerable.Empty<CanvasItemViewModel>();
        return Items.Where(i => i.GroupId == item.GroupId);
    }

    // ─── Layout Templates (Fase 3.8) ─────────────────────────────────────────

    private static readonly LayoutTemplate[] BuiltInLayoutTemplates =
    [
        new() { Id = "2col", Name = "2 Colunas", Icon = "⬛⬛", IsBuiltIn = true,
            Description = "Dois terminais lado a lado (50/50).",
            Items = [
                new() { Type = CanvasItemType.Terminal, RelativeX=0, RelativeY=0, RelativeWidth=0.49, RelativeHeight=0.95 },
                new() { Type = CanvasItemType.Terminal, RelativeX=0.51, RelativeY=0, RelativeWidth=0.49, RelativeHeight=0.95 },
            ]},
        new() { Id = "3col", Name = "3 Colunas", Icon = "⬛⬛⬛", IsBuiltIn = true,
            Description = "Três terminais em colunas iguais.",
            Items = [
                new() { Type = CanvasItemType.Terminal, RelativeX=0, RelativeY=0, RelativeWidth=0.32, RelativeHeight=0.95 },
                new() { Type = CanvasItemType.Terminal, RelativeX=0.34, RelativeY=0, RelativeWidth=0.32, RelativeHeight=0.95 },
                new() { Type = CanvasItemType.Terminal, RelativeX=0.68, RelativeY=0, RelativeWidth=0.32, RelativeHeight=0.95 },
            ]},
        new() { Id = "focus_sidebar", Name = "Foco + Sidebar", Icon = "⬜◻", IsBuiltIn = true,
            Description = "Terminal grande à esquerda e Git/Chat à direita.",
            Items = [
                new() { Type = CanvasItemType.Terminal, RelativeX=0, RelativeY=0, RelativeWidth=0.65, RelativeHeight=0.95 },
                new() { Type = CanvasItemType.GitWidget, RelativeX=0.67, RelativeY=0, RelativeWidth=0.33, RelativeHeight=0.45 },
                new() { Type = CanvasItemType.ChatWidget, RelativeX=0.67, RelativeY=0.5, RelativeWidth=0.33, RelativeHeight=0.45 },
            ]},
        new() { Id = "quadrant", Name = "Quadrante", Icon = "⬛⬛\n⬛⬛", IsBuiltIn = true,
            Description = "Quatro tiles em quadrante (2x2).",
            Items = [
                new() { Type = CanvasItemType.Terminal, RelativeX=0, RelativeY=0, RelativeWidth=0.49, RelativeHeight=0.48 },
                new() { Type = CanvasItemType.Terminal, RelativeX=0.51, RelativeY=0, RelativeWidth=0.49, RelativeHeight=0.48 },
                new() { Type = CanvasItemType.ChatWidget, RelativeX=0, RelativeY=0.52, RelativeWidth=0.49, RelativeHeight=0.48 },
                new() { Type = CanvasItemType.GitWidget, RelativeX=0.51, RelativeY=0.52, RelativeWidth=0.49, RelativeHeight=0.48 },
            ]},
        new() { Id = "dev_dashboard", Name = "Dev Dashboard", Icon = "📐", IsBuiltIn = true,
            Description = "Terminal principal com Git, Monitor e Chat.",
            Items = [
                new() { Type = CanvasItemType.Terminal, RelativeX=0, RelativeY=0, RelativeWidth=0.65, RelativeHeight=0.65 },
                new() { Type = CanvasItemType.GitWidget, RelativeX=0.67, RelativeY=0, RelativeWidth=0.33, RelativeHeight=0.32 },
                new() { Type = CanvasItemType.SystemMonitorWidget, RelativeX=0.67, RelativeY=0.34, RelativeWidth=0.33, RelativeHeight=0.31 },
                new() { Type = CanvasItemType.ChatWidget, RelativeX=0, RelativeY=0.67, RelativeWidth=0.65, RelativeHeight=0.33 },
                new() { Type = CanvasItemType.ActivityFeedWidget, RelativeX=0.67, RelativeY=0.67, RelativeWidth=0.33, RelativeHeight=0.33 },
            ]},
    ];

    public IReadOnlyList<LayoutTemplate> LayoutTemplates => BuiltInLayoutTemplates;

    /// <summary>
    /// Applies a layout template to the current canvas.
    /// Creates new tile items positioned according to the template's relative coordinates,
    /// scaled to the current viewport size.
    /// </summary>
    public void ApplyLayoutTemplate(LayoutTemplate template)
    {
        if (ViewportWidth <= 0 || ViewportHeight <= 0) return;

        foreach (var entry in template.Items)
        {
            double x = entry.RelativeX * ViewportWidth;
            double y = entry.RelativeY * ViewportHeight;
            double w = entry.RelativeWidth * ViewportWidth;
            double h = entry.RelativeHeight * ViewportHeight;

            switch (entry.Type)
            {
                case CanvasItemType.Terminal:
                    // Adding a terminal requires user interaction — skip in template apply
                    break;
                case CanvasItemType.ChatWidget:
                    var chat = _workspaceService.AddChatTile(x, y);
                    chat.Width = w; chat.Height = h;
                    break;
                case CanvasItemType.CodeEditorWidget:
                    var editor = _workspaceService.AddCodeEditorTile(x, y);
                    editor.Width = w; editor.Height = h;
                    break;
                case CanvasItemType.FileExplorerWidget:
                    var fe = _workspaceService.AddFileExplorerTile(x, y);
                    fe.Width = w; fe.Height = h;
                    break;
                case CanvasItemType.BrowserWidget:
                    var br = _workspaceService.AddBrowserTile(x, y);
                    br.Width = w; br.Height = h;
                    break;
                case CanvasItemType.ActivityFeedWidget:
                    var af = _workspaceService.AddActivityFeedTile(x, y);
                    af.Width = w; af.Height = h;
                    break;
                default:
                    var widgetType = entry.Type switch
                    {
                        CanvasItemType.GitWidget      => WidgetType.Git,
                        CanvasItemType.ProcessWidget  => WidgetType.Process,
                        CanvasItemType.NoteWidget     => WidgetType.Note,
                        CanvasItemType.SystemMonitorWidget => WidgetType.SystemMonitor,
                        CanvasItemType.KanbanWidget   => WidgetType.Kanban,
                        CanvasItemType.PomodoroWidget => WidgetType.Pomodoro,
                        CanvasItemType.TokenCounterWidget => WidgetType.TokenCounter,
                        _ => (WidgetType?)null
                    };
                    if (widgetType.HasValue)
                    {
                        var wgt = _workspaceService.AddWidgetItem(widgetType.Value);
                        wgt.X = x; wgt.Y = y; wgt.Width = w; wgt.Height = h;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Saves the current canvas layout as a new template.
    /// </summary>
    public LayoutTemplate SaveCurrentAsTemplate(string name, string description = "")
    {
        var vw = ViewportWidth > 0 ? ViewportWidth : 1200;
        var vh = ViewportHeight > 0 ? ViewportHeight : 800;

        var items = Items.Select(i => new LayoutTemplateItem
        {
            Type = i.ItemType,
            RelativeX = i.X / vw,
            RelativeY = i.Y / vh,
            RelativeWidth = i.Width / vw,
            RelativeHeight = i.Height / vh
        }).ToList();

        return new LayoutTemplate
        {
            Name = name,
            Description = description,
            Items = items
        };
    }
}

