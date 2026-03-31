using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DevWorkspaceHub.Helpers;

/// <summary>
/// Measures monospace font metrics (character cell width and height) for terminal resize calculations.
/// Uses FormattedText for accurate WPF measurement accounting for DPI and font properties.
/// </summary>
public static class TerminalFontMetrics
{
    private const string MeasureChar = "W"; // Widest common character

    /// <summary>
    /// Gets the DPI-aware pixels-per-dip value for a given visual element.
    /// </summary>
    private static double GetPixelsPerDip(Visual visual)
    {
        return VisualTreeHelper.GetDpi(visual).PixelsPerDip;
    }

    /// <summary>
    /// Creates a FormattedText for the given font properties, used for measurement.
    /// </summary>
    private static FormattedText CreateFormattedText(
        string text,
        FontFamily fontFamily,
        FontStyle fontStyle,
        FontWeight fontWeight,
        FontStretch fontStretch,
        double fontSize,
        double pixelsPerDip)
    {
        return new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(fontFamily, fontStyle, fontWeight, fontStretch),
            fontSize,
            Brushes.Black,
            pixelsPerDip);
    }

    /// <summary>
    /// Measures the character cell dimensions for a monospace font at a given size and DPI.
    /// </summary>
    /// <param name="fontFamily">The font family (e.g., "Cascadia Code").</param>
    /// <param name="fontSize">Font size in WPF points.</param>
    /// <param name="pixelsPerDip">DPI scale factor.</param>
    /// <returns>Tuple of (charWidth, lineHeight).</returns>
    public static (double CharWidth, double LineHeight) Measure(
        FontFamily fontFamily,
        double fontSize,
        double pixelsPerDip)
    {
        try
        {
            var ft = CreateFormattedText(
                MeasureChar,
                fontFamily,
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal,
                fontSize,
                pixelsPerDip);

            return (ft.Width, ft.Height);
        }
        catch
        {
            return (8.4, 18.0); // Safe fallback
        }
    }

    /// <summary>
    /// Measures character cell dimensions from a FrameworkElement's current font properties and DPI.
    /// </summary>
    /// <param name="element">The element to read font properties and DPI from.</param>
    /// <returns>Tuple of (charWidth, lineHeight).</returns>
    public static (double CharWidth, double LineHeight) Measure(Control element)
    {
        try
        {
            double pixelsPerDip = GetPixelsPerDip(element);
            var fontFamily = element.FontFamily;
            var fontSize = element.FontSize;

            var ft = CreateFormattedText(
                MeasureChar,
                fontFamily,
                element.FontStyle,
                element.FontWeight,
                element.FontStretch,
                fontSize,
                pixelsPerDip);

            return (ft.Width, ft.Height);
        }
        catch
        {
            return (8.4, 18.0);
        }
    }

    /// <summary>
    /// Calculates terminal columns and rows based on available space, font metrics, and padding.
    /// </summary>
    /// <param name="availableWidth">Available width in device-independent pixels.</param>
    /// <param name="availableHeight">Available height in device-independent pixels.</param>
    /// <param name="padding">Control padding to subtract from available space.</param>
    /// <param name="charWidth">Width of a single character cell.</param>
    /// <param name="lineHeight">Height of a single line.</param>
    /// <param name="minColumns">Minimum columns (default 40).</param>
    /// <param name="minRows">Minimum rows (default 10).</param>
    /// <returns>Tuple of (columns, rows).</returns>
    public static (short Columns, short Rows) CalculateTerminalSize(
        double availableWidth,
        double availableHeight,
        Thickness padding,
        double charWidth,
        double lineHeight,
        short minColumns = 40,
        short minRows = 10)
    {
        double usableWidth = Math.Max(0, availableWidth - padding.Left - padding.Right);
        double usableHeight = Math.Max(0, availableHeight - padding.Top - padding.Bottom);

        short columns = (short)Math.Max(minColumns, (int)(usableWidth / charWidth));
        short rows = (short)Math.Max(minRows, (int)(usableHeight / lineHeight));

        return (columns, rows);
    }

    /// <summary>
    /// Validates that charWidth and lineHeight are within reasonable bounds.
    /// </summary>
    public static bool IsValidMetrics(double charWidth, double lineHeight)
    {
        return charWidth is > 0 and < 100 && lineHeight is > 0 and < 100;
    }
}
