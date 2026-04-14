using System;

namespace CommandDeck.Helpers;

/// <summary>
/// Maps Bento slot indices (0..7) to Grid row/column positions in a 3×3 grid
/// where the center cell (row=1, col=1) is reserved for the block catalog panel.
///
/// Grid layout:
///   [ 0 ][ 1 ][ 2 ]
///   [ 3 ][ . ][ 4 ]    (. = center = BlockCatalogPanel)
///   [ 5 ][ 6 ][ 7 ]
/// </summary>
public static class BentoSlotMap
{
    /// <summary>Total number of available drop slots.</summary>
    public const int SlotCount = 8;

    /// <summary>Grid row of the central catalog panel.</summary>
    public const int CenterRow = 1;

    /// <summary>Grid column of the central catalog panel.</summary>
    public const int CenterCol = 1;

    /// <summary>
    /// Converts a slot index (0..7) to its (row, col) position in the 3×3 grid.
    /// </summary>
    public static (int Row, int Col) SlotToGrid(int slotIndex) => slotIndex switch
    {
        0 => (0, 0),
        1 => (0, 1),
        2 => (0, 2),
        3 => (1, 0),
        4 => (1, 2),
        5 => (2, 0),
        6 => (2, 1),
        7 => (2, 2),
        _ => throw new ArgumentOutOfRangeException(nameof(slotIndex), $"Slot index must be 0..7, got {slotIndex}.")
    };

    /// <summary>
    /// Converts a (row, col) position back to its slot index, or -1 if it's the center cell.
    /// </summary>
    public static int GridToSlot(int row, int col)
    {
        if (row == CenterRow && col == CenterCol) return -1;
        for (int i = 0; i < SlotCount; i++)
        {
            var (r, c) = SlotToGrid(i);
            if (r == row && c == col) return i;
        }
        return -1;
    }
}
