using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// Base ViewModel for any item placed on the spatial canvas (terminal, widget, etc.).
/// Keeps X/Y/Width/Height/ZIndex as observable properties and syncs them back
/// to the underlying <see cref="CanvasItemModel"/> for serialization.
/// </summary>
public abstract partial class CanvasItemViewModel : ObservableObject
{
    /// <summary>Underlying serializable model — synced on every property change.</summary>
    public CanvasItemModel Model { get; }

    /// <summary>Convenience shortcut for Model.Id, used by WSL modules.</summary>
    public string Id => Model.Id;

    /// <summary>Runtime metadata dictionary, used by WSL modules for cross-referencing.</summary>
    public Dictionary<string, object> Metadata { get; } = new();

    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width;
    [ObservableProperty] private double _height;
    [ObservableProperty] private int _zIndex;
    [ObservableProperty] private bool _isFocused;
    [ObservableProperty] private bool _isSelected;

    // ─── Free-canvas position stash (used to restore when leaving tiled mode) ──

    /// <summary>Stashed free-canvas X position.</summary>
    public double FreeX { get; set; }

    /// <summary>Stashed free-canvas Y position.</summary>
    public double FreeY { get; set; }

    /// <summary>Stashed free-canvas width.</summary>
    public double FreeWidth { get; set; }

    /// <summary>Stashed free-canvas height.</summary>
    public double FreeHeight { get; set; }

    /// <summary>Whether the free-canvas position has been stashed.</summary>
    public bool HasFreePositionStash { get; set; }

    /// <summary>Saves current X/Y/Width/Height as the free-canvas positions.</summary>
    public void StashFreePosition()
    {
        FreeX = X;
        FreeY = Y;
        FreeWidth = Width;
        FreeHeight = Height;
        HasFreePositionStash = true;
    }

    /// <summary>Restores X/Y/Width/Height from the stashed free-canvas positions.</summary>
    public void RestoreFreePosition()
    {
        if (!HasFreePositionStash) return;
        X = FreeX;
        Y = FreeY;
        Width = FreeWidth;
        Height = FreeHeight;
        HasFreePositionStash = false;
    }

    protected CanvasItemViewModel(CanvasItemModel model)
    {
        Model = model;
        _x = model.X;
        _y = model.Y;
        _width = model.Width;
        _height = model.Height;
        _zIndex = model.ZIndex;
    }

    // ─── Sync VM → Model (keeps model ready for JSON serialization at any time) ──

    partial void OnXChanged(double value) => Model.X = value;
    partial void OnYChanged(double value) => Model.Y = value;
    partial void OnWidthChanged(double value) => Model.Width = value;
    partial void OnHeightChanged(double value) => Model.Height = value;
    partial void OnZIndexChanged(int value) => Model.ZIndex = value;

    public abstract CanvasItemType ItemType { get; }
}
