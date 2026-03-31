using System.Collections.Generic;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Describes the placement of a single item within a tiled grid.
/// </summary>
public record TilePlacement(
    int Index,
    double X,
    double Y,
    double Width,
    double Height);

/// <summary>
/// Describes the complete tiled layout for a set of items.
/// </summary>
public record TileLayout(
    int Rows,
    int Cols,
    IReadOnlyList<TilePlacement> Placements);

/// <summary>
/// Strategy interface for computing terminal layout positions.
/// </summary>
public interface ILayoutStrategy
{
    LayoutMode Mode { get; }
    bool SupportsDrag { get; }
    bool SupportsPanZoom { get; }
    bool SupportsResize { get; }

    /// <summary>
    /// Calculates layout positions for a given number of items within the viewport.
    /// </summary>
    TileLayout CalculateLayout(int itemCount, double viewportWidth, double viewportHeight);
}
