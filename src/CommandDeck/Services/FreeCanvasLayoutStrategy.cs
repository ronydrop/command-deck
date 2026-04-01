using System;
using System.Collections.Generic;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Layout strategy for the free canvas mode.
/// Items are positioned via user drag — CalculateLayout returns cascade positions
/// used only as defaults for newly added items.
/// </summary>
public class FreeCanvasLayoutStrategy : ILayoutStrategy
{
    private const double ItemWidth = 780;
    private const double ItemHeight = 520;
    private const double Padding = 24;
    private const double MaxWidth = 1800;

    public LayoutMode Mode => LayoutMode.FreeCanvas;
    public bool SupportsDrag => true;
    public bool SupportsPanZoom => true;
    public bool SupportsResize => true;

    public TileLayout CalculateLayout(int itemCount, double viewportWidth, double viewportHeight)
    {
        var placements = new List<TilePlacement>();
        double nextX = 40, nextY = 40, rowHeight = 0;

        for (int i = 0; i < itemCount; i++)
        {
            placements.Add(new TilePlacement(i, nextX, nextY, ItemWidth, ItemHeight));

            if (ItemHeight > rowHeight) rowHeight = ItemHeight;
            nextX += ItemWidth + Padding;

            if (nextX + ItemWidth > MaxWidth)
            {
                nextX = 40;
                nextY += rowHeight + Padding;
                rowHeight = 0;
            }
        }

        int cols = Math.Max(1, (int)((MaxWidth - 40) / (ItemWidth + Padding)));
        int rows = Math.Max(1, (int)Math.Ceiling((double)itemCount / cols));

        return new TileLayout(rows, cols, placements);
    }
}
