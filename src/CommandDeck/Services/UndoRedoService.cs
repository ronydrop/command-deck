namespace CommandDeck.Services;

/// <summary>
/// Thread-safe (UI-thread-only) undo/redo stack with a configurable maximum history size.
/// </summary>
public sealed class UndoRedoService : IUndoRedoService
{
    private const int MaxStackSize = 100;

    private readonly Stack<IUndoableCommand> _undoStack = new();
    private readonly Stack<IUndoableCommand> _redoStack = new();

    /// <inheritdoc/>
    public bool CanUndo => _undoStack.Count > 0;

    /// <inheritdoc/>
    public bool CanRedo => _redoStack.Count > 0;

    /// <inheritdoc/>
    public event Action? StateChanged;

    /// <inheritdoc/>
    public void Record(IUndoableCommand command)
    {
        // A new action invalidates any previously undone operations
        _redoStack.Clear();
        _undoStack.Push(command);

        // Trim the oldest entry when the stack exceeds the maximum size
        if (_undoStack.Count > MaxStackSize)
        {
            var items = _undoStack.ToArray(); // newest-first
            _undoStack.Clear();
            foreach (var item in items.Take(MaxStackSize).Reverse())
                _undoStack.Push(item);
        }

        StateChanged?.Invoke();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        if (!CanUndo) return;
        var cmd = _undoStack.Pop();
        cmd.Undo();
        _redoStack.Push(cmd);
        StateChanged?.Invoke();
    }

    /// <inheritdoc/>
    public void Redo()
    {
        if (!CanRedo) return;
        var cmd = _redoStack.Pop();
        cmd.Execute();
        _undoStack.Push(cmd);
        StateChanged?.Invoke();
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke();
    }
}
