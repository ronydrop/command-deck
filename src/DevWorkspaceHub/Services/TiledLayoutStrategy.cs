using System;
using System.Collections.Generic;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Layout strategy for tiled mode.
/// Arranges terminals in a single horizontal row (infinite strip to the right).
/// Each terminal occupies the full viewport height and a proportional width.
/// When few terminals fit the viewport they share its width equally;
/// once they would become too narrow, a fixed minimum width is used and
/// the strip extends beyond the viewport (scroll via drag-to-pan).
/// </summary>
public class TiledLayoutStrategy : ILayoutStrategy
{
    private const double Gap = 4;

    /// <summary>Minimum width for a single terminal tile in the horizontal strip.</summary>
    private const double MinTileWidth = 480;

    public LayoutMode Mode => LayoutMode.Tiled;
    public bool SupportsDrag => false;
    public bool SupportsPanZoom => true;   // horizontal pan enabled
    public bool SupportsResize => false;

    public TileLayout CalculateLayout(int itemCount, double viewportWidth, double viewportHeight)
    {
        if (itemCount <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
            return new TileLayout(0, 0, Array.Empty<TilePlacement>());

        return BuildHorizontalStripLayout(itemCount, viewportWidth, viewportHeight);
    }

    // ─── Horizontal strip: all terminals side-by-side ───────────────────────

    private static TileLayout BuildHorizontalStripLayout(int itemCount, double vpW, double vpH)
    {
        double h = vpH - Gap * 2;

        // Try to fit all tiles evenly inside the viewport
        double evenWidth = (vpW - Gap * (itemCount + 1)) / itemCount;

        // Use the larger of the even split or the minimum width
        double tileW = Math.Max(evenWidth, MinTileWidth);

        var placements = new List<TilePlacement>(itemCount);

        for (int i = 0; i < itemCount; i++)
        {
            double x = Gap + i * (tileW + Gap);
            placements.Add(new TilePlacement(i, x, Gap, tileW, h));
        }

        return new TileLayout(1, itemCount, placements);
    }
}
