using System;
using System.Collections.Generic;
using System.Linq;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Layout strategy for Split Pane mode.
/// Builds a binary tree of panes; each leaf holds one tile.
/// The tree is rebuilt whenever items are added or removed.
/// Splitter ratios can be adjusted at runtime via <see cref="SetRatio"/>.
/// </summary>
public class SplitPaneLayoutStrategy : ILayoutStrategy
{
    private const double MinPaneSize = 120;
    private const double Gap = 4;

    public LayoutMode Mode => LayoutMode.SplitPane;
    public bool SupportsDrag => false;
    public bool SupportsPanZoom => false;
    public bool SupportsResize => false;

    // Persisted ratios: key = sorted pair of tile indices separated by '|'
    private readonly Dictionary<string, double> _ratios = new();

    public TileLayout CalculateLayout(int itemCount, double viewportWidth, double viewportHeight)
    {
        if (itemCount <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
            return new TileLayout(0, 0, Array.Empty<TilePlacement>());

        var indices = Enumerable.Range(0, itemCount).ToList();
        var placements = new List<TilePlacement>(itemCount);
        BuildSplit(indices, 0, 0, viewportWidth, viewportHeight, placements, true);
        return new TileLayout(1, itemCount, placements);
    }

    /// <summary>Sets the split ratio between two adjacent panes.</summary>
    public void SetRatio(int indexA, int indexB, double ratio)
    {
        var key = MakeKey(indexA, indexB);
        _ratios[key] = Math.Clamp(ratio, 0.15, 0.85);
    }

    private void BuildSplit(List<int> indices, double x, double y, double w, double h,
                            List<TilePlacement> placements, bool horizontal)
    {
        if (indices.Count == 0) return;

        if (indices.Count == 1)
        {
            placements.Add(new TilePlacement(indices[0], x + Gap, y + Gap,
                Math.Max(w - Gap * 2, MinPaneSize),
                Math.Max(h - Gap * 2, MinPaneSize)));
            return;
        }

        int splitIdx = indices.Count / 2;
        var first = indices.Take(splitIdx).ToList();
        var second = indices.Skip(splitIdx).ToList();

        var key = MakeKey(first.Last(), second.First());
        double ratio = _ratios.TryGetValue(key, out var r) ? r : 0.5;

        if (horizontal)
        {
            double firstW = w * ratio;
            double secondW = w - firstW;
            BuildSplit(first, x, y, firstW, h, placements, !horizontal);
            BuildSplit(second, x + firstW, y, secondW, h, placements, !horizontal);
        }
        else
        {
            double firstH = h * ratio;
            double secondH = h - firstH;
            BuildSplit(first, x, y, w, firstH, placements, !horizontal);
            BuildSplit(second, x, y + firstH, w, secondH, placements, !horizontal);
        }
    }

    private static string MakeKey(int a, int b)
        => a < b ? $"{a}|{b}" : $"{b}|{a}";
}
