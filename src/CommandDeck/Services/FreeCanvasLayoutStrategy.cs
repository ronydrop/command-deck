using System;
using System.Collections.Generic;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Layout strategy for the free canvas mode.
/// Items are positioned via user drag — <see cref="CalculateLayout"/> returns a uniform grid
/// (used when a fixed grid is needed). Auto-arrange in canvas mode uses
/// <see cref="CalculateReflowPreserveSizes"/> so only positions change, not card sizes.
/// </summary>
public class FreeCanvasLayoutStrategy : ILayoutStrategy
{
    private const double Padding = 24;
    private const int MaxCols = 4;
    private const double MinItemWidth = 480;
    private const double MinItemHeight = 360;
    private const double DefaultItemWidth = 780;
    private const double DefaultItemHeight = 520;
    private const double MinReflowWidth = 320;
    private const double MinReflowHeight = 220;

    public LayoutMode Mode => LayoutMode.FreeCanvas;
    public bool SupportsDrag => true;
    public bool SupportsPanZoom => true;
    public bool SupportsResize => true;

    /// <summary>
    /// Flows items left-to-right with wrapping, preserving each item's width and height.
    /// Used for "organize" on the free canvas so terminals are only repositioned.
    /// </summary>
    public TileLayout CalculateReflowPreserveSizes(
        IReadOnlyList<double> widths,
        IReadOnlyList<double> heights,
        double viewportWidth)
    {
        var placements = new List<TilePlacement>();

        int n = widths.Count;
        if (n == 0 || heights.Count != n)
            return new TileLayout(0, 0, placements);

        double vpW = viewportWidth > 0 ? viewportWidth : 1200;

        double x = Padding;
        double y = Padding;
        double rowHeight = 0;
        int rowCount = 1;

        for (int i = 0; i < n; i++)
        {
            double w = Math.Max(widths[i], MinReflowWidth);
            double h = Math.Max(heights[i], MinReflowHeight);

            // Wrap when the next item does not fit in the remainder of the row (not at row start).
            if (x > Padding && x + w > vpW - Padding)
            {
                x = Padding;
                y += rowHeight + Padding;
                rowHeight = 0;
                rowCount++;
            }

            placements.Add(new TilePlacement(i, x, y, w, h));
            rowHeight = Math.Max(rowHeight, h);
            x += w + Padding;
        }

        return new TileLayout(rowCount, n, placements);
    }

    public TileLayout CalculateLayout(int itemCount, double viewportWidth, double viewportHeight)
    {
        var placements = new List<TilePlacement>();

        if (itemCount <= 0)
            return new TileLayout(0, 0, placements);

        int cols = Math.Min(itemCount, MaxCols);
        int rows = (int)Math.Ceiling((double)itemCount / cols);

        double vpW = viewportWidth > 0 ? viewportWidth : DefaultItemWidth * cols + Padding * (cols + 1);
        double vpH = viewportHeight > 0 ? viewportHeight : DefaultItemHeight;

        double itemW = Math.Max((vpW - Padding * (cols + 1)) / cols, MinItemWidth);
        double itemH = Math.Max((vpH - Padding * (rows + 1)) / rows, MinItemHeight);

        for (int i = 0; i < itemCount; i++)
        {
            int col = i % cols;
            int row = i / cols;

            double x = Padding + col * (itemW + Padding);
            double y = Padding + row * (itemH + Padding);

            placements.Add(new TilePlacement(i, x, y, itemW, itemH));
        }

        return new TileLayout(rows, cols, placements);
    }
}
