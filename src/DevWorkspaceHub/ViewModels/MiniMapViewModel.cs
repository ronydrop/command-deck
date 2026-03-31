using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.Services;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// ViewModel for the spatial canvas mini-map overlay.
///
/// Responsibilities:
///   - Tracks all canvas items and the camera state.
///   - Computes the world bounding-box and projects everything into mini-map pixels.
///   - Exposes a flat <see cref="ItemRects"/> collection for XAML binding.
///   - Exposes the current viewport rectangle in mini-map space.
///   - Allows the user to click on the mini-map to re-centre the camera.
/// </summary>
public partial class MiniMapViewModel : ObservableObject
{
    // ─── Dependencies ────────────────────────────────────────────────────────

    private readonly IWorkspaceService _workspaceService;
    private readonly ICanvasCameraService _cameraService;

    // ─── Mini-map dimensions (fixed, matches XAML canvas size) ───────────────

    [ObservableProperty] private double _miniMapWidth  = 200;
    [ObservableProperty] private double _miniMapHeight = 140;

    // ─── World bounding-box ──────────────────────────────────────────────────

    [ObservableProperty] private double _worldMinX;
    [ObservableProperty] private double _worldMinY;
    [ObservableProperty] private double _worldWidth  = 1;   // never 0 (avoids divide-by-zero)
    [ObservableProperty] private double _worldHeight = 1;

    // ─── Mapped item rects ───────────────────────────────────────────────────

    /// <summary>All canvas items mapped to mini-map pixel coordinates.</summary>
    public ObservableCollection<MiniMapItemRect> ItemRects { get; } = new();

    // ─── Viewport rectangle in mini-map space ────────────────────────────────

    /// <summary>Left edge of the viewport rect inside the mini-map.</summary>
    [ObservableProperty] private double _viewportRectX;

    /// <summary>Top edge of the viewport rect inside the mini-map.</summary>
    [ObservableProperty] private double _viewportRectY;

    /// <summary>Width of the viewport rect inside the mini-map.</summary>
    [ObservableProperty] private double _viewportRectW;

    /// <summary>Height of the viewport rect inside the mini-map.</summary>
    [ObservableProperty] private double _viewportRectH;

    // ─── Padding added around the world bounding-box ─────────────────────────

    private const double WorldPadding = 80;

    // ─── Constructor ─────────────────────────────────────────────────────────

    public MiniMapViewModel(IWorkspaceService workspaceService, ICanvasCameraService cameraService)
    {
        _workspaceService = workspaceService;
        _cameraService    = cameraService;

        // Listen for camera moves / zooms
        _cameraService.CameraChanged += OnCameraChanged;

        // Listen for items being added / removed
        _workspaceService.Items.CollectionChanged += OnItemsCollectionChanged;

        // Also subscribe to each existing item's position/size changes
        foreach (var item in _workspaceService.Items)
            SubscribeToItem(item);

        Update();
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Recalculates the world bounding-box, all item rects, and the viewport rectangle.
    /// Called automatically when the camera or item collection changes.
    /// Can also be called manually (e.g., after a resize).
    /// </summary>
    public void Update()
    {
        RecalculateBoundingBox();
        RecalculateItemRects();
        // Viewport update needs external viewport size; use last known values.
        // The View calls UpdateViewport() after resize events.
        UpdateViewportRect(_lastViewportW, _lastViewportH);
    }

    // Last-known viewport dimensions (supplied by the View via UpdateViewport).
    private double _lastViewportW = 800;
    private double _lastViewportH = 600;

    /// <summary>
    /// Updates the viewport rect in mini-map space.
    /// Called from the View (MiniMapControl) whenever the viewport size changes or the control loads.
    /// </summary>
    public void UpdateViewport(double viewportW, double viewportH)
    {
        _lastViewportW = viewportW;
        _lastViewportH = viewportH;
        UpdateViewportRect(viewportW, viewportH);
    }

    /// <summary>
    /// Translates a click at mini-map coordinates (mmX, mmY) into a camera pan
    /// that centres the world point under the click in the viewport.
    /// </summary>
    public void HandleMiniMapClick(double mmX, double mmY, double viewportW, double viewportH)
    {
        double scaleX = SafeScale(MiniMapWidth,  WorldWidth);
        double scaleY = SafeScale(MiniMapHeight, WorldHeight);

        // Map mini-map pixel → world coordinate
        double worldX = mmX / scaleX + WorldMinX;
        double worldY = mmY / scaleY + WorldMinY;

        double zoom = _cameraService.Current.Zoom;

        // Centre viewport on that world point:
        // offsetX = vpW/2 - worldX * zoom
        double newOffsetX = viewportW / 2.0 - worldX * zoom;
        double newOffsetY = viewportH / 2.0 - worldY * zoom;

        // Apply via Pan from current offset
        double deltaX = newOffsetX - _cameraService.Current.OffsetX;
        double deltaY = newOffsetY - _cameraService.Current.OffsetY;
        _cameraService.Pan(deltaX, deltaY);
    }

    // ─── Internal recalculation ───────────────────────────────────────────────

    private void RecalculateBoundingBox()
    {
        var items = _workspaceService.Items;

        double minX, minY, maxX, maxY;

        if (items.Count == 0)
        {
            minX = 0; minY = 0; maxX = 1; maxY = 1;
        }
        else
        {
            minX = items.Min(i => i.X);
            minY = items.Min(i => i.Y);
            maxX = items.Max(i => i.X + i.Width);
            maxY = items.Max(i => i.Y + i.Height);
        }

        // Include the visible viewport region so that zooming out
        // on the canvas also shrinks items in the mini-map.
        double zoom    = _cameraService.Current.Zoom;
        double offsetX = _cameraService.Current.OffsetX;
        double offsetY = _cameraService.Current.OffsetY;

        if (zoom > 0)
        {
            double vpWorldX = -offsetX / zoom;
            double vpWorldY = -offsetY / zoom;
            double vpWorldR = vpWorldX + _lastViewportW / zoom;
            double vpWorldB = vpWorldY + _lastViewportH / zoom;

            minX = Math.Min(minX, vpWorldX);
            minY = Math.Min(minY, vpWorldY);
            maxX = Math.Max(maxX, vpWorldR);
            maxY = Math.Max(maxY, vpWorldB);
        }

        // Add padding so items at the very edge are not clipped in the mini-map
        WorldMinX   = minX - WorldPadding;
        WorldMinY   = minY - WorldPadding;
        WorldWidth  = Math.Max(1, maxX - minX + WorldPadding * 2);
        WorldHeight = Math.Max(1, maxY - minY + WorldPadding * 2);

        // Enforce a minimum world size so items never dominate the mini-map
        const double MinWorldSize = 2000;
        if (WorldWidth < MinWorldSize)
        {
            double cx = WorldMinX + WorldWidth / 2;
            WorldMinX  = cx - MinWorldSize / 2;
            WorldWidth = MinWorldSize;
        }
        if (WorldHeight < MinWorldSize)
        {
            double cy = WorldMinY + WorldHeight / 2;
            WorldMinY   = cy - MinWorldSize / 2;
            WorldHeight = MinWorldSize;
        }
    }

    private void RecalculateItemRects()
    {
        double scaleX = SafeScale(MiniMapWidth,  WorldWidth);
        double scaleY = SafeScale(MiniMapHeight, WorldHeight);

        ItemRects.Clear();

        foreach (var item in _workspaceService.Items)
        {
            double mmX = (item.X - WorldMinX) * scaleX;
            double mmY = (item.Y - WorldMinY) * scaleY;
            double mmW = Math.Max(2, item.Width  * scaleX);
            double mmH = Math.Max(2, item.Height * scaleY);

            var isAi = item is TerminalCanvasItemViewModel tci && tci.IsAiSession;

            ItemRects.Add(new MiniMapItemRect
            {
                X          = mmX,
                Y          = mmY,
                Width      = mmW,
                Height     = mmH,
                IsTerminal = item.ItemType == CanvasItemType.Terminal,
                IsAiSession = isAi,
                IsSelected = item.IsSelected
            });
        }
    }

    private void UpdateViewportRect(double viewportW, double viewportH)
    {
        double zoom    = _cameraService.Current.Zoom;
        double offsetX = _cameraService.Current.OffsetX;
        double offsetY = _cameraService.Current.OffsetY;

        double scaleX = SafeScale(MiniMapWidth,  WorldWidth);
        double scaleY = SafeScale(MiniMapHeight, WorldHeight);

        // World region visible in the viewport
        // viewport_px = world_px * zoom + offset  =>  world_px = (viewport_px - offset) / zoom
        double vpWorldX = -offsetX / zoom;
        double vpWorldY = -offsetY / zoom;
        double vpWorldW = (zoom > 0) ? viewportW / zoom : viewportW;
        double vpWorldH = (zoom > 0) ? viewportH / zoom : viewportH;

        // Map to mini-map pixels
        ViewportRectX = (vpWorldX - WorldMinX) * scaleX;
        ViewportRectY = (vpWorldY - WorldMinY) * scaleY;
        ViewportRectW = Math.Max(4, vpWorldW * scaleX);
        ViewportRectH = Math.Max(4, vpWorldH * scaleY);
    }

    // ─── Event handlers ───────────────────────────────────────────────────────

    private void OnCameraChanged()
    {
        // Full recalc: zoom changes affect the world bounding-box (viewport region included)
        Update();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Subscribe/unsubscribe from new/removed items so we catch drag & resize
        if (e.NewItems is not null)
        {
            foreach (CanvasItemViewModel item in e.NewItems)
                SubscribeToItem(item);
        }

        if (e.OldItems is not null)
        {
            foreach (CanvasItemViewModel item in e.OldItems)
                UnsubscribeFromItem(item);
        }

        Update();
    }

    private void SubscribeToItem(CanvasItemViewModel item)
    {
        item.PropertyChanged += OnItemPropertyChanged;
    }

    private void UnsubscribeFromItem(CanvasItemViewModel item)
    {
        item.PropertyChanged -= OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Only re-compute when spatial properties change (avoid unnecessary work on every keystroke)
        if (e.PropertyName is nameof(CanvasItemViewModel.X)
                           or nameof(CanvasItemViewModel.Y)
                           or nameof(CanvasItemViewModel.Width)
                           or nameof(CanvasItemViewModel.Height)
                           or nameof(CanvasItemViewModel.IsSelected))
        {
            Update();
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Returns mapSize/worldSize, guarding against divide-by-zero.</summary>
    private static double SafeScale(double mapSize, double worldSize)
        => worldSize > 0 ? mapSize / worldSize : 1.0;
}
