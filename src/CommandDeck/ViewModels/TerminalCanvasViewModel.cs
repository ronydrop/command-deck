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
    private readonly TiledLayoutStrategy _tiledStrategy;
    private readonly FreeCanvasLayoutStrategy _freeStrategy;
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

    /// <summary>Viewport width in pixels (fed from View SizeChanged).</summary>
    [ObservableProperty] private double _viewportWidth;

    /// <summary>Viewport height in pixels (fed from View SizeChanged).</summary>
    [ObservableProperty] private double _viewportHeight;

    /// <summary>View should apply tiled positions (event carries the placement list).</summary>
    public event Action<LayoutMode>? LayoutModeChanged;

    // ─── Canvas zoom mode ─────────────────────────────────────────────

    [ObservableProperty] private bool _zoomRequiresCtrl = true;

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
        TiledLayoutStrategy tiledStrategy,
        FreeCanvasLayoutStrategy freeStrategy,
        MiniMapViewModel miniMap)
    {
        _workspaceService = workspaceService;
        _cameraService = cameraService;
        _persistenceService = persistenceService;
        _paneStateService = paneStateService;
        _aiAgentStateService = aiAgentStateService;
        _settingsService = settingsService;
        _tiledStrategy = tiledStrategy;
        _freeStrategy = freeStrategy;

        MiniMap = miniMap;

        _cameraService.CameraChanged += OnCameraChanged;

        // Propagate pane state changes to the matching canvas item
        _paneStateService.StateChanged += OnPaneStateChanged;

        // Propagate AI agent state changes to the matching canvas item
        _aiAgentStateService.StateChanged += OnAiAgentStateChanged;

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

    /// <summary>Called from View when the viewport size changes.</summary>
    public void OnViewportSizeChanged(double width, double height)
    {
        ViewportWidth = width;
        ViewportHeight = height;
        ScheduleTiledLayoutRecalculation();
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
        double vpH = ViewportHeight > 0 ? ViewportHeight : 800;

        var layout = _freeStrategy.CalculateLayout(allItems.Count, vpW, vpH);

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
        ScheduleTiledLayoutRecalculation();
    }

    // ─── Layout mode partial callbacks ──────────────────────────────────────

    partial void OnLayoutModeChanged(LayoutMode oldValue, LayoutMode newValue)
    {
        OnPropertyChanged(nameof(IsCanvasMode));
        OnPropertyChanged(nameof(IsTiledMode));

        if (newValue == LayoutMode.Tiled)
        {
            // Exit focus mode if active
            if (IsFocusMode) ExitFocusMode();

            // Save camera state for return
            _cameraService.SaveSnapshot();

            // Stash free-canvas positions so they can be restored later
            foreach (var item in Items)
                item.StashFreePosition();

            // Calculate and apply tiled positions
            RecalculateTiledLayout();
        }
        else // Returning to FreeCanvas
        {
            // Restore the positions the user had before switching to tiled
            foreach (var item in Items)
                item.RestoreFreePosition();

            // Restore widgets that were hidden in tiled mode
            foreach (var widget in _workspaceService.Items.OfType<WidgetCanvasItemViewModel>())
            {
                if (widget.HasFreePositionStash)
                    widget.RestoreFreePosition();
            }
        }

        LayoutModeChanged?.Invoke(newValue);
    }

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

    // ─── Public methods called from the View ─────────────────────────────────

    /// <summary>
    /// Called when the user clicks on a terminal card or sidebar item.
    /// Sets it as the active (keyboard-receiving) terminal.
    /// </summary>
    public void SetActiveTerminal(TerminalCanvasItemViewModel item)
    {
        if (ActiveTerminal is not null)
            ActiveTerminal.IsSelected = false;

        ActiveTerminal = item;
        item.IsSelected = true;
        _workspaceService.BringToFront(item.Model.Id);
        _workspaceService.ActiveTerminal = item;
    }

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
}
