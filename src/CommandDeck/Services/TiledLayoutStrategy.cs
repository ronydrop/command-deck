using System;
using System.Collections.Generic;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Layout strategy for tiled mode.
/// Arranges terminals in a grid with up to 4 columns per row.
/// Each row shares the viewport width equally; rows share the viewport height equally.
/// When tiles would become too narrow, a minimum width is enforced and the
/// canvas extends beyond the viewport (scroll via drag-to-pan).
/// </summary>
public class TiledLayoutStrategy : ILayoutStrategy
{
    private const double Gap = 4;
    private const int MaxCols = 4;

    /// <summary>Minimum width for a single terminal tile.</summary>
    private const double MinTileWidth = 480;

    /// <summary>Minimum height for a single terminal tile.</summary>
    private const double MinTileHeight = 300;

    public LayoutMode Mode => LayoutMode.Tiled;
    public bool SupportsDrag => false;
    public bool SupportsPanZoom => true;
    public bool SupportsResize => false;

    public TileLayout CalculateLayout(int itemCount, double viewportWidth, double viewportHeight)
    {
        if (itemCount <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
            return new TileLayout(0, 0, Array.Empty<TilePlacement>());

        return BuildGridLayout(itemCount, viewportWidth, viewportHeight);
    }

    // ─── Grid layout: up to MaxCols columns, wraps into multiple rows ───────

    private static TileLayout BuildGridLayout(int itemCount, double vpW, double vpH)
    {
        int cols = Math.Min(itemCount, MaxCols);
        int rows = (int)Math.Ceiling((double)itemCount / cols);

        double tileW = Math.Max((vpW - Gap * (cols + 1)) / cols, MinTileWidth);
        double tileH = Math.Max((vpH - Gap * (rows + 1)) / rows, MinTileHeight);

        var placements = new List<TilePlacement>(itemCount);

        for (int i = 0; i < itemCount; i++)
        {
            int col = i % cols;
            int row = i / cols;

            double x = Gap + col * (tileW + Gap);
            double y = Gap + row * (tileH + Gap);

            placements.Add(new TilePlacement(i, x, y, tileW, tileH));
        }

        return new TileLayout(rows, cols, placements);
    }
}
