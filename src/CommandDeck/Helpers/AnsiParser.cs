using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace CommandDeck.Helpers;

/// <summary>
/// Parses VT100/ANSI escape sequences from terminal output and renders directly
/// into a WPF <see cref="FlowDocument"/> using a full 2-D screen buffer.
/// Supports: SGR colors (standard, 256-color, true color), bold, italic, underline,
/// full cursor addressing (ESC[H), all erase modes, scroll/insert/delete line ops,
/// and OSC title sequences.
/// </summary>
public class AnsiParser
{
    // ─── ANSI Standard Colors (indices 0–7) ─────────────────────────────────
    private static readonly Color[] StandardColors =
    {
        Color.FromRgb(0x1E, 0x1E, 0x2E), // 0 Black
        Color.FromRgb(0xF3, 0x8B, 0xA8), // 1 Red
        Color.FromRgb(0xA6, 0xE3, 0xA1), // 2 Green
        Color.FromRgb(0xF9, 0xE2, 0xAF), // 3 Yellow
        Color.FromRgb(0x89, 0xB4, 0xFA), // 4 Blue
        Color.FromRgb(0xCB, 0xA6, 0xF7), // 5 Magenta
        Color.FromRgb(0x94, 0xE2, 0xD5), // 6 Cyan
        Color.FromRgb(0xCD, 0xD6, 0xF4), // 7 White
    };

    // ─── ANSI Bright Colors (indices 8–15) ──────────────────────────────────
    private static readonly Color[] BrightColors =
    {
        Color.FromRgb(0x58, 0x5B, 0x70), // 8 Bright Black
        Color.FromRgb(0xF3, 0x8B, 0xA8), // 9 Bright Red
        Color.FromRgb(0xA6, 0xE3, 0xA1), // 10 Bright Green
        Color.FromRgb(0xF9, 0xE2, 0xAF), // 11 Bright Yellow
        Color.FromRgb(0x89, 0xB4, 0xFA), // 12 Bright Blue
        Color.FromRgb(0xCB, 0xA6, 0xF7), // 13 Bright Magenta
        Color.FromRgb(0x94, 0xE2, 0xD5), // 14 Bright Cyan
        Color.FromRgb(0xFF, 0xFF, 0xFF), // 15 Bright White
    };

    // ─── SGR State ───────────────────────────────────────────────────────────
    private Color _foreground;
    private Color _background;
    private bool _isBold;
    private bool _isItalic;
    private bool _isUnderline;
    private bool _isDim;
    private bool _isStrikethrough;
    private bool _isInverse;

    private readonly Color _defaultForeground;
    private readonly Color _defaultBackground;

    private static readonly Color FallbackFg = Color.FromRgb(0xCD, 0xD6, 0xF4);
    private static readonly Color FallbackBg = Color.FromRgb(0x1E, 0x1E, 0x2E);

    // ─── 2-D Screen Buffer ───────────────────────────────────────────────────
    private readonly TerminalLineBuffer _lineBuffer;

    // ─── Document Ownership ─────────────────────────────────────────────────
    private FlowDocument? _doc;
    private Paragraph? _screenParagraph;      // Always the last Block; rebuilt on every render
    private const int MaxScrollbackParagraphs = 2000;

    // ─── Incomplete Escape Buffer ────────────────────────────────────────────
    private string _pendingInput = string.Empty;

    // ─── Regex ───────────────────────────────────────────────────────────────
    private static readonly Regex CsiRegex = new(
        @"\x1B\[(?<params>[0-9;]*?)(?<final>[A-Za-z@`])",
        RegexOptions.Compiled);

    private static readonly Regex OscRegex = new(
        @"\x1B\](?<id>\d+);(?<text>[^\x07\x1B]*?)(?:\x07|\x1B\\)",
        RegexOptions.Compiled);

    private static readonly Regex CharsetSelectRegex  = new(@"\x1B[()][AB012]", RegexOptions.Compiled);
    private static readonly Regex KeypadModeRegex     = new(@"\x1B[=>]",        RegexOptions.Compiled);
    private static readonly Regex DecPrivateRegex     = new(@"\x1B\x5B\?[0-9;]*[A-Za-z@`]", RegexOptions.Compiled);
    private static readonly Regex PrivateCsiRegex     = new(@"\x1B\[[><!=][0-9;]*[A-Za-z@`]", RegexOptions.Compiled);
    private static readonly Regex DecSaveRestoreRegex = new(@"\x1B[78]", RegexOptions.Compiled);

    // ─── Brush Cache ─────────────────────────────────────────────────────────
    private static readonly Dictionary<Color, SolidColorBrush> BrushCache = new();

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Fires when an OSC title-change sequence is received.</summary>
    public event Action<string>? TitleChanged;

    /// <summary>Fires when a bell character is received.</summary>
    public event Action? BellReceived;

    /// <summary>Current cursor column (0-based).</summary>
    public int CursorColumn => _lineBuffer.CursorCol;

    /// <summary>Current cursor row (0-based) within the visible screen.</summary>
    public int CursorRow => _lineBuffer.CursorRow;

    public AnsiParser() : this(FallbackFg, FallbackBg) { }

    public AnsiParser(Color defaultForeground, Color defaultBackground)
    {
        _defaultForeground = defaultForeground;
        _defaultBackground = defaultBackground;
        _foreground = defaultForeground;
        _background = defaultBackground;
        _lineBuffer = new TerminalLineBuffer(120, defaultForeground, defaultBackground);
    }

    // ─── Document Initialization ─────────────────────────────────────────────

    /// <summary>
    /// Connects this parser to a <see cref="FlowDocument"/>.
    /// Must be called once before <see cref="ParseAndRender"/>.
    /// Clears any existing blocks and adds the screen paragraph.
    /// </summary>
    public void Initialize(FlowDocument doc)
    {
        _doc = doc;
        _doc.Blocks.Clear();
        _screenParagraph = new Paragraph { Margin = new Thickness(0) };
        _doc.Blocks.Add(_screenParagraph);
    }

    // ─── Main Render Entry Point ─────────────────────────────────────────────

    /// <summary>
    /// Parses ANSI sequences from <paramref name="input"/>, writes characters
    /// to the 2-D screen buffer, then rebuilds the document's screen paragraph.
    /// </summary>
    public void ParseAndRender(string input)
    {
        if (_doc == null || _screenParagraph == null) return;
        if (string.IsNullOrEmpty(input) && _pendingInput.Length == 0) return;

        // Re-attach leftover partial escape from previous chunk
        if (_pendingInput.Length > 0)
        {
            input = _pendingInput + input;
            _pendingInput = string.Empty;
        }

        // Save any trailing partial escape for next call
        int trailingEsc = FindTrailingPartialEscape(input);
        if (trailingEsc >= 0)
        {
            _pendingInput = input[trailingEsc..];
            input = input[..trailingEsc];
            if (input.Length == 0) return;
        }

        // Strip OSC sequences (title changes handled via TitleChanged event)
        input = OscRegex.Replace(input, match =>
        {
            if (int.TryParse(match.Groups["id"].Value, out int id))
            {
                string text = match.Groups["text"].Value;
                if (id is 0 or 2) TitleChanged?.Invoke(text);
            }
            return string.Empty;
        });

        // Strip non-CSI sequences we don't handle
        input = CharsetSelectRegex.Replace(input, "");
        input = KeypadModeRegex.Replace(input, "");
        // DEC private sequences: handle alt-screen switches, strip the rest
        input = DecPrivateRegex.Replace(input, m => HandleDecPrivateMode(m.Value));
        input = PrivateCsiRegex.Replace(input, "");
        // ESC 7 / ESC 8: DECSC / DECRC (save/restore cursor)
        input = DecSaveRestoreRegex.Replace(input, m => {
            if (m.Value[1] == '7') _lineBuffer.SaveCursor();
            else                   _lineBuffer.RestoreCursor();
            return string.Empty;
        });

        if (input.Contains('\x07'))
        {
            BellReceived?.Invoke();
            input = input.Replace("\x07", "");
        }

        // Process: write text to buffer, apply CSI sequences to buffer
        int lastIndex = 0;
        foreach (Match match in CsiRegex.Matches(input))
        {
            if (match.Index > lastIndex)
                WriteTextToBuffer(input[lastIndex..match.Index]);

            ProcessCsi(match.Groups["params"].Value, match.Groups["final"].Value[0]);
            lastIndex = match.Index + match.Length;
        }
        if (lastIndex < input.Length)
            WriteTextToBuffer(input[lastIndex..]);

        // Commit lines that scrolled off the top to scrollback paragraphs
        CommitScrolledLines();

        // Rebuild the screen paragraph from the current buffer state
        RenderScreenToDocument();

        // Limit scrollback memory
        TrimScrollback();
    }

    /// <summary>
    /// Parses a string and returns a flat list of WPF <see cref="Inline"/> elements.
    /// Used for one-shot rendering outside of the document (e.g. tooltips).
    /// </summary>
    public List<Inline> Parse(string input)
    {
        var inlines = new List<Inline>();
        if (string.IsNullOrEmpty(input)) return inlines;

        input = OscRegex.Replace(input, match =>
        {
            if (int.TryParse(match.Groups["id"].Value, out int id))
            {
                string text = match.Groups["text"].Value;
                if (id is 0 or 2) TitleChanged?.Invoke(text);
            }
            return string.Empty;
        });

        input = CharsetSelectRegex.Replace(input, "");
        input = KeypadModeRegex.Replace(input, "");
        input = DecPrivateRegex.Replace(input, "");
        input = PrivateCsiRegex.Replace(input, "");

        if (input.Contains('\x07'))
        {
            BellReceived?.Invoke();
            input = input.Replace("\x07", "");
        }

        int lastIndex = 0;
        foreach (Match match in CsiRegex.Matches(input))
        {
            if (match.Index > lastIndex)
                AppendTextAsInlines(input[lastIndex..match.Index], inlines);
            ProcessCsiSequence(match.Groups["params"].Value, match.Groups["final"].Value[0]);
            lastIndex = match.Index + match.Length;
        }
        if (lastIndex < input.Length)
            AppendTextAsInlines(input[lastIndex..], inlines);

        return inlines;
    }

    // ─── DEC Private Mode Handler ────────────────────────────────────────────

    /// <summary>
    /// Processes DEC private mode sequences (ESC[?...h/l).
    /// Handles alternate screen switching; all other modes are silently consumed.
    /// </summary>
    private string HandleDecPrivateMode(string seq)
    {
        // seq format: ESC [ ? <params> <finalChar>
        // indices:    0   1 2  3..^1    ^1
        if (seq.Length < 4) return string.Empty;

        char finalChar = seq[^1];
        string paramStr = seq[3..^1];

        foreach (var part in paramStr.Split(';'))
        {
            if (!int.TryParse(part, out int mode)) continue;

            switch (finalChar)
            {
                case 'h': // Set mode
                    if (mode is 1049 or 1047 or 47)
                    {
                        _lineBuffer.SwitchToAltScreen();
                        ResetFormatting();
                    }
                    break;

                case 'l': // Reset mode
                    if (mode is 1049 or 1047 or 47)
                        _lineBuffer.SwitchToMainScreen();
                    break;
            }
        }

        return string.Empty;
    }

    // ─── Reset & Resize ──────────────────────────────────────────────────────

    /// <summary>Full reset: clears formatting, buffer state, and rebuilds the document.</summary>
    public void Reset()
    {
        ResetFormatting();
        _pendingInput = string.Empty;
        // If on alt screen, switch back to main before clearing
        if (_lineBuffer.IsAltScreen)
            _lineBuffer.SwitchToMainScreen();
        _lineBuffer.Clear();
        if (_doc != null) Initialize(_doc);
    }

    /// <summary>Resizes the buffer to match the terminal's new column count.</summary>
    public void SetColumns(int columns) => SetSize(columns, _lineBuffer.ScreenRows);

    /// <summary>Resizes the buffer to match both terminal width and height.</summary>
    public void SetSize(int columns, int rows)
    {
        _lineBuffer.SetSize(rows, columns);
    }

    // ─── Internal: Write Text to Buffer ──────────────────────────────────────

    private void WriteTextToBuffer(string text)
    {
        var fg = _isInverse ? _background : _foreground;
        var bg = _isInverse ? _foreground : _background;
        if (_isDim) fg = Color.FromArgb(178, fg.R, fg.G, fg.B);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            switch (c)
            {
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        _lineBuffer.CarriageReturn();
                        _lineBuffer.LineFeed();
                        i++; // skip the \n
                    }
                    else
                    {
                        _lineBuffer.CarriageReturn();
                    }
                    break;

                case '\n':
                    _lineBuffer.LineFeed();
                    break;

                case '\x08': // Backspace
                    _lineBuffer.Backspace();
                    break;

                case '\t':
                    int nextTab = ((_lineBuffer.CursorCol / 8) + 1) * 8;
                    while (_lineBuffer.CursorCol < nextTab)
                        _lineBuffer.Write(' ', fg, bg, _isBold, _isItalic, _isUnderline);
                    break;

                default:
                    if (!char.IsControl(c))
                        _lineBuffer.Write(c, fg, bg, _isBold, _isItalic, _isUnderline);
                    break;
            }
        }
    }

    // ─── Internal: CSI Processing ────────────────────────────────────────────

    private void ProcessCsi(string paramString, char finalChar)
    {
        int n = string.IsNullOrEmpty(paramString) ? 1
              : int.TryParse(paramString, out int p) ? p : 1;

        switch (finalChar)
        {
            case 'm': // SGR
                ProcessSgr(paramString);
                break;

            case 'J': // Erase in Display (all 4 modes)
            {
                int mode = string.IsNullOrEmpty(paramString) ? 0
                         : (int.TryParse(paramString, out int jp) ? jp : 0);
                _lineBuffer.EraseDisplay(mode);
                break;
            }

            case 'K': // Erase in Line (all 3 modes)
            {
                int mode = string.IsNullOrEmpty(paramString) ? 0
                         : (int.TryParse(paramString, out int kp) ? kp : 0);
                _lineBuffer.EraseLine(mode);
                break;
            }

            case 'H': case 'f': // Cursor Position (1-based row;col)
            {
                var parts = (paramString ?? "").Split(';');
                int row = parts.Length >= 1 && int.TryParse(parts[0], out int r) && r > 0 ? r : 1;
                int col = parts.Length >= 2 && int.TryParse(parts[1], out int c) && c > 0 ? c : 1;
                _lineBuffer.SetCursor(row - 1, col - 1);
                break;
            }

            case 'A': _lineBuffer.MoveCursorUp(n);    break; // Cursor Up
            case 'B': _lineBuffer.MoveCursorDown(n);  break; // Cursor Down
            case 'C': _lineBuffer.MoveCursorRight(n); break; // Cursor Forward
            case 'D': _lineBuffer.MoveCursorLeft(n);  break; // Cursor Back

            case 'G': _lineBuffer.MoveCursorToColumn(n - 1); break; // Cursor Horizontal Absolute

            case 'P': _lineBuffer.DeleteChars(n);  break; // Delete Characters
            case '@': _lineBuffer.InsertChars(n);  break; // Insert Characters
            case 'L': _lineBuffer.InsertLines(n);  break; // Insert Lines
            case 'M': _lineBuffer.DeleteLines(n);  break; // Delete Lines
            case 'S': _lineBuffer.ScrollUp(n);     break; // Scroll Up
            case 'T': _lineBuffer.ScrollDown(n);   break; // Scroll Down

            case 'r': // DECSTBM - Set Scroll Region (top;bottom, 1-based)
            {
                var parts = (paramString ?? "").Split(';');
                int top    = parts.Length >= 1 && int.TryParse(parts[0], out int t) && t > 0 ? t : 1;
                int bottom = parts.Length >= 2 && int.TryParse(parts[1], out int b) && b > 0 ? b : _lineBuffer.ScreenRows;
                _lineBuffer.SetScrollRegion(top - 1, bottom - 1);
                _lineBuffer.SetCursor(0, 0); // DECSTBM moves cursor to home
                break;
            }

            case 's': _lineBuffer.SaveCursor();    break; // Save Cursor Position
            case 'u': _lineBuffer.RestoreCursor(); break; // Restore Cursor Position
        }
    }

    // ─── Internal: Scrollback & Screen Render ────────────────────────────────

    /// <summary>
    /// Transfers lines that scrolled off the top of the main screen to scrollback Paragraphs
    /// inserted before the screen paragraph. No-op while the alternate screen is active.
    /// </summary>
    private void CommitScrolledLines()
    {
        if (_doc == null || _screenParagraph == null) return;
        if (_lineBuffer.IsAltScreen) return; // alt screen never produces scrollback
        while (_lineBuffer.HasScrolledLines)
        {
            var cells = _lineBuffer.DequeueScrolledLine();
            var para = new Paragraph { Margin = new Thickness(0) };
            RenderCellsToInlines(cells, cells.Length, para);
            _doc.Blocks.InsertBefore(_screenParagraph, para);
        }
    }

    /// <summary>
    /// Clears and rebuilds the screen paragraph from the current 2-D buffer state.
    /// Renders rows 0 through the last row that contains any non-default content.
    /// </summary>
    private void RenderScreenToDocument()
    {
        if (_screenParagraph == null) return;

        _screenParagraph.Inlines.Clear();

        // On alt screen render the full grid so TUI apps have a clean fixed-size canvas.
        // On main screen clamp to screen height to avoid ghost rows from stale buffer data.
        int lastRow = _lineBuffer.IsAltScreen
            ? _lineBuffer.ScreenRows - 1
            : Math.Min(_lineBuffer.ScreenRows - 1,
                       Math.Max(_lineBuffer.CursorRow, _lineBuffer.GetLastUsedRow()));

        for (int r = 0; r <= lastRow; r++)
        {
            if (r > 0) _screenParagraph.Inlines.Add(new LineBreak());
            RenderScreenRowToInlines(r, _screenParagraph);
        }
    }

    private void RenderScreenRowToInlines(int row, Paragraph para)
    {
        int cols = _lineBuffer.ScreenCols;

        // Find logical end of the row (trim trailing default spaces)
        int len = cols;
        while (len > 0)
        {
            var cell = _lineBuffer.GetCell(row, len - 1);
            if (cell.Char != ' ' || cell.Background != _defaultBackground)
                break;
            len--;
        }
        if (len == 0) return;

        int start = 0;
        while (start < len)
        {
            var fmt = _lineBuffer.GetCell(row, start);
            int end = start + 1;
            while (end < len && _lineBuffer.GetCell(row, end).SameFormat(fmt))
                end++;

            var chars = new char[end - start];
            for (int i = start; i < end; i++)
                chars[i - start] = _lineBuffer.GetCell(row, i).Char;

            para.Inlines.Add(BuildRun(new string(chars), fmt));
            start = end;
        }
    }

    private void RenderCellsToInlines(TerminalCell[] cells, int totalLen, Paragraph para)
    {
        int len = totalLen;
        while (len > 0)
        {
            if (cells[len - 1].Char != ' ' || cells[len - 1].Background != _defaultBackground)
                break;
            len--;
        }
        if (len == 0) return;

        int start = 0;
        while (start < len)
        {
            var fmt = cells[start];
            int end = start + 1;
            while (end < len && cells[end].SameFormat(fmt))
                end++;

            var chars = new char[end - start];
            for (int i = start; i < end; i++) chars[i - start] = cells[i].Char;
            para.Inlines.Add(BuildRun(new string(chars), fmt));
            start = end;
        }
    }

    private void TrimScrollback()
    {
        if (_doc == null || _screenParagraph == null) return;
        // -1: don't count the screen paragraph itself
        while (_doc.Blocks.Count - 1 > MaxScrollbackParagraphs)
            _doc.Blocks.Remove(_doc.Blocks.FirstBlock!);
    }

    // ─── Run Builder ─────────────────────────────────────────────────────────

    private Run BuildRun(string text, TerminalCell fmt)
    {
        var run = new Run(text);
        if (fmt.Foreground != _defaultForeground) run.Foreground = GetBrush(fmt.Foreground);
        if (fmt.Background != _defaultBackground) run.Background = GetBrush(fmt.Background);
        if (fmt.IsBold)      run.FontWeight = FontWeights.Bold;
        if (fmt.IsItalic)    run.FontStyle  = FontStyles.Italic;
        if (fmt.IsUnderline) run.TextDecorations = TextDecorations.Underline;
        return run;
    }

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

    // ─── Parse (flat inlines path) ───────────────────────────────────────────

    private void AppendTextAsInlines(string text, List<Inline> inlines)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == '\x08')
            {
                if (sb.Length > 0) sb.Length--;
                else if (inlines.Count > 0 && inlines[^1] is Run r)
                {
                    if (r.Text.Length > 1) r.Text = r.Text[..^1];
                    else inlines.RemoveAt(inlines.Count - 1);
                }
            }
            else if (c == '\n' || c == '\r' || c == '\t' || !char.IsControl(c))
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0) inlines.Add(CreateInlineRun(sb.ToString()));
    }

    private Run CreateInlineRun(string text)
    {
        var fg = _isInverse ? _background : _foreground;
        var bg = _isInverse ? _foreground : _background;
        if (_isDim) fg = Color.FromArgb(178, fg.R, fg.G, fg.B);

        var run = new Run(text)
        {
            Foreground = GetBrush(fg),
            FontWeight = _isBold  ? FontWeights.Bold   : FontWeights.Normal,
            FontStyle  = _isItalic ? FontStyles.Italic : FontStyles.Normal,
        };
        if (bg != _defaultBackground) run.Background = GetBrush(bg);
        if (_isUnderline) run.TextDecorations = TextDecorations.Underline;
        if (_isStrikethrough)
        {
            run.TextDecorations = run.TextDecorations != null
                ? new TextDecorationCollection(run.TextDecorations.Concat(TextDecorations.Strikethrough))
                : TextDecorations.Strikethrough;
        }
        return run;
    }

    // Stateless CSI handler for the flat Parse() path
    private void ProcessCsiSequence(string paramString, char finalChar)
    {
        if (finalChar == 'm') ProcessSgr(paramString);
        // Cursor movement and erase have no effect on the flat-inline path
    }

    // ─── SGR ─────────────────────────────────────────────────────────────────

    private void ResetFormatting()
    {
        _foreground     = _defaultForeground;
        _background     = _defaultBackground;
        _isBold         = false;
        _isItalic       = false;
        _isUnderline    = false;
        _isDim          = false;
        _isStrikethrough = false;
        _isInverse      = false;
    }

    private void ProcessSgr(string paramString)
    {
        if (string.IsNullOrEmpty(paramString) || paramString == "0") { ResetFormatting(); return; }

        var parts = paramString.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out int code)) continue;
            switch (code)
            {
                case 0:  ResetFormatting(); break;
                case 1:  _isBold = true;    break;
                case 2:  _isDim  = true;    break;
                case 3:  _isItalic = true;  break;
                case 4:  _isUnderline = true; break;
                case 7:  _isInverse = true; break;
                case 9:  _isStrikethrough = true; break;
                case 21: _isBold = false;   break;
                case 22: _isBold = false; _isDim = false; break;
                case 23: _isItalic = false; break;
                case 24: _isUnderline = false; break;
                case 27: _isInverse = false; break;
                case 29: _isStrikethrough = false; break;

                case >= 30 and <= 37:
                    _foreground = StandardColors[code - 30];
                    if (_isBold) _foreground = BrightColors[code - 30];
                    break;
                case 38: i = ParseExtendedColor(parts, i, isForeground: true);  break;
                case 39: _foreground = _defaultForeground; break;
                case >= 40 and <= 47: _background = StandardColors[code - 40]; break;
                case 48: i = ParseExtendedColor(parts, i, isForeground: false); break;
                case 49: _background = _defaultBackground; break;
                case >= 90 and <= 97:  _foreground = BrightColors[code - 90];  break;
                case >= 100 and <= 107: _background = BrightColors[code - 100]; break;
            }
        }
    }

    private int ParseExtendedColor(string[] parts, int i, bool isForeground)
    {
        if (i + 1 >= parts.Length) return i;
        if (!int.TryParse(parts[i + 1], out int mode)) return i;

        switch (mode)
        {
            case 5: // 256-color
                if (i + 2 < parts.Length && int.TryParse(parts[i + 2], out int idx))
                {
                    var col = Get256Color(idx);
                    if (isForeground) _foreground = col; else _background = col;
                    return i + 2;
                }
                return i + 1;

            case 2: // True color
                if (i + 4 < parts.Length
                    && int.TryParse(parts[i + 2], out int r)
                    && int.TryParse(parts[i + 3], out int g)
                    && int.TryParse(parts[i + 4], out int b))
                {
                    var col = Color.FromRgb(
                        (byte)Math.Clamp(r, 0, 255),
                        (byte)Math.Clamp(g, 0, 255),
                        (byte)Math.Clamp(b, 0, 255));
                    if (isForeground) _foreground = col; else _background = col;
                    return i + 4;
                }
                return i + 1;
        }
        return i;
    }

    private static Color Get256Color(int index)
    {
        index = Math.Clamp(index, 0, 255);
        if (index < 8)   return StandardColors[index];
        if (index < 16)  return BrightColors[index - 8];
        if (index < 232)
        {
            int v = index - 16;
            int r = v / 36, gg = (v % 36) / 6, b = v % 6;
            return Color.FromRgb(
                (byte)(r  > 0 ? 55 + r  * 40 : 0),
                (byte)(gg > 0 ? 55 + gg * 40 : 0),
                (byte)(b  > 0 ? 55 + b  * 40 : 0));
        }
        int gray = Math.Clamp(8 + (index - 232) * 10, 0, 255);
        return Color.FromRgb((byte)gray, (byte)gray, (byte)gray);
    }

    // ─── Partial Escape Detection ────────────────────────────────────────────

    private static int FindTrailingPartialEscape(string input)
    {
        int lastEsc = input.LastIndexOf('\x1B');
        if (lastEsc < 0) return -1;

        string tail = input[lastEsc..];
        if (tail.Length == 1) return lastEsc;

        char second = tail[1];

        if (second == '[')
        {
            for (int i = 2; i < tail.Length; i++)
            {
                char c = tail[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '@' || c == '`')
                    return -1;
                if (c != ';' && (c < '0' || c > '9') && c != '?' && c != '>' && c != '<' && c != '!' && c != '=')
                    return -1;
            }
            return lastEsc;
        }

        if (second == ']')
        {
            if (tail.Contains('\x07') || tail.Contains("\x1B\\")) return -1;
            return lastEsc;
        }

        if (tail.Length >= 2) return -1;
        return lastEsc;
    }
}
