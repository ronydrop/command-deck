using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommandDeck.Models;

namespace CommandDeck.ViewModels;

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
    [ObservableProperty] private bool _isTiledMode;

    // ─── Tile customization (Fase 3.4) ───────────────────────────────────────

    /// <summary>Custom accent color hex. Null = use theme default accent.</summary>
    [ObservableProperty] private string? _accentColor;

    /// <summary>Custom label shown in the titlebar. Empty = use default item title.</summary>
    [ObservableProperty] private string? _tileLabel;

    /// <summary>Whether to hide the titlebar entirely (content fills the whole card).</summary>
    [ObservableProperty] private bool _hideTitlebar;

    /// <summary>Corner radius override. -1 = use theme default.</summary>
    [ObservableProperty] private double _tileBorderRadius = -1;

    // ─── Connection targets (Fase 3.3) ───────────────────────────────────────

    /// <summary>IDs of tiles this tile is visually connected to via Bézier lines.</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> ConnectionTargetIds { get; }
        = new();

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

        // Restore customization from model
        _accentColor = model.AccentColor;
        _tileLabel = model.TileLabel;
        _hideTitlebar = model.HideTitlebar;
        _tileBorderRadius = model.TileBorderRadius;

        foreach (var id in model.ConnectionTargetIds)
            ConnectionTargetIds.Add(id);
    }

    // ─── Sync VM → Model ─────────────────────────────────────────────────────

    partial void OnXChanged(double value) => Model.X = value;
    partial void OnYChanged(double value) => Model.Y = value;
    partial void OnWidthChanged(double value) => Model.Width = value;
    partial void OnHeightChanged(double value) => Model.Height = value;
    partial void OnZIndexChanged(int value) => Model.ZIndex = value;
    partial void OnAccentColorChanged(string? value) => Model.AccentColor = value;
    partial void OnTileLabelChanged(string? value) => Model.TileLabel = value;
    partial void OnHideTitlebarChanged(bool value) => Model.HideTitlebar = value;
    partial void OnTileBorderRadiusChanged(double value) => Model.TileBorderRadius = value;

    public abstract CanvasItemType ItemType { get; }

    /// <summary>Human-readable title shown in the sidebar block list.</summary>
    public virtual string DisplayTitle => ItemType switch
    {
        CanvasItemType.Terminal          => "Terminal",
        CanvasItemType.ChatWidget        => "Chat IA",
        CanvasItemType.CodeEditorWidget  => "Editor de Código",
        CanvasItemType.BrowserWidget     => "Browser",
        CanvasItemType.NoteWidget        => "Nota",
        CanvasItemType.GitWidget         => "Git Status",
        CanvasItemType.KanbanWidget      => "Kanban",
        CanvasItemType.ProcessWidget     => "Processos",
        CanvasItemType.SystemMonitorWidget => "Monitor",
        CanvasItemType.FileExplorerWidget  => "Explorador",
        CanvasItemType.ActivityFeedWidget  => "Feed",
        CanvasItemType.ImageWidget         => "Imagem",
        CanvasItemType.TokenCounterWidget  => "Token Counter",
        CanvasItemType.PomodoroWidget      => "Pomodoro",
        _                                  => ItemType.ToString()
    };

    /// <summary>
    /// When true the item's container applies an inverse ScaleTransform so it
    /// keeps its original on-screen size regardless of canvas zoom level.
    /// Useful for HwndHost-based controls (browser) that don't benefit from zoom.
    /// </summary>
    public virtual bool IsZoomImmune => false;
}
