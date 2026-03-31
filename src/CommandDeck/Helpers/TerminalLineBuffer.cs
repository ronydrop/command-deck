using System.Windows.Media;

namespace CommandDeck.Helpers;

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

    /// <summary>Returns true if this cell has identical formatting to another.</summary>
    public readonly bool SameFormat(in TerminalCell other)
        => Foreground == other.Foreground
        && Background == other.Background
        && IsBold == other.IsBold
        && IsItalic == other.IsItalic
        && IsUnderline == other.IsUnderline;
}

/// <summary>
/// Full 2-D virtual terminal screen buffer.
/// Manages a <c>rows × cols</c> grid of <see cref="TerminalCell"/> values,
/// cursor positioning, all standard erase/scroll/insert/delete operations,
/// and a scrollback queue for lines that roll off the top.
/// </summary>
internal sealed class TerminalLineBuffer
{
    private TerminalCell[,] _screen;
    private int _rows;
    private int _cols;

    private readonly Color _defaultFg;
    private readonly Color _defaultBg;

    // Lines that scrolled off the top; consumed by AnsiParser for scrollback rendering.
    private readonly Queue<TerminalCell[]> _scrolledOffLines = new();

    // ─── Public Properties ───────────────────────────────────────────────────

    /// <summary>0-based cursor row (within the visible screen).</summary>
    public int CursorRow { get; private set; }

    /// <summary>0-based cursor column.</summary>
    public int CursorCol { get; private set; }

    public int ScreenRows => _rows;
    public int ScreenCols => _cols;

    /// <summary>True when there are lines that scrolled off and are waiting to be committed.</summary>
    public bool HasScrolledLines => _scrolledOffLines.Count > 0;

    // ─── Constructor ─────────────────────────────────────────────────────────

    public TerminalLineBuffer(int columns = 120)
        : this(columns, Color.FromRgb(0xCD, 0xD6, 0xF4), Color.FromRgb(0x1E, 0x1E, 0x2E)) { }

    public TerminalLineBuffer(int columns, Color defaultFg, Color defaultBg)
    {
        _defaultFg = defaultFg;
        _defaultBg = defaultBg;
        _rows = 30;
        _cols = Math.Max(1, columns);
        _screen = new TerminalCell[_rows, _cols];
        FillEmpty(0, _rows);
    }

    // ─── Screen Fill ─────────────────────────────────────────────────────────

    private void FillEmpty(int fromRow, int toRowExclusive)
    {
        var cell = new TerminalCell { Char = ' ', Foreground = _defaultFg, Background = _defaultBg };
        for (int r = fromRow; r < toRowExclusive; r++)
            for (int c = 0; c < _cols; c++)
                _screen[r, c] = cell;
    }

    private void FillRowEmpty(int row)
    {
        var cell = new TerminalCell { Char = ' ', Foreground = _defaultFg, Background = _defaultBg };
        for (int c = 0; c < _cols; c++)
            _screen[row, c] = cell;
    }

    // ─── Character Write ─────────────────────────────────────────────────────

    /// <summary>Writes a character at the cursor position and advances the cursor.</summary>
    public void Write(char c, Color fg, Color bg, bool bold, bool italic, bool underline)
    {
        if (CursorCol >= _cols)
        {
            // Auto-wrap: move to next line
            CursorCol = 0;
            LineFeedInternal();
        }

        _screen[CursorRow, CursorCol] = new TerminalCell
        {
            Char = c,
            Foreground = fg,
            Background = bg,
            IsBold = bold,
            IsItalic = italic,
            IsUnderline = underline,
        };
        CursorCol++;
    }

    /// <summary>Writes a string starting at the cursor, expanding tabs to multiples of 8.</summary>
    public void Write(string text, Color fg, Color bg, bool bold, bool italic, bool underline)
    {
        foreach (char c in text)
        {
            if (c == '\t')
            {
                int nextTab = ((CursorCol / 8) + 1) * 8;
                while (CursorCol < nextTab)
                    Write(' ', fg, bg, bold, italic, underline);
            }
            else if (!char.IsControl(c))
            {
                Write(c, fg, bg, bold, italic, underline);
            }
        }
    }

    // ─── Cursor Movement ─────────────────────────────────────────────────────

    public void CarriageReturn() => CursorCol = 0;
    public void Backspace() => CursorCol = Math.Max(0, CursorCol - 1);

    /// <summary>Sets cursor to absolute position (0-based, clamped).</summary>
    public void SetCursor(int row, int col)
    {
        CursorRow = Math.Clamp(row, 0, _rows - 1);
        CursorCol = Math.Clamp(col, 0, _cols - 1);
    }

    public void MoveCursorUp(int n = 1)    => CursorRow = Math.Max(0, CursorRow - n);
    public void MoveCursorDown(int n = 1)  => CursorRow = Math.Min(_rows - 1, CursorRow + n);
    public void MoveCursorLeft(int n = 1)  => CursorCol = Math.Max(0, CursorCol - n);
    public void MoveCursorRight(int n = 1) => CursorCol = Math.Min(_cols - 1, CursorCol + n);
    public void MoveCursorToColumn(int col) => CursorCol = Math.Clamp(col, 0, _cols - 1);

    // ─── Line Feed & Scroll ──────────────────────────────────────────────────

    /// <summary>Advances cursor down one row; scrolls if at the bottom.</summary>
    public void LineFeed() => LineFeedInternal();

    private void LineFeedInternal()
    {
        if (CursorRow < _rows - 1)
            CursorRow++;
        else
            ScrollUpBuffer(1);
    }

    private void ScrollUpBuffer(int n)
    {
        for (int i = 0; i < n; i++)
        {
            // Capture the scrolled-off row before overwriting
            var line = new TerminalCell[_cols];
            for (int c = 0; c < _cols; c++)
                line[c] = _screen[0, c];
            _scrolledOffLines.Enqueue(line);

            // Shift every row up by 1
            for (int r = 0; r < _rows - 1; r++)
                for (int c = 0; c < _cols; c++)
                    _screen[r, c] = _screen[r + 1, c];

            FillRowEmpty(_rows - 1);
        }
    }

    private void ScrollDownBuffer(int n)
    {
        for (int i = 0; i < n; i++)
        {
            for (int r = _rows - 1; r > 0; r--)
                for (int c = 0; c < _cols; c++)
                    _screen[r, c] = _screen[r - 1, c];
            FillRowEmpty(0);
        }
    }

    // ─── Erase Operations ────────────────────────────────────────────────────

    /// <summary>
    /// Erase in display (ESC[J).
    /// Mode 0: cursor→end; 1: start→cursor; 2: entire screen; 3: screen+scrollback.
    /// </summary>
    public void EraseDisplay(int mode)
    {
        switch (mode)
        {
            case 0: // cursor to end of screen
                for (int c = CursorCol; c < _cols; c++) _screen[CursorRow, c] = EmptyCell();
                for (int r = CursorRow + 1; r < _rows; r++) FillRowEmpty(r);
                break;

            case 1: // start of screen to cursor
                for (int r = 0; r < CursorRow; r++) FillRowEmpty(r);
                for (int c = 0; c <= CursorCol && c < _cols; c++) _screen[CursorRow, c] = EmptyCell();
                break;

            case 2: // entire screen (cursor stays)
                FillEmpty(0, _rows);
                break;

            case 3: // entire screen + scrollback
                FillEmpty(0, _rows);
                _scrolledOffLines.Clear();
                break;
        }
    }

    /// <summary>
    /// Erase in line (ESC[K).
    /// Mode 0: cursor→end; 1: start→cursor; 2: entire line.
    /// </summary>
    public void EraseLine(int mode)
    {
        switch (mode)
        {
            case 0:
                for (int c = CursorCol; c < _cols; c++) _screen[CursorRow, c] = EmptyCell();
                break;
            case 1:
                for (int c = 0; c <= CursorCol && c < _cols; c++) _screen[CursorRow, c] = EmptyCell();
                break;
            case 2:
                FillRowEmpty(CursorRow);
                break;
        }
    }

    // ─── Line Insert / Delete ────────────────────────────────────────────────

    /// <summary>Insert n blank lines at cursor row, pushing existing lines down (ESC[L).</summary>
    public void InsertLines(int n)
    {
        n = Math.Min(n, _rows - CursorRow);
        for (int r = _rows - 1; r >= CursorRow + n; r--)
            for (int c = 0; c < _cols; c++)
                _screen[r, c] = _screen[r - n, c];
        for (int r = CursorRow; r < CursorRow + n && r < _rows; r++)
            FillRowEmpty(r);
    }

    /// <summary>Delete n lines at cursor row, pulling lines below up (ESC[M).</summary>
    public void DeleteLines(int n)
    {
        n = Math.Min(n, _rows - CursorRow);
        for (int r = CursorRow; r < _rows - n; r++)
            for (int c = 0; c < _cols; c++)
                _screen[r, c] = _screen[r + n, c];
        for (int r = _rows - n; r < _rows; r++)
            FillRowEmpty(r);
    }

    // ─── Character Insert / Delete ───────────────────────────────────────────

    /// <summary>Delete n characters at cursor column, shifting the rest left (ESC[P).</summary>
    public void DeleteChars(int n)
    {
        n = Math.Min(n, _cols - CursorCol);
        if (n <= 0) return;
        for (int c = CursorCol; c < _cols - n; c++)
            _screen[CursorRow, c] = _screen[CursorRow, c + n];
        for (int c = _cols - n; c < _cols; c++)
            _screen[CursorRow, c] = EmptyCell();
    }

    /// <summary>Insert n blank characters at cursor column, shifting the rest right (ESC[@).</summary>
    public void InsertChars(int n)
    {
        if (n <= 0) return;
        for (int c = _cols - 1; c >= CursorCol + n; c--)
            _screen[CursorRow, c] = _screen[CursorRow, c - n];
        for (int c = CursorCol; c < CursorCol + n && c < _cols; c++)
            _screen[CursorRow, c] = EmptyCell();
    }

    // ─── Scroll Sequences ────────────────────────────────────────────────────

    /// <summary>Scroll screen up n lines (ESC[S). Lines scrolled off go to scrollback.</summary>
    public void ScrollUp(int n) => ScrollUpBuffer(n);

    /// <summary>Scroll screen down n lines (ESC[T). Blank lines appear at the top.</summary>
    public void ScrollDown(int n) => ScrollDownBuffer(n);

    // ─── Full Reset ──────────────────────────────────────────────────────────

    /// <summary>Clears the entire screen and scrollback queue, resets cursor to (0,0).</summary>
    public void Clear()
    {
        FillEmpty(0, _rows);
        CursorRow = 0;
        CursorCol = 0;
        _scrolledOffLines.Clear();
    }

    // ─── Resize ──────────────────────────────────────────────────────────────

    /// <summary>Resizes the screen buffer, preserving existing content.</summary>
    public void SetSize(int rows, int cols)
    {
        rows = Math.Max(1, rows);
        cols = Math.Max(1, cols);
        if (rows == _rows && cols == _cols) return;

        var newScreen = new TerminalCell[rows, cols];
        var empty = new TerminalCell { Char = ' ', Foreground = _defaultFg, Background = _defaultBg };

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                newScreen[r, c] = empty;

        int copyRows = Math.Min(rows, _rows);
        int copyCols = Math.Min(cols, _cols);
        for (int r = 0; r < copyRows; r++)
            for (int c = 0; c < copyCols; c++)
                newScreen[r, c] = _screen[r, c];

        _screen = newScreen;
        _rows = rows;
        _cols = cols;
        CursorRow = Math.Clamp(CursorRow, 0, rows - 1);
        CursorCol = Math.Clamp(CursorCol, 0, cols - 1);
    }

    // Keep old single-axis setters for backward compatibility
    public void SetWidth(int cols) => SetSize(_rows, cols);
    public void SetHeight(int rows) => SetSize(rows, _cols);

    // ─── Cell Access (for rendering) ─────────────────────────────────────────

    /// <summary>Returns the cell at a given screen position.</summary>
    public TerminalCell GetCell(int row, int col) => _screen[row, col];

    /// <summary>Dequeues the oldest line that scrolled off the top of the screen.</summary>
    public TerminalCell[] DequeueScrolledLine() => _scrolledOffLines.Dequeue();

    /// <summary>
    /// Returns the index of the last row that contains any non-default character.
    /// Returns 0 (never negative) so at least row 0 is always rendered.
    /// </summary>
    public int GetLastUsedRow()
    {
        var empty = new TerminalCell { Char = ' ', Foreground = _defaultFg, Background = _defaultBg };
        for (int r = _rows - 1; r > 0; r--)
            for (int c = 0; c < _cols; c++)
            {
                ref var cell = ref _screen[r, c];
                if (cell.Char != ' ' || cell.Background != _defaultBg)
                    return r;
            }
        return 0;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private TerminalCell EmptyCell()
        => new() { Char = ' ', Foreground = _defaultFg, Background = _defaultBg };
}
