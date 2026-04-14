using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

/// <summary>
/// Transparent overlay drawn on top of the canvas that renders Bézier curves
/// between connected tiles. Tiles declare connections via <see cref="CanvasItemViewModel.ConnectionTargetIds"/>.
///
/// The overlay is a Canvas with no hit-testing so mouse events pass through to tiles.
/// </summary>
public class CanvasConnectionOverlay : Canvas
{
    // Brushes for connection lines
    private static readonly Color DefaultLineColor = Color.FromArgb(160, 139, 180, 250); // #89b4fa60
    private static readonly double LineThickness = 1.8;

    // Cache of Path elements keyed by "sourceId→targetId"
    private readonly Dictionary<string, Path> _paths = new();

    public CanvasConnectionOverlay()
    {
        IsHitTestVisible = false;
        Background = Brushes.Transparent;
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Re-draws all connection lines based on the current canvas items.
    /// Should be called whenever items move, resize, or connections change.
    /// </summary>
    public void Refresh(IEnumerable<CanvasItemViewModel> items)
    {
        Children.Clear();
        _paths.Clear();

        var list = items.ToList();
        var lookup = list.ToDictionary(i => i.Id);

        foreach (var source in list)
        {
            foreach (var targetId in source.ConnectionTargetIds)
            {
                if (!lookup.TryGetValue(targetId, out var target)) continue;
                DrawConnection(source, target);
            }
        }
    }

    // ─── Drawing ─────────────────────────────────────────────────────────────

    private void DrawConnection(CanvasItemViewModel source, CanvasItemViewModel target)
    {
        // Connection points: right center of source → left center of target
        // If target is to the left, swap to left→right
        bool targetIsRight = target.X > source.X;

        double srcX = targetIsRight ? source.X + source.Width  : source.X;
        double srcY = source.Y + source.Height / 2;
        double tgtX = targetIsRight ? target.X                  : target.X + target.Width;
        double tgtY = target.Y + target.Height / 2;

        // Bézier control points: horizontal handles proportional to distance
        double dist = Math.Abs(tgtX - srcX);
        double cpOffset = Math.Max(60, dist * 0.45);

        var figure = new PathFigure
        {
            StartPoint = new Point(srcX, srcY),
            IsFilled = false
        };

        figure.Segments.Add(new BezierSegment(
            new Point(srcX + (targetIsRight ? cpOffset : -cpOffset), srcY),
            new Point(tgtX + (targetIsRight ? -cpOffset : cpOffset), tgtY),
            new Point(tgtX, tgtY),
            isStroked: true));

        // Use source's accent color if available
        var lineColor = DefaultLineColor;
        if (!string.IsNullOrEmpty(source.AccentColor))
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(source.AccentColor);
                lineColor = Color.FromArgb(180, c.R, c.G, c.B);
            }
            catch { }
        }

        var path = new Path
        {
            Data = new PathGeometry(new[] { figure }),
            Stroke = new SolidColorBrush(lineColor),
            StrokeThickness = LineThickness,
            StrokeDashArray = new DoubleCollection { 6, 3 },
            StrokeLineJoin = PenLineJoin.Round,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 6,
                ShadowDepth = 0,
                Color = lineColor,
                Opacity = 0.5
            }
        };

        // Arrow head at target
        var arrow = DrawArrow(tgtX, tgtY, targetIsRight ? 0 : Math.PI, lineColor);

        Children.Add(path);
        Children.Add(arrow);

        _paths[$"{source.Id}→{target.Id}"] = path;
    }

    private static Path DrawArrow(double x, double y, double angleDeg, Color color)
    {
        double size = 8;
        double angle = angleDeg;
        // Arrow pointing left (←) when angle=π, right (→) when angle=0
        double x1 = x + Math.Cos(angle + 2.7) * size;
        double y1 = y + Math.Sin(angle + 2.7) * size;
        double x2 = x + Math.Cos(angle - 2.7) * size;
        double y2 = y + Math.Sin(angle - 2.7) * size;

        var figure = new PathFigure { StartPoint = new Point(x1, y1), IsFilled = true };
        figure.Segments.Add(new LineSegment(new Point(x, y), isStroked: true));
        figure.Segments.Add(new LineSegment(new Point(x2, y2), isStroked: true));
        figure.IsClosed = false;

        return new Path
        {
            Data = new PathGeometry(new[] { figure }),
            Stroke = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B)),
            StrokeThickness = LineThickness,
            Fill = Brushes.Transparent,
        };
    }
}
