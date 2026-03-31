using System.Windows.Documents;
using System.Windows.Media;

namespace DevWorkspaceHub.Helpers;

/// <summary>
/// Stores a single terminal character with its formatting attributes.
/// </summary>
internal struct TerminalCell
{
    public char Char;
    public Color Foreground;
    public Color Background;
    public bool IsBold;
    public bool IsItalic;
    public bool IsUnderline;

    public static readonly TerminalCell Empty = new()
    {
        Char = ' ',
        Foreground = Color.FromRgb(0xCD, 0xD6, 0xF4),
        Background = Color.FromRgb(0x1E, 0x1E, 0x2E),
    };

    /// <summary>
    /// Returns true if this cell has identical formatting to another.
    /// </summary>
    public readonly bool SameFormat(in TerminalCell other)
        => Foreground == other.Foreground
        && Background == other.Background
        && IsBold == other.IsBold
        && IsItalic == other.IsItalic
        && IsUnderline == other.IsUnderline;
}

/// <summary>
/// A line-level character buffer with cursor tracking for terminal emulation.
/// Maintains a fixed-width array of <see cref="TerminalCell"/> representing the current
/// terminal line. Supports cursor movement, insert/delete, and erase operations.
/// Can flush its content as WPF <see cref="Run"/> elements for display in a FlowDocument.
/// </summary>
internal class TerminalLineBuffer
{
    private TerminalCell[] _cells;
    private int _width;

    /// <summary>Current cursor column (0-based).</summary>
    public int CursorCol { get; private set; }

    /// <summary>Logical length of content in the line (rightmost non-empty position + 1).</summary>
    public int LineLength { get; private set; }

    private readonly Color _defaultFg;
    private readonly Color _defaultBg;
    private readonly TerminalCell _emptyCell;

    public TerminalLineBuffer(int columns = 120)
        : this(columns, Color.FromRgb(0xCD, 0xD6, 0xF4), Color.FromRgb(0x1E, 0x1E, 0x2E))
    {
    }

    public TerminalLineBuffer(int columns, Color defaultFg, Color defaultBg)
    {
        _width = columns;
        _defaultFg = defaultFg;
        _defaultBg = defaultBg;
        _emptyCell = new TerminalCell { Char = ' ', Foreground = defaultFg, Background = defaultBg };
        _cells = new TerminalCell[columns];
        Clear();
    }

    /// <summary>
    /// Writes a character at the current cursor position and advances the cursor.
    /// Overwrites any existing character at that position.
    /// </summary>
    public void Write(char c, Color fg, Color bg, bool bold, bool italic, bool underline)
    {
        if (CursorCol >= _width)
        {
            // Line wrap not supported in single-line buffer; clamp to last column
            CursorCol = _width - 1;
        }

        _cells[CursorCol] = new TerminalCell
        {
            Char = c,
            Foreground = fg,
            Background = bg,
            IsBold = bold,
            IsItalic = italic,
            IsUnderline = underline,
        };

        CursorCol++;
        if (CursorCol > LineLength)
            LineLength = CursorCol;
    }

    /// <summary>
    /// Writes a string starting at the current cursor position.
    /// </summary>
    public void Write(string text, Color fg, Color bg, bool bold, bool italic, bool underline)
    {
        foreach (char c in text)
        {
            if (c == '\t')
            {
                // Expand tab to next multiple of 8
                int nextTab = ((CursorCol / 8) + 1) * 8;
                while (CursorCol < nextTab && CursorCol < _width)
                    Write(' ', fg, bg, bold, italic, underline);
            }
            else if (!char.IsControl(c))
            {
                Write(c, fg, bg, bold, italic, underline);
            }
        }
    }

    /// <summary>Move cursor left by n positions (CSI D). Clamps to column 0.</summary>
    public void MoveCursorLeft(int n = 1)
        => CursorCol = Math.Max(0, CursorCol - n);

    /// <summary>Move cursor right by n positions (CSI C). Clamps to width - 1.</summary>
    public void MoveCursorRight(int n = 1)
        => CursorCol = Math.Min(_width - 1, CursorCol + n);

    /// <summary>Set cursor to absolute column (0-based) (CSI G).</summary>
    public void MoveCursorToColumn(int col)
        => CursorCol = Math.Clamp(col, 0, _width - 1);

    /// <summary>Move cursor left by 1 for backspace character (\x08). Does NOT delete.</summary>
    public void Backspace()
        => MoveCursorLeft(1);

    /// <summary>Move cursor to column 0 for carriage return (\r).</summary>
    public void CarriageReturn()
        => CursorCol = 0;

    /// <summary>
    /// Erase in line (CSI K).
    /// Mode 0: erase from cursor to end. Mode 1: erase from start to cursor. Mode 2: erase all.
    /// </summary>
    public void EraseLine(int mode)
    {
        switch (mode)
        {
            case 0: // Erase to end of line
                for (int i = CursorCol; i < _width; i++)
                    _cells[i] = _emptyCell;
                if (CursorCol < LineLength)
                    LineLength = CursorCol;
                break;

            case 1: // Erase to start of line
                for (int i = 0; i <= CursorCol && i < _width; i++)
                    _cells[i] = _emptyCell;
                break;

            case 2: // Erase entire line
                for (int i = 0; i < _width; i++)
                    _cells[i] = _emptyCell;
                LineLength = 0;
                break;
        }
    }

    /// <summary>
    /// Delete n characters at cursor, shifting remaining chars left (CSI P).
    /// </summary>
    public void DeleteChars(int n)
    {
        n = Math.Min(n, LineLength - CursorCol);
        if (n <= 0) return;

        Array.Copy(_cells, CursorCol + n, _cells, CursorCol, _width - CursorCol - n);
        for (int i = _width - n; i < _width; i++)
            _cells[i] = _emptyCell;

        LineLength = Math.Max(0, LineLength - n);
    }

    /// <summary>
    /// Insert n blank characters at cursor, shifting existing chars right (CSI @).
    /// </summary>
    public void InsertChars(int n)
    {
        if (n <= 0) return;
        int shiftEnd = Math.Min(LineLength + n, _width);

        // Shift right
        for (int i = shiftEnd - 1; i >= CursorCol + n; i--)
            _cells[i] = _cells[i - n];

        // Fill inserted positions with blanks
        for (int i = CursorCol; i < CursorCol + n && i < _width; i++)
            _cells[i] = _emptyCell;

        LineLength = Math.Min(LineLength + n, _width);
    }

    /// <summary>
    /// Converts the buffer content into a list of <see cref="Run"/> elements,
    /// grouping consecutive cells with identical formatting into single Runs.
    /// Only includes content up to <see cref="LineLength"/>.
    /// </summary>
    public List<Run> FlushToInlines()
    {
        var runs = new List<Run>();
        if (LineLength == 0) return runs;

        int start = 0;
        while (start < LineLength)
        {
            var fmt = _cells[start];
            int end = start + 1;

            // Group consecutive cells with same formatting
            while (end < LineLength && _cells[end].SameFormat(fmt))
                end++;

            // Build the text for this run
            var chars = new char[end - start];
            for (int i = start; i < end; i++)
                chars[i - start] = _cells[i].Char;

            var text = new string(chars);

            // Trim trailing spaces only for the very last run
            if (end >= LineLength)
                text = text.TrimEnd();

            if (text.Length > 0)
            {
                var run = new Run(text);

                // Apply foreground
                if (fmt.Foreground != _defaultFg)
                    run.Foreground = GetBrush(fmt.Foreground);

                // Apply background
                if (fmt.Background != _defaultBg)
                    run.Background = GetBrush(fmt.Background);

                if (fmt.IsBold) run.FontWeight = System.Windows.FontWeights.Bold;
                if (fmt.IsItalic) run.FontStyle = System.Windows.FontStyles.Italic;
                if (fmt.IsUnderline)
                    run.TextDecorations = System.Windows.TextDecorations.Underline;

                runs.Add(run);
            }

            start = end;
        }

        return runs;
    }

    /// <summary>
    /// Clears the buffer and resets cursor to column 0.
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < _width; i++)
            _cells[i] = _emptyCell;
        CursorCol = 0;
        LineLength = 0;
    }

    /// <summary>
    /// Resizes the buffer to a new column width. Preserves existing content where possible.
    /// </summary>
    public void SetWidth(int columns)
    {
        if (columns == _width) return;

        var newCells = new TerminalCell[columns];
        int copyLen = Math.Min(columns, _width);
        Array.Copy(_cells, newCells, copyLen);

        for (int i = copyLen; i < columns; i++)
            newCells[i] = _emptyCell;

        _cells = newCells;
        _width = columns;
        LineLength = Math.Min(LineLength, columns);
        CursorCol = Math.Min(CursorCol, columns - 1);
    }

    // ─── Brush cache (shared with AnsiParser) ───────────────────────────────

    private static readonly Dictionary<Color, SolidColorBrush> BrushCache = new();

    private static SolidColorBrush GetBrush(Color color)
    {
        if (!BrushCache.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze();
            BrushCache[color] = brush;
        }
        return brush;
    }
}
