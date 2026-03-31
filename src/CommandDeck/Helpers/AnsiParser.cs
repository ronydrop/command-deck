using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace CommandDeck.Helpers;

/// <summary>
/// Parses VT100/ANSI escape sequences from terminal output and produces
/// WPF rich text (Inline elements) for display in a RichTextBox or FlowDocument.
/// Supports: SGR colors (standard, 256-color, true color), bold, italic, underline,
/// cursor movement (basic), clear screen/line, and OSC title sequences.
/// </summary>
public class AnsiParser
{
    // ─── ANSI Standard Colors (indices 0–7) ─────────────────────────────────
    private static readonly Color[] StandardColors =
    {
        Color.FromRgb(0x1E, 0x1E, 0x2E), // 0 Black (match theme background)
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

    // ─── Parser State ───────────────────────────────────────────────────────

    private Color _foreground;
    private Color _background;
    private bool _isBold;
    private bool _isItalic;
    private bool _isUnderline;
    private bool _isDim;
    private bool _isStrikethrough;
    private bool _isInverse;

    // ─── Line Buffer for cursor-aware rendering ────────────────────────────

    private readonly TerminalLineBuffer _lineBuffer;

    /// <summary>Number of committed inlines (before current line) in the Paragraph.</summary>
    private int _committedInlineCount;

    /// <summary>Current cursor column position within the active line.</summary>
    public int CursorColumn => _lineBuffer.CursorCol;

    private readonly Color _defaultForeground;
    private readonly Color _defaultBackground;

    private static readonly Color FallbackFg = Color.FromRgb(0xCD, 0xD6, 0xF4);
    private static readonly Color FallbackBg = Color.FromRgb(0x1E, 0x1E, 0x2E);

    /// <summary>
    /// Creates a parser with Catppuccin Mocha defaults (backward-compatible).
    /// </summary>
    public AnsiParser() : this(FallbackFg, FallbackBg) { }

    /// <summary>
    /// Creates a parser with theme-specific default colors.
    /// </summary>
    public AnsiParser(Color defaultForeground, Color defaultBackground)
    {
        _defaultForeground = defaultForeground;
        _defaultBackground = defaultBackground;
        _foreground = defaultForeground;
        _background = defaultBackground;
        _lineBuffer = new TerminalLineBuffer(120, defaultForeground, defaultBackground);
    }

    /// <summary>
    /// Fires when an OSC title-change sequence is received.
    /// </summary>
    public event Action<string>? TitleChanged;

    /// <summary>
    /// Fires when a bell character is received.
    /// </summary>
    public event Action? BellReceived;

    // Regex to match CSI sequences: ESC [ <params> <intermediate> <final>
    private static readonly Regex CsiRegex = new(
        @"\x1B\[(?<params>[0-9;]*?)(?<final>[A-Za-z@`])",
        RegexOptions.Compiled);

    // Regex to match OSC sequences: ESC ] <id> ; <text> (BEL | ST)
    private static readonly Regex OscRegex = new(
        @"\x1B\](?<id>\d+);(?<text>[^\x07\x1B]*?)(?:\x07|\x1B\\)",
        RegexOptions.Compiled);

    // Cached compiled Regex for non-CSI escape sequences stripped in Parse()
    private static readonly Regex CharsetSelectRegex = new(@"\x1B[()][AB012]", RegexOptions.Compiled);
    private static readonly Regex KeypadModeRegex    = new(@"\x1B[=>]",        RegexOptions.Compiled);
    private static readonly Regex DecPrivateRegex    = new(@"\x1B\x5B\?[0-9;]*[A-Za-z@`]", RegexOptions.Compiled);
    // Private/extended CSI: ESC [ > ... or < ... or ! ... (e.g. ESC[>0q, ESC[>4m, ESC[<u sent by Claude Code)
    private static readonly Regex PrivateCsiRegex    = new(@"\x1B\[[><!=][0-9;]*[A-Za-z@`]", RegexOptions.Compiled);

    // Frozen brush cache: avoids allocating a new SolidColorBrush per Run
    private static readonly Dictionary<Color, SolidColorBrush> BrushCache = new();

    // Buffer for incomplete escape sequences split across ConPTY read chunks
    private string _pendingInput = string.Empty;

    /// <summary>
    /// Parses a string containing ANSI escape codes and returns a list of WPF Inline elements.
    /// </summary>
    public List<Inline> Parse(string input)
    {
        var inlines = new List<Inline>();
        if (string.IsNullOrEmpty(input)) return inlines;

        // Handle OSC sequences first (window titles, etc.)
        input = OscRegex.Replace(input, match =>
        {
            if (int.TryParse(match.Groups["id"].Value, out int id))
            {
                string text = match.Groups["text"].Value;
                if (id is 0 or 2) // Set window title
                    TitleChanged?.Invoke(text);
            }
            return string.Empty;
        });

        // Remove non-CSI escape sequences we don't handle (compiled, cached)
        input = CharsetSelectRegex.Replace(input, ""); // Character set selection
        input = KeypadModeRegex.Replace(input, "");    // Keypad mode
        input = DecPrivateRegex.Replace(input, "");    // DEC private modes (?...)
        input = PrivateCsiRegex.Replace(input, "");    // Private CSI (>..., <..., !..., =...)

        // Handle bell character
        if (input.Contains('\x07'))
        {
            BellReceived?.Invoke();
            input = input.Replace("\x07", "");
        }

        // Split by CSI sequences
        int lastIndex = 0;
        foreach (Match match in CsiRegex.Matches(input))
        {
            // Add text before this escape sequence (handles \x08 backspace)
            if (match.Index > lastIndex)
                AppendTextWithControls(input[lastIndex..match.Index], inlines);

            // Process the escape sequence
            ProcessCsiSequence(match.Groups["params"].Value, match.Groups["final"].Value[0]);
            lastIndex = match.Index + match.Length;
        }

        // Add remaining text after last escape sequence
        if (lastIndex < input.Length)
            AppendTextWithControls(input[lastIndex..], inlines);

        return inlines;
    }

    /// <summary>
    /// Appends text to the inline list, handling backspace (\x08) by removing
    /// the previous character and skipping other non-printable control chars.
    /// This enables correct visual display of shell backspace-erase sequences
    /// like BS + SPACE + BS that readline produces when the user presses Backspace.
    /// </summary>
    private void AppendTextWithControls(string text, List<Inline> inlines)
    {
        var sb = new StringBuilder(text.Length);

        foreach (char c in text)
        {
            if (c == '\x08') // Backspace in terminal output — erase previous char
            {
                if (sb.Length > 0)
                {
                    sb.Length--;
                }
                else if (inlines.Count > 0 && inlines[^1] is Run lastRun)
                {
                    // Remove last char from the most recently added Run
                    var t = lastRun.Text;
                    if (t.Length > 1)
                        lastRun.Text = t[..^1];
                    else
                        inlines.RemoveAt(inlines.Count - 1);
                }
            }
            else if (c == '\n' || c == '\r' || c == '\t' || !char.IsControl(c))
            {
                // Flush pending buffer when formatting might change at CSI boundary
                sb.Append(c);
            }
            // All other control chars (e.g. \x07 bell already removed, \x7F, etc.) are skipped
        }

        if (sb.Length > 0)
            inlines.Add(CreateRun(sb.ToString()));
    }

    /// <summary>
    /// Parses ANSI text and appends to an existing Paragraph.
    /// </summary>
    public void ParseAndAppend(string input, Paragraph paragraph)
    {
        var inlines = Parse(input);
        foreach (var inline in inlines)
            paragraph.Inlines.Add(inline);
    }

    /// <summary>
    /// Parses ANSI escape codes and renders directly into the given Paragraph.
    /// Unlike Parse(), this method handles cursor/erase operations (\r, ESC[K, ESC[J)
    /// by manipulating the Paragraph's Inlines in-place.
    /// Buffers incomplete escape sequences across calls to avoid garbled output.
    /// </summary>
    public void ParseAndRender(string input, Paragraph paragraph)
    {
        if (string.IsNullOrEmpty(input) && _pendingInput.Length == 0) return;

        // Prepend any leftover partial escape sequence from the previous call
        if (_pendingInput.Length > 0)
        {
            input = _pendingInput + input;
            _pendingInput = string.Empty;
        }

        // Check for incomplete escape sequence at the end of input and save it
        int trailingEsc = FindTrailingPartialEscape(input);
        if (trailingEsc >= 0)
        {
            _pendingInput = input[trailingEsc..];
            input = input[..trailingEsc];
            if (input.Length == 0) return;
        }

        // Handle OSC sequences (window titles, etc.)
        input = OscRegex.Replace(input, match =>
        {
            if (int.TryParse(match.Groups["id"].Value, out int id))
            {
                string text = match.Groups["text"].Value;
                if (id is 0 or 2)
                    TitleChanged?.Invoke(text);
            }
            return string.Empty;
        });

        // Remove non-CSI escape sequences we don't handle
        input = CharsetSelectRegex.Replace(input, "");
        input = KeypadModeRegex.Replace(input, "");
        input = DecPrivateRegex.Replace(input, "");
        input = PrivateCsiRegex.Replace(input, "");

        // Handle bell character
        if (input.Contains('\x07'))
        {
            BellReceived?.Invoke();
            input = input.Replace("\x07", "");
        }

        // Split by CSI sequences and process
        int lastIndex = 0;
        foreach (Match match in CsiRegex.Matches(input))
        {
            if (match.Index > lastIndex)
                RenderTextToDocument(input[lastIndex..match.Index], paragraph);

            ProcessCsiWithDocument(match.Groups["params"].Value, match.Groups["final"].Value[0], paragraph);
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < input.Length)
            RenderTextToDocument(input[lastIndex..], paragraph);
    }

    /// <summary>
    /// Detects an incomplete escape sequence at the end of the input.
    /// Returns the start index of the partial sequence, or -1 if none found.
    /// </summary>
    private static int FindTrailingPartialEscape(string input)
    {
        // Search backward for the last ESC character
        int lastEsc = input.LastIndexOf('\x1B');
        if (lastEsc < 0) return -1;

        string tail = input[lastEsc..];

        // ESC alone at end → definitely incomplete
        if (tail.Length == 1) return lastEsc;

        char second = tail[1];

        // CSI sequence: ESC [ params finalChar
        if (second == '[')
        {
            // Check if there's a valid final character (letter or @`)
            for (int i = 2; i < tail.Length; i++)
            {
                char c = tail[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '@' || c == '`')
                    return -1; // Complete CSI sequence found
                if (c != ';' && (c < '0' || c > '9') && c != '?' && c != '>' && c != '<' && c != '!' && c != '=')
                    return -1; // Invalid char — not a partial CSI, let it through
            }
            return lastEsc; // Incomplete: has ESC[ + params but no final char
        }

        // OSC sequence: ESC ] id ; text (BEL | ST)
        if (second == ']')
        {
            // Check for terminator: BEL (\x07) or ST (ESC \)
            if (tail.Contains('\x07') || tail.Contains("\x1B\\"))
                return -1; // Complete OSC
            return lastEsc; // Incomplete OSC
        }

        // Other 2-char sequences (charset, keypad): ESC ( A, ESC =, etc.
        if (tail.Length >= 2)
            return -1; // 2+ chars is enough for these short sequences

        return lastEsc;
    }

    /// <summary>
    /// Renders plain text (with control chars like \r, \n, \x08) into a Paragraph
    /// using the line buffer for cursor-aware rendering.
    /// </summary>
    private void RenderTextToDocument(string text, Paragraph paragraph)
    {
        var fg = _isInverse ? _background : _foreground;
        var bg = _isInverse ? _foreground : _background;
        if (_isDim) fg = Color.FromArgb(178, fg.R, fg.G, fg.B);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    // \r\n → commit current line, start new line
                    CommitCurrentLine(paragraph);
                    paragraph.Inlines.Add(new Run("\n"));
                    _committedInlineCount = paragraph.Inlines.Count;
                    _lineBuffer.Clear();
                    i++; // skip \n
                }
                else
                {
                    // Standalone \r → cursor to column 0 (line will be overwritten)
                    _lineBuffer.CarriageReturn();
                }
            }
            else if (c == '\n')
            {
                CommitCurrentLine(paragraph);
                paragraph.Inlines.Add(new Run("\n"));
                _committedInlineCount = paragraph.Inlines.Count;
                _lineBuffer.Clear();
            }
            else if (c == '\x08') // Backspace — move cursor left (NOT delete)
            {
                _lineBuffer.Backspace();
            }
            else if (c == '\t')
            {
                // Expand tab to next multiple of 8
                int nextTab = ((_lineBuffer.CursorCol / 8) + 1) * 8;
                while (_lineBuffer.CursorCol < nextTab)
                    _lineBuffer.Write(' ', fg, bg, _isBold, _isItalic, _isUnderline);
            }
            else if (!char.IsControl(c))
            {
                _lineBuffer.Write(c, fg, bg, _isBold, _isItalic, _isUnderline);
            }
        }

        // Flush current line buffer to display (replaceable zone)
        FlushCurrentLine(paragraph);
    }

    /// <summary>
    /// Commits the current line buffer content as permanent inlines (no longer replaceable).
    /// Called before a newline to "freeze" the current line.
    /// </summary>
    private void CommitCurrentLine(Paragraph paragraph)
    {
        // Remove the replaceable current-line inlines
        RemoveCurrentLineInlines(paragraph);

        // Add the buffer content as permanent inlines
        var runs = _lineBuffer.FlushToInlines();
        foreach (var run in runs)
            paragraph.Inlines.Add(run);

        // Update committed count to include these new inlines
        _committedInlineCount = paragraph.Inlines.Count;
    }

    /// <summary>
    /// Flushes the current line buffer to display, replacing the temporary
    /// current-line inlines with fresh Runs from the buffer.
    /// </summary>
    private void FlushCurrentLine(Paragraph paragraph)
    {
        // Remove old current-line inlines (everything after committed zone)
        RemoveCurrentLineInlines(paragraph);

        // Add new Runs from the buffer
        var runs = _lineBuffer.FlushToInlines();
        foreach (var run in runs)
            paragraph.Inlines.Add(run);
    }

    /// <summary>
    /// Removes all inlines after the committed zone (the replaceable current line).
    /// </summary>
    private void RemoveCurrentLineInlines(Paragraph paragraph)
    {
        while (paragraph.Inlines.Count > _committedInlineCount)
            paragraph.Inlines.Remove(paragraph.Inlines.LastInline);
    }

    /// <summary>
    /// Processes a CSI sequence with direct access to the Paragraph for erase operations
    /// and cursor movement via the line buffer.
    /// </summary>
    private void ProcessCsiWithDocument(string paramString, char finalChar, Paragraph paragraph)
    {
        int n = string.IsNullOrEmpty(paramString) ? 1 : int.TryParse(paramString, out int p) ? p : 1;

        switch (finalChar)
        {
            case 'm':
                ProcessSgr(paramString);
                break;

            case 'J': // Erase in Display
            {
                int mode = string.IsNullOrEmpty(paramString) ? 0 : (int.TryParse(paramString, out int jp) ? jp : 0);
                if (mode is 2 or 3)
                {
                    paragraph.Inlines.Clear();
                    _committedInlineCount = 0;
                    _lineBuffer.Clear();
                }
                break;
            }

            case 'K': // Erase in Line
            {
                int mode = string.IsNullOrEmpty(paramString) ? 0 : (int.TryParse(paramString, out int kp) ? kp : 0);
                _lineBuffer.EraseLine(mode);
                FlushCurrentLine(paragraph);
                break;
            }

            case 'C': // Cursor Forward
                _lineBuffer.MoveCursorRight(n);
                break;

            case 'D': // Cursor Back
                _lineBuffer.MoveCursorLeft(n);
                break;

            case 'G': // Cursor Horizontal Absolute (1-based)
                _lineBuffer.MoveCursorToColumn(n - 1);
                break;

            case 'P': // Delete Characters
                _lineBuffer.DeleteChars(n);
                FlushCurrentLine(paragraph);
                break;

            case '@': // Insert Characters
                _lineBuffer.InsertChars(n);
                FlushCurrentLine(paragraph);
                break;

            case 'H': case 'f': // Cursor Position (row;col)
            {
                // Both row and col are 1-based; default to 1 if omitted
                var parts = (paramString ?? "").Split(';');
                int row = parts.Length >= 1 && int.TryParse(parts[0], out int r) && r > 0 ? r : 1;
                int col = parts.Length >= 2 && int.TryParse(parts[1], out int c) && c > 0 ? c : 1;
                _lineBuffer.CursorRow = Math.Clamp(row - 1, 0, _lineBuffer.ScreenRows - 1);
                _lineBuffer.MoveCursorToColumn(col - 1);
                break;
            }

            case 'A': // Cursor Up
                _lineBuffer.CursorRow = Math.Max(0, _lineBuffer.CursorRow - n);
                break;
            case 'B': // Cursor Down
                _lineBuffer.CursorRow = Math.Min(_lineBuffer.ScreenRows - 1, _lineBuffer.CursorRow + n);
                break;
            // Scroll, insert/delete lines — no-op
            case 'S': case 'T': case 'L': case 'M':
            // Save/restore cursor, scrolling region — no-op
            case 's': case 'u': case 'r':
                break;
        }
    }

    /// <summary>
    /// Resets only formatting attributes (colors, bold, italic, etc.) to defaults.
    /// Does NOT touch rendering state (_committedInlineCount, _lineBuffer, _pendingInput).
    /// Called by ProcessSgr when SGR code 0 is received (\x1B[0m).
    /// </summary>
    private void ResetFormatting()
    {
        _foreground = _defaultForeground;
        _background = _defaultBackground;
        _isBold = false;
        _isItalic = false;
        _isUnderline = false;
        _isDim = false;
        _isStrikethrough = false;
        _isInverse = false;
    }

    /// <summary>
    /// Full reset: formatting + rendering state. Used only for clear screen / terminal init.
    /// </summary>
    public void Reset()
    {
        ResetFormatting();
        _pendingInput = string.Empty;
        _committedInlineCount = 0;
        _lineBuffer.Clear();
    }

    /// <summary>
    /// Adjusts the committed inline count after scrollback trimming removes inlines
    /// from the front of the Paragraph.
    /// </summary>
    public void AdjustCommittedCount(int removedCount)
    {
        _committedInlineCount = Math.Max(0, _committedInlineCount - removedCount);
    }

    /// <summary>
    /// Resizes the line buffer to match the terminal width.
    /// </summary>
    public void SetColumns(int columns) => SetSize(columns, _lineBuffer.ScreenRows);

    /// <summary>
    /// Resizes the line buffer to match both terminal width and height.
    /// </summary>
    public void SetSize(int columns, int rows)
    {
        _lineBuffer.SetWidth(columns);
        _lineBuffer.SetHeight(rows);
    }

    // ─── Private Methods ────────────────────────────────────────────────────

    /// <summary>
    /// Returns a frozen SolidColorBrush for the given color, reusing cached instances.
    /// Frozen brushes are thread-safe and cheaper to use in WPF rendering.
    /// </summary>
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

    /// <summary>
    /// Creates a Run with current formatting state.
    /// </summary>
    private Run CreateRun(string text)
    {
        var fg = _isInverse ? _background : _foreground;
        var bg = _isInverse ? _foreground : _background;

        if (_isDim)
        {
            fg = Color.FromArgb(178, fg.R, fg.G, fg.B); // ~70% opacity
        }

        var run = new Run(text)
        {
            Foreground = GetBrush(fg),
            FontWeight = _isBold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = _isItalic ? FontStyles.Italic : FontStyles.Normal,
        };

        if (bg != _defaultBackground)
            run.Background = GetBrush(bg);

        if (_isUnderline)
        {
            run.TextDecorations = TextDecorations.Underline;
        }

        if (_isStrikethrough)
        {
            run.TextDecorations = run.TextDecorations != null
                ? new TextDecorationCollection(run.TextDecorations.Concat(TextDecorations.Strikethrough))
                : TextDecorations.Strikethrough;
        }

        return run;
    }

    /// <summary>
    /// Processes a CSI (Control Sequence Introducer) escape sequence.
    /// </summary>
    private void ProcessCsiSequence(string paramString, char finalChar)
    {
        switch (finalChar)
        {
            case 'm': // SGR - Select Graphic Rendition
                ProcessSgr(paramString);
                break;

            case 'A': // Cursor Up
            case 'B': // Cursor Down
            case 'C': // Cursor Forward
            case 'D': // Cursor Back
            case 'H': // Cursor Position
            case 'f': // Cursor Position (same as H)
                // Cursor movement — handled at terminal control level
                break;

            case 'J': // Erase in Display
                // 0=below, 1=above, 2=all, 3=scrollback
                break;

            case 'K': // Erase in Line
                // 0=to end, 1=to start, 2=entire line
                break;

            case 'S': // Scroll Up
            case 'T': // Scroll Down
                break;

            case 'L': // Insert lines
            case 'M': // Delete lines
            case 'P': // Delete characters
            case '@': // Insert characters
                break;

            case 's': // Save cursor position
            case 'u': // Restore cursor position
                break;

            case 'r': // Set scrolling region
                break;
        }
    }

    /// <summary>
    /// Processes SGR (Select Graphic Rendition) parameters.
    /// Handles: basic attributes, standard colors, 256-color mode, and true color (24-bit).
    /// </summary>
    private void ProcessSgr(string paramString)
    {
        if (string.IsNullOrEmpty(paramString) || paramString == "0")
        {
            ResetFormatting();
            return;
        }

        var parts = paramString.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out int code)) continue;

            switch (code)
            {
                case 0: ResetFormatting(); break;
                case 1: _isBold = true; break;
                case 2: _isDim = true; break;
                case 3: _isItalic = true; break;
                case 4: _isUnderline = true; break;
                case 7: _isInverse = true; break;
                case 9: _isStrikethrough = true; break;

                case 21: _isBold = false; break; // Double underline / bold off
                case 22: _isBold = false; _isDim = false; break;
                case 23: _isItalic = false; break;
                case 24: _isUnderline = false; break;
                case 27: _isInverse = false; break;
                case 29: _isStrikethrough = false; break;

                // Standard foreground colors (30–37)
                case >= 30 and <= 37:
                    _foreground = StandardColors[code - 30];
                    if (_isBold) _foreground = BrightColors[code - 30];
                    break;

                // Extended foreground color
                case 38:
                    i = ParseExtendedColor(parts, i, isForeground: true);
                    break;

                // Default foreground
                case 39:
                    _foreground = _defaultForeground;
                    break;

                // Standard background colors (40–47)
                case >= 40 and <= 47:
                    _background = StandardColors[code - 40];
                    break;

                // Extended background color
                case 48:
                    i = ParseExtendedColor(parts, i, isForeground: false);
                    break;

                // Default background
                case 49:
                    _background = _defaultBackground;
                    break;

                // Bright foreground colors (90–97)
                case >= 90 and <= 97:
                    _foreground = BrightColors[code - 90];
                    break;

                // Bright background colors (100–107)
                case >= 100 and <= 107:
                    _background = BrightColors[code - 100];
                    break;
            }
        }
    }

    /// <summary>
    /// Parses extended color sequences: 256-color (5;n) and true color (2;r;g;b).
    /// </summary>
    /// <returns>Updated index into the parts array.</returns>
    private int ParseExtendedColor(string[] parts, int currentIndex, bool isForeground)
    {
        if (currentIndex + 1 >= parts.Length) return currentIndex;

        if (int.TryParse(parts[currentIndex + 1], out int mode))
        {
            switch (mode)
            {
                case 5: // 256-color: ESC[38;5;{n}m
                    if (currentIndex + 2 < parts.Length && int.TryParse(parts[currentIndex + 2], out int colorIndex))
                    {
                        var color = Get256Color(colorIndex);
                        if (isForeground) _foreground = color;
                        else _background = color;
                        return currentIndex + 2;
                    }
                    return currentIndex + 1;

                case 2: // True color: ESC[38;2;{r};{g};{b}m
                    if (currentIndex + 4 < parts.Length &&
                        int.TryParse(parts[currentIndex + 2], out int r) &&
                        int.TryParse(parts[currentIndex + 3], out int g) &&
                        int.TryParse(parts[currentIndex + 4], out int b))
                    {
                        var color = Color.FromRgb(
                            (byte)Math.Clamp(r, 0, 255),
                            (byte)Math.Clamp(g, 0, 255),
                            (byte)Math.Clamp(b, 0, 255));
                        if (isForeground) _foreground = color;
                        else _background = color;
                        return currentIndex + 4;
                    }
                    return currentIndex + 1;
            }
        }

        return currentIndex;
    }

    /// <summary>
    /// Converts a 256-color index to an RGB Color.
    /// Indices 0–7: standard colors, 8–15: bright colors,
    /// 16–231: 6×6×6 color cube, 232–255: grayscale ramp.
    /// </summary>
    private static Color Get256Color(int index)
    {
        index = Math.Clamp(index, 0, 255);

        // Standard colors (0–7)
        if (index < 8)
            return StandardColors[index];

        // Bright colors (8–15)
        if (index < 16)
            return BrightColors[index - 8];

        // 6×6×6 color cube (16–231)
        if (index < 232)
        {
            int value = index - 16;
            int r = value / 36;
            int g = (value % 36) / 6;
            int b = value % 6;
            return Color.FromRgb(
                (byte)(r > 0 ? 55 + r * 40 : 0),
                (byte)(g > 0 ? 55 + g * 40 : 0),
                (byte)(b > 0 ? 55 + b * 40 : 0));
        }

        // Grayscale ramp (232–255)
        {
            int gray = 8 + (index - 232) * 10;
            gray = Math.Clamp(gray, 0, 255);
            return Color.FromRgb((byte)gray, (byte)gray, (byte)gray);
        }
    }

}
