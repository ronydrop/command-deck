using CommandDeck.Services;
using CommandDeck.ViewModels;

namespace CommandDeck.Models;

// ─── Move ─────────────────────────────────────────────────────────────────────

/// <summary>Records a canvas item move operation.</summary>
public sealed class MoveItemCommand : IUndoableCommand
{
    private readonly CanvasItemViewModel _item;
    private readonly double _oldX, _oldY, _newX, _newY;

    public string Description => $"Mover {_item.Id}";

    public MoveItemCommand(CanvasItemViewModel item,
                           double oldX, double oldY,
                           double newX, double newY)
    {
        _item = item;
        _oldX = oldX; _oldY = oldY;
        _newX = newX; _newY = newY;
    }

    public void Execute() { _item.X = _newX; _item.Y = _newY; }
    public void Undo()    { _item.X = _oldX; _item.Y = _oldY; }
}

// ─── Resize ───────────────────────────────────────────────────────────────────

/// <summary>Records a canvas item resize operation.</summary>
public sealed class ResizeItemCommand : IUndoableCommand
{
    private readonly CanvasItemViewModel _item;
    private readonly double _oldW, _oldH, _newW, _newH;

    public string Description => $"Redimensionar {_item.Id}";

    public ResizeItemCommand(CanvasItemViewModel item,
                             double oldW, double oldH,
                             double newW, double newH)
    {
        _item = item;
        _oldW = oldW; _oldH = oldH;
        _newW = newW; _newH = newH;
    }

    public void Execute() { _item.Width = _newW; _item.Height = _newH; }
    public void Undo()    { _item.Width = _oldW; _item.Height = _oldH; }
}

// ─── Add ──────────────────────────────────────────────────────────────────────

/// <summary>Records adding a canvas item.</summary>
public sealed class AddItemCommand : IUndoableCommand
{
    private readonly ICanvasItemsService _canvas;
    private readonly CanvasItemViewModel _item;

    public string Description => "Adicionar item";

    public AddItemCommand(ICanvasItemsService canvas, CanvasItemViewModel item)
    {
        _canvas = canvas;
        _item   = item;
    }

    // Re-add uses AddRestoredItem to bypass position recalculation
    public void Execute() => _canvas.AddRestoredItem(_item);
    public void Undo()    => _canvas.RemoveItem(_item.Id);
}

// ─── Remove ───────────────────────────────────────────────────────────────────

/// <summary>Records removing a canvas item.</summary>
public sealed class RemoveItemCommand : IUndoableCommand
{
    private readonly ICanvasItemsService _canvas;
    private readonly CanvasItemViewModel _item;

    public string Description => "Remover item";

    public RemoveItemCommand(ICanvasItemsService canvas, CanvasItemViewModel item)
    {
        _canvas = canvas;
        _item   = item;
    }

    public void Execute() => _canvas.RemoveItem(_item.Id);
    // Undo restores the item at its original position without recalculating layout
    public void Undo()    => _canvas.AddRestoredItem(_item);
}
