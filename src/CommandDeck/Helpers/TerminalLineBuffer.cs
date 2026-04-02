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
        Foreground = ThemeColors.CatppuccinText,
        Background = ThemeColors.CatppuccinBase,
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
/// Supports dual-buffer (main + alternate screen), scroll regions (DECSTBM),
/// cursor save/restore, and all standard erase/scroll/insert/delete operations.
/// </summary>
internal sealed class TerminalLineBuffer
{
    // ─── Dual-buffer fields ──────────────────────────────────────────────────
    private TerminalCell[,] _screen;       // active screen (main or alt)
    private TerminalCell[,] _mainScreen;   // main screen buffer
    private TerminalCell[,]? _altScreen;   // alternate screen buffer (lazy)
    private bool _isAltScreen;

    // Saved cursors for screen switching (ESC[?1049h/l)
    private (int Row, int Col) _mainSavedCursor;

    private int _rows;
    private int _cols;

    private readonly Color _defaultFg;
    private readonly Color _defaultBg;

    // Lines that scrolled off the top of the MAIN screen; consumed by AnsiParser for scrollback.
    private readonly Queue<TerminalCell[]> _scrolledOffLines = new();

    // ─── Scroll Region (DECSTBM) ─────────────────────────────────────────────
    private int _scrollTop;
    private int _scrollBottom;

    // ─── Deferred wrap (VT100 pending-wrap state) ────────────────────────────
    // True when cursor is at the last column and the NEXT printable character
    // should wrap to the start of the next line before being written.
    private bool _pendingWrap;
    private bool _savedPendingWrap;

    // ─── Cursor Save/Restore (CSI s / CSI u) ────────────────────────────────
    private int _savedCursorRow;
    private int _savedCursorCol;

    // ─── Public Properties ───────────────────────────────────────────────────

    /// <summary>0-based cursor row (within the visible screen).</summary>
    public int CursorRow { get; private set; }

    /// <summary>0-based cursor column.</summary>
    public int CursorCol { get; private set; }

    public int ScreenRows => _rows;
    public int ScreenCols => _cols;

    /// <summary>True when there are lines that scrolled off and are waiting to be committed.</summary>
    public bool HasScrolledLines => _scrolledOffLines.Count > 0;

    /// <summary>True when the alternate screen buffer is active.</summary>
    public bool IsAltScreen => _isAltScreen;

    // ─── Constructor ─────────────────────────────────────────────────────────

    public TerminalLineBuffer(int columns = 120)
        : this(columns, ThemeColors.CatppuccinText, ThemeColors.CatppuccinBase) { }

    public TerminalLineBuffer(int columns, Color defaultFg, Color defaultBg)
    {
        _defaultFg = defaultFg;
        _defaultBg = defaultBg;
        _rows = 30;
        _cols = Math.Max(1, columns);
        _mainScreen = new TerminalCell[_rows, _cols];
        _screen = _mainScreen;
        FillEmpty(0, _rows);
        _scrollBottom = _rows - 1;
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

    private static void FillBufferEmpty(TerminalCell[,] buf, int rows, int cols, Color fg, Color bg)
    {
        var cell = new TerminalCell { Char = ' ', Foreground = fg, Background = bg };
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                buf[r, c] = cell;
    }

    // ─── Character Write ─────────────────────────────────────────────────────

    /// <summary>Writes a character at the cursor position and advances the cursor.</summary>
    public void Write(char c, Color fg, Color bg, bool bold, bool italic, bool underline)
    {
        // Deferred wrap: if the previous write hit the last column, wrap NOW
        // before writing the new character (VT100 pending-wrap behavior).
        if (_pendingWrap)
        {
            _pendingWrap = false;
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

        if (CursorCol + 1 < _cols)
            CursorCol++;
        else
            _pendingWrap = true; // at last column — defer wrap to keep cursor in-bounds
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

    public void CarriageReturn() { _pendingWrap = false; CursorCol = 0; }
    public void Backspace()      { _pendingWrap = false; CursorCol = Math.Max(0, CursorCol - 1); }

    /// <summary>Sets cursor to absolute position (0-based, clamped).</summary>
    public void SetCursor(int row, int col)
    {
        _pendingWrap = false;
        CursorRow = Math.Clamp(row, 0, _rows - 1);
        CursorCol = Math.Clamp(col, 0, _cols - 1);
    }

    public void MoveCursorUp(int n = 1)    { _pendingWrap = false; CursorRow = Math.Max(0, CursorRow - n); }
    public void MoveCursorDown(int n = 1)  { _pendingWrap = false; CursorRow = Math.Min(_rows - 1, CursorRow + n); }
    public void MoveCursorLeft(int n = 1)  { _pendingWrap = false; CursorCol = Math.Max(0, CursorCol - n); }
    public void MoveCursorRight(int n = 1) { _pendingWrap = false; CursorCol = Math.Min(_cols - 1, CursorCol + n); }
    public void MoveCursorToColumn(int col) { _pendingWrap = false; CursorCol = Math.Clamp(col, 0, _cols - 1); }

    // ─── Cursor Save/Restore ─────────────────────────────────────────────────

    /// <summary>Saves current cursor position and pending-wrap state (CSI s / ESC 7).</summary>
    public void SaveCursor()
    {
        _savedCursorRow  = CursorRow;
        _savedCursorCol  = CursorCol;
        _savedPendingWrap = _pendingWrap;
    }

    /// <summary>Restores previously saved cursor position and pending-wrap state (CSI u / ESC 8).</summary>
    public void RestoreCursor()
    {
        CursorRow    = Math.Clamp(_savedCursorRow, 0, _rows - 1);
        CursorCol    = Math.Clamp(_savedCursorCol, 0, _cols - 1);
        _pendingWrap = _savedPendingWrap;
    }

    // ─── Alternate Screen Buffer ─────────────────────────────────────────────

    /// <summary>
    /// Switches to the alternate screen buffer (ESC[?1049h / ESC[?47h).
    /// Saves the main cursor position, clears the alt buffer, resets cursor to (0,0).
    /// </summary>
    public void SwitchToAltScreen()
    {
        if (_isAltScreen) return;

        _mainSavedCursor = (CursorRow, CursorCol);
        _pendingWrap = false;

        // Allocate or reuse the alt buffer at current dimensions
        if (_altScreen == null || _altScreen.GetLength(0) != _rows || _altScreen.GetLength(1) != _cols)
            _altScreen = new TerminalCell[_rows, _cols];

        // Always start with a clean alt screen
        FillBufferEmpty(_altScreen, _rows, _cols, _defaultFg, _defaultBg);

        _screen = _altScreen;
        _isAltScreen = true;
        CursorRow = 0;
        CursorCol = 0;
        _scrollTop = 0;
        _scrollBottom = _rows - 1;
    }

    /// <summary>
    /// Returns to the main screen buffer (ESC[?1049l / ESC[?47l).
    /// Restores the main cursor position saved on entry.
    /// </summary>
    public void SwitchToMainScreen()
    {
        if (!_isAltScreen) return;

        _screen = _mainScreen;
        _isAltScreen = false;

        CursorRow    = Math.Clamp(_mainSavedCursor.Row, 0, _rows - 1);
        CursorCol    = Math.Clamp(_mainSavedCursor.Col, 0, _cols - 1);
        _pendingWrap = false;
        _scrollTop   = 0;
        _scrollBottom = _rows - 1;
    }

    // ─── Scroll Region (DECSTBM) ─────────────────────────────────────────────

    /// <summary>
    /// Sets the scrolling region (ESC[top;bottomr).  Both indices are 0-based.
    /// If top >= bottom the region resets to the full screen.
    /// </summary>
    public void SetScrollRegion(int top, int bottom)
    {
        _scrollTop    = Math.Clamp(top,    0, _rows - 1);
        _scrollBottom = Math.Clamp(bottom, 0, _rows - 1);
        if (_scrollTop >= _scrollBottom)
        {
            _scrollTop    = 0;
            _scrollBottom = _rows - 1;
        }
    }

    // ─── Line Feed & Scroll ──────────────────────────────────────────────────

    /// <summary>Advances cursor down one row; scrolls the active region if at the bottom margin.</summary>
    public void LineFeed() => LineFeedInternal();

    private void LineFeedInternal()
    {
        if (CursorRow == _scrollBottom)
            // At the bottom margin: scroll the region
            ScrollUpRegion(1);
        else if (CursorRow < _rows - 1)
            // Inside or above the region: simply move down
            CursorRow++;
        // Below _scrollBottom but at _rows-1: cursor stays (no scroll outside region)
    }

    /// <summary>
    /// Scrolls the scroll region up by n lines.
    /// Lines that leave the top of the region are captured to scrollback only when
    /// the region starts at row 0 and the buffer is in main-screen mode.
    /// </summary>
    private void ScrollUpRegion(int n)
    {
        for (int i = 0; i < n; i++)
        {
            // Capture the leaving row to scrollback only for main-screen full-screen scrolls
            if (_scrollTop == 0 && !_isAltScreen)
            {
                var line = new TerminalCell[_cols];
                for (int c = 0; c < _cols; c++)
                    line[c] = _screen[_scrollTop, c];
                _scrolledOffLines.Enqueue(line);
            }

            // Shift rows up within the region
            for (int r = _scrollTop; r < _scrollBottom; r++)
                for (int c = 0; c < _cols; c++)
                    _screen[r, c] = _screen[r + 1, c];

            FillRowEmpty(_scrollBottom);
        }
    }

    /// <summary>Scrolls the scroll region down by n lines; blank lines appear at the top margin.</summary>
    private void ScrollDownRegion(int n)
    {
        for (int i = 0; i < n; i++)
        {
            for (int r = _scrollBottom; r > _scrollTop; r--)
                for (int c = 0; c < _cols; c++)
                    _screen[r, c] = _screen[r - 1, c];

            FillRowEmpty(_scrollTop);
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

    /// <summary>Insert n blank lines at cursor row within the scroll region (ESC[L).</summary>
    public void InsertLines(int n)
    {
        int regionBottom = _scrollBottom;
        n = Math.Min(n, regionBottom - CursorRow + 1);
        if (n <= 0) return;

        for (int r = regionBottom; r >= CursorRow + n; r--)
            for (int c = 0; c < _cols; c++)
                _screen[r, c] = _screen[r - n, c];

        for (int r = CursorRow; r < CursorRow + n && r <= regionBottom; r++)
            FillRowEmpty(r);
    }

    /// <summary>Delete n lines at cursor row within the scroll region (ESC[M).</summary>
    public void DeleteLines(int n)
    {
        int regionBottom = _scrollBottom;
        n = Math.Min(n, regionBottom - CursorRow + 1);
        if (n <= 0) return;

        for (int r = CursorRow; r <= regionBottom - n; r++)
            for (int c = 0; c < _cols; c++)
                _screen[r, c] = _screen[r + n, c];

        for (int r = regionBottom - n + 1; r <= regionBottom; r++)
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

    /// <summary>Scroll region up n lines (ESC[S). Respects DECSTBM scroll region.</summary>
    public void ScrollUp(int n) => ScrollUpRegion(n);

    /// <summary>Scroll region down n lines (ESC[T). Respects DECSTBM scroll region.</summary>
    public void ScrollDown(int n) => ScrollDownRegion(n);

    // ─── Full Reset ──────────────────────────────────────────────────────────

    /// <summary>Clears the active screen and scrollback queue, resets cursor to (0,0).</summary>
    public void Clear()
    {
        FillEmpty(0, _rows);
        CursorRow    = 0;
        CursorCol    = 0;
        _pendingWrap = false;
        _scrolledOffLines.Clear();
    }

    // ─── Resize ──────────────────────────────────────────────────────────────

    /// <summary>Resizes the screen buffer(s), preserving existing content.</summary>
    public void SetSize(int rows, int cols)
    {
        rows = Math.Max(1, rows);
        cols = Math.Max(1, cols);
        if (rows == _rows && cols == _cols) return;

        _mainScreen = ResizeBuffer(_mainScreen, _rows, _cols, rows, cols);

        if (_altScreen != null)
            _altScreen = ResizeBuffer(_altScreen, _rows, _cols, rows, cols);

        // Re-point _screen to the correct (now resized) buffer
        _screen = _isAltScreen ? _altScreen! : _mainScreen;

        _rows = rows;
        _cols = cols;

        // Reset scroll region to full screen after resize
        _scrollTop    = 0;
        _scrollBottom = rows - 1;

        CursorRow    = Math.Clamp(CursorRow, 0, rows - 1);
        CursorCol    = Math.Clamp(CursorCol, 0, cols - 1);
        _pendingWrap = false;
    }

    private TerminalCell[,] ResizeBuffer(TerminalCell[,] old, int oldRows, int oldCols, int newRows, int newCols)
    {
        var newBuf = new TerminalCell[newRows, newCols];
        var empty  = new TerminalCell { Char = ' ', Foreground = _defaultFg, Background = _defaultBg };

        for (int r = 0; r < newRows; r++)
            for (int c = 0; c < newCols; c++)
                newBuf[r, c] = empty;

        int copyRows = Math.Min(newRows, oldRows);
        int copyCols = Math.Min(newCols, oldCols);
        for (int r = 0; r < copyRows; r++)
            for (int c = 0; c < copyCols; c++)
                newBuf[r, c] = old[r, c];

        return newBuf;
    }

    // Keep old single-axis setters for backward compatibility
    public void SetWidth(int cols) => SetSize(_rows, cols);
    public void SetHeight(int rows) => SetSize(rows, _cols);

    // ─── Cell Access (for rendering) ─────────────────────────────────────────

    /// <summary>Returns the cell at a given screen position.</summary>
    public TerminalCell GetCell(int row, int col) => _screen[row, col];

    /// <summary>Dequeues the oldest line that scrolled off the top of the main screen.</summary>
    public TerminalCell[] DequeueScrolledLine() => _scrolledOffLines.Dequeue();

    /// <summary>
    /// Returns the index of the last row that contains any non-default character.
    /// Returns 0 (never negative) so at least row 0 is always rendered.
    /// </summary>
    public int GetLastUsedRow()
    {
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
