using System;
using System.Collections.Generic;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Manages the spatial canvas camera: pan offset, zoom level,
/// and utility operations (center on item, fit all, snapshot/restore).
/// </summary>
public interface ICanvasCameraService
{
    /// <summary>Current camera state (read-only snapshot; changes via the methods below).</summary>
    CameraStateModel Current { get; }

    /// <summary>Pan by a delta in world coordinates.</summary>
    void Pan(double deltaX, double deltaY);

    /// <summary>
    /// Zoom centred on a point in world space.
    /// <paramref name="zoomDelta"/> is a signed step (e.g. +0.12 or -0.12).
    /// </summary>
    void ZoomToPoint(double worldX, double worldY, double zoomDelta);

    /// <summary>Animate (or snap) the camera so the given item is centred in the viewport.</summary>
    void CenterOnItem(CanvasItemModel item, double viewportWidth, double viewportHeight);

    /// <summary>Pan by a delta in screen-space pixels (wrapper over Pan).</summary>
    void PanBy(double deltaX, double deltaY);

    /// <summary>
    /// Compute camera state that centres the item in the viewport at the current zoom level.
    /// Does NOT mutate state — the View executes animation using the returned state.
    /// </summary>
    CameraStateModel ComputeCenterOnItem(CanvasItemModel item, double vpW, double vpH);

    /// <summary>
    /// Compute camera state that scales the item to fill 90 % of the viewport.
    /// Does NOT mutate state — the View executes animation using the returned state.
    /// </summary>
    CameraStateModel ComputeFocusItem(CanvasItemModel item, double vpW, double vpH);

    /// <summary>
    /// Compute camera state that fits all items inside the viewport with optional padding.
    /// Does NOT animate — the View handles animation using the returned state.
    /// </summary>
    CameraStateModel ComputeFitAll(IEnumerable<CanvasItemModel> items,
                                    double viewportWidth, double viewportHeight,
                                    double padding = 80);

    /// <summary>Push the current camera state onto an internal stack (max 10 entries).</summary>
    void SaveSnapshot();

    /// <summary>Pop and restore the most recently saved snapshot.</summary>
    void RestoreSnapshot();

    /// <summary>
    /// Pop the most recently saved snapshot and return it WITHOUT applying it to Current.
    /// Returns null if the stack is empty.
    /// </summary>
    CameraStateModel? PopSnapshot();

    /// <summary>
    /// Synchronises Current with the transform values maintained directly by the View.
    /// Fires CameraChanged so subscribers (e.g. MiniMapViewModel) receive the update.
    /// </summary>
    void SyncState(double offsetX, double offsetY, double zoom);

    /// <summary>Fired whenever any camera property changes.</summary>
    event Action? CameraChanged;
}
