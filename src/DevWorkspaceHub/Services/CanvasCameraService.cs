using System;
using System.Collections.Generic;
using System.Linq;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <inheritdoc />
public class CanvasCameraService : ICanvasCameraService
{
    private const double MinZoom = 0.25;
    private const double MaxZoom = 2.0;
    private const int MaxSnapshots = 10;

    private readonly Stack<CameraStateModel> _snapshots = new();

    public CameraStateModel Current { get; } = new();

    public event Action? CameraChanged;

    // ─── Pan ────────────────────────────────────────────────────────────────────

    public void Pan(double deltaX, double deltaY)
    {
        Current.OffsetX += deltaX;
        Current.OffsetY += deltaY;
        CameraChanged?.Invoke();
    }

    public void PanBy(double deltaX, double deltaY) => Pan(deltaX, deltaY);

    // ─── Zoom ───────────────────────────────────────────────────────────────────

    public void ZoomToPoint(double worldX, double worldY, double zoomDelta)
    {
        double oldZoom = Current.Zoom;
        double newZoom = Math.Clamp(oldZoom + zoomDelta, MinZoom, MaxZoom);

        if (Math.Abs(newZoom - oldZoom) < 1e-6) return;

        // Keep the point under the cursor stationary:
        // viewportPoint = worldPoint * zoom + offset
        // => after zoom change: offset_new = viewportPoint - worldPoint * zoom_new
        // But here worldX/worldY are already in world space (before transform),
        // so the viewport position of that point is: vx = worldX * oldZoom + offsetX
        // We want: vx = worldX * newZoom + offsetX_new
        double ratio = newZoom / oldZoom;
        Current.OffsetX = worldX * (1 - ratio) + Current.OffsetX;
        Current.OffsetY = worldY * (1 - ratio) + Current.OffsetY;
        Current.Zoom = newZoom;

        CameraChanged?.Invoke();
    }

    // ─── Center on item ─────────────────────────────────────────────────────────

    public void CenterOnItem(CanvasItemModel item, double viewportWidth, double viewportHeight)
    {
        // Item centre in world coords
        double itemCX = item.X + item.Width / 2.0;
        double itemCY = item.Y + item.Height / 2.0;

        // Offset so item centre lands at viewport centre:
        // viewportCX = itemCX * zoom + offsetX  =>  offsetX = viewportCX - itemCX * zoom
        Current.OffsetX = viewportWidth / 2.0 - itemCX * Current.Zoom;
        Current.OffsetY = viewportHeight / 2.0 - itemCY * Current.Zoom;

        CameraChanged?.Invoke();
    }

    public CameraStateModel ComputeCenterOnItem(CanvasItemModel item, double vpW, double vpH)
    {
        double itemCX = item.X + item.Width  / 2.0;
        double itemCY = item.Y + item.Height / 2.0;
        return new CameraStateModel
        {
            Zoom    = Current.Zoom,
            OffsetX = vpW / 2.0 - itemCX * Current.Zoom,
            OffsetY = vpH / 2.0 - itemCY * Current.Zoom
        };
    }

    public CameraStateModel ComputeFocusItem(CanvasItemModel item, double vpW, double vpH)
    {
        double scaleX = (vpW * 0.90) / item.Width;
        double scaleY = (vpH * 0.90) / item.Height;
        double zoom   = Math.Clamp(Math.Min(scaleX, scaleY), MinZoom, MaxZoom);

        double itemCX = item.X + item.Width  / 2.0;
        double itemCY = item.Y + item.Height / 2.0;
        return new CameraStateModel
        {
            Zoom    = zoom,
            OffsetX = vpW / 2.0 - itemCX * zoom,
            OffsetY = vpH / 2.0 - itemCY * zoom
        };
    }

    // ─── Fit all ────────────────────────────────────────────────────────────────

    public CameraStateModel ComputeFitAll(IEnumerable<CanvasItemModel> items,
                                           double viewportWidth, double viewportHeight,
                                           double padding = 80)
    {
        var list = items.ToList();
        if (list.Count == 0)
            return new CameraStateModel { OffsetX = 0, OffsetY = 0, Zoom = 1 };

        double minX = list.Min(i => i.X);
        double minY = list.Min(i => i.Y);
        double maxX = list.Max(i => i.X + i.Width);
        double maxY = list.Max(i => i.Y + i.Height);

        double contentW = maxX - minX;
        double contentH = maxY - minY;

        double availW = viewportWidth - padding * 2;
        double availH = viewportHeight - padding * 2;

        double scaleX = availW / contentW;
        double scaleY = availH / contentH;
        double zoom = Math.Clamp(Math.Min(scaleX, scaleY), MinZoom, MaxZoom);

        // Centre the bounding box in the viewport
        double offsetX = (viewportWidth - contentW * zoom) / 2.0 - minX * zoom;
        double offsetY = (viewportHeight - contentH * zoom) / 2.0 - minY * zoom;

        return new CameraStateModel { OffsetX = offsetX, OffsetY = offsetY, Zoom = zoom };
    }

    // ─── Snapshot ───────────────────────────────────────────────────────────────

    public void SaveSnapshot()
    {
        if (_snapshots.Count >= MaxSnapshots) return;
        _snapshots.Push(new CameraStateModel
        {
            OffsetX = Current.OffsetX,
            OffsetY = Current.OffsetY,
            Zoom = Current.Zoom
        });
    }

    public void RestoreSnapshot()
    {
        if (!_snapshots.TryPop(out var snap)) return;
        Current.OffsetX = snap.OffsetX;
        Current.OffsetY = snap.OffsetY;
        Current.Zoom = snap.Zoom;
        CameraChanged?.Invoke();
    }

    public CameraStateModel? PopSnapshot()
        => _snapshots.TryPop(out var snap) ? snap : null;

    public void SyncState(double offsetX, double offsetY, double zoom)
    {
        Current.OffsetX = offsetX;
        Current.OffsetY = offsetY;
        Current.Zoom    = zoom;
        CameraChanged?.Invoke();
    }
}
