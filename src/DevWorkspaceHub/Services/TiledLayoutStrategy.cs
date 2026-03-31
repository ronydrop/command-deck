using System;
using System.Collections.Generic;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Layout strategy for tiled mode.
/// Calculates grid positions to fill the viewport proportionally.
/// </summary>
public class TiledLayoutStrategy : ILayoutStrategy
{
    private const double Gap = 4;

    public LayoutMode Mode => LayoutMode.Tiled;
    public bool SupportsDrag => false;
    public bool SupportsPanZoom => false;
    public bool SupportsResize => false;

    public TileLayout CalculateLayout(int itemCount, double viewportWidth, double viewportHeight)
    {
        if (itemCount <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
            return new TileLayout(0, 0, Array.Empty<TilePlacement>());

        return itemCount switch
        {
            1 => BuildSingleLayout(viewportWidth, viewportHeight),
            2 => BuildTwoColumnLayout(viewportWidth, viewportHeight),
            3 => BuildMasterStackLayout(viewportWidth, viewportHeight),
            4 => BuildGridLayout(2, 2, 4, viewportWidth, viewportHeight),
            5 => BuildFiveLayout(viewportWidth, viewportHeight),
            6 => BuildGridLayout(2, 3, 6, viewportWidth, viewportHeight),
            _ => BuildGridLayout(
                    rows: (int)Math.Ceiling((double)itemCount / 3),
                    cols: 3,
                    itemCount,
                    viewportWidth,
                    viewportHeight)
        };
    }

    // ─── 1 terminal: full area ──────────────────────────────────────────────

    private static TileLayout BuildSingleLayout(double vpW, double vpH)
    {
        var p = new TilePlacement(0, Gap, Gap, vpW - Gap * 2, vpH - Gap * 2);
        return new TileLayout(1, 1, new[] { p });
    }

    // ─── 2 terminals: side by side ──────────────────────────────────────────

    private static TileLayout BuildTwoColumnLayout(double vpW, double vpH)
    {
        double halfW = (vpW - Gap * 3) / 2;
        double h = vpH - Gap * 2;

        return new TileLayout(1, 2, new[]
        {
            new TilePlacement(0, Gap, Gap, halfW, h),
            new TilePlacement(1, Gap * 2 + halfW, Gap, halfW, h)
        });
    }

    // ─── 3 terminals: master left + 2 stacked right ────────────────────────

    private static TileLayout BuildMasterStackLayout(double vpW, double vpH)
    {
        double halfW = (vpW - Gap * 3) / 2;
        double fullH = vpH - Gap * 2;
        double halfH = (vpH - Gap * 3) / 2;
        double rightX = Gap * 2 + halfW;

        return new TileLayout(2, 2, new[]
        {
            new TilePlacement(0, Gap, Gap, halfW, fullH),
            new TilePlacement(1, rightX, Gap, halfW, halfH),
            new TilePlacement(2, rightX, Gap * 2 + halfH, halfW, halfH)
        });
    }

    // ─── 5 terminals: 3 top + 2 bottom ─────────────────────────────────────

    private static TileLayout BuildFiveLayout(double vpW, double vpH)
    {
        double halfH = (vpH - Gap * 3) / 2;

        // Top row: 3 columns
        double topCellW = (vpW - Gap * 4) / 3;
        // Bottom row: 2 columns
        double botCellW = (vpW - Gap * 3) / 2;

        double bottomY = Gap * 2 + halfH;

        return new TileLayout(2, 3, new[]
        {
            new TilePlacement(0, Gap, Gap, topCellW, halfH),
            new TilePlacement(1, Gap * 2 + topCellW, Gap, topCellW, halfH),
            new TilePlacement(2, Gap * 3 + topCellW * 2, Gap, topCellW, halfH),
            new TilePlacement(3, Gap, bottomY, botCellW, halfH),
            new TilePlacement(4, Gap * 2 + botCellW, bottomY, botCellW, halfH)
        });
    }

    // ─── Generic grid layout ────────────────────────────────────────────────

    private static TileLayout BuildGridLayout(int rows, int cols, int itemCount, double vpW, double vpH)
    {
        double cellW = (vpW - Gap * (cols + 1)) / cols;
        double cellH = (vpH - Gap * (rows + 1)) / rows;

        var placements = new List<TilePlacement>();

        for (int i = 0; i < itemCount; i++)
        {
            int row = i / cols;
            int col = i % cols;

            // Last row: center remaining items if fewer than cols
            int itemsInLastRow = itemCount - (rows - 1) * cols;
            bool isLastRow = row == rows - 1 && itemsInLastRow < cols;

            double x, w;
            if (isLastRow)
            {
                int lastCol = i - (rows - 1) * cols;
                w = (vpW - Gap * (itemsInLastRow + 1)) / itemsInLastRow;
                x = Gap + lastCol * (w + Gap);
            }
            else
            {
                w = cellW;
                x = Gap + col * (cellW + Gap);
            }

            double y = Gap + row * (cellH + Gap);

            placements.Add(new TilePlacement(i, x, y, w, cellH));
        }

        return new TileLayout(rows, cols, placements);
    }
}
