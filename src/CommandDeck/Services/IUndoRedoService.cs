namespace CommandDeck.Services;

/// <summary>
/// Service that tracks undoable canvas operations and supports Ctrl+Z / Ctrl+Shift+Z.
/// </summary>
public interface IUndoRedoService
{
    /// <summary>True when there is at least one operation that can be undone.</summary>
    bool CanUndo { get; }

    /// <summary>True when there is at least one operation that can be redone.</summary>
    bool CanRedo { get; }

    /// <summary>
    /// Records a completed command on the undo stack.
    /// Clears the redo stack because a new action breaks the redo history.
    /// </summary>
    void Record(IUndoableCommand command);

    /// <summary>Pops the top of the undo stack, calls Undo(), and pushes to the redo stack.</summary>
    void Undo();

    /// <summary>Pops the top of the redo stack, calls Execute(), and pushes to the undo stack.</summary>
    void Redo();

    /// <summary>Empties both stacks (e.g. after a workspace is closed or replaced).</summary>
    void Clear();

    /// <summary>Raised whenever <see cref="CanUndo"/> or <see cref="CanRedo"/> may have changed.</summary>
    event Action? StateChanged;
}

/// <summary>Represents a reversible canvas operation.</summary>
public interface IUndoableCommand
{
    /// <summary>Applies (or re-applies) the operation.</summary>
    void Execute();

    /// <summary>Reverses the operation.</summary>
    void Undo();

    /// <summary>Human-readable description shown in tooltips or history panels.</summary>
    string Description { get; }
}
