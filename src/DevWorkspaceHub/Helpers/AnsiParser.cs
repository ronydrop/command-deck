using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DevWorkspaceHub.Helpers;

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

    private Color _foreground = Color.FromRgb(0xCD, 0xD6, 0xF4); // Default: white
    private Color _background = Color.FromRgb(0x1E, 0x1E, 0x2E); // Default: dark bg
    private bool _isBold;
    private bool _isItalic;
    private bool _isUnderline;
    private bool _isDim;
    private bool _isStrikethrough;
    private bool _isInverse;

    private readonly Color _defaultForeground = Color.FromRgb(0xCD, 0xD6, 0xF4);
    private readonly Color _defaultBackground = Color.FromRgb(0x1E, 0x1E, 0x2E);

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
            int id = int.Parse(match.Groups["id"].Value);
            string text = match.Groups["text"].Value;
            if (id is 0 or 2) // Set window title
                TitleChanged?.Invoke(text);
            return string.Empty;
        });

        // Remove non-CSI escape sequences we don't handle
        // Keep CSI sequences for processing below
        input = Regex.Replace(input, @"\x1B[()][AB012]", ""); // Character set selection
        input = Regex.Replace(input, @"\x1B[=>]", "");        // Keypad mode
        input = Regex.Replace(input, @"\x1B\x5B\?[0-9;]*[hlsr]", ""); // DEC private modes

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
            // Add text before this escape sequence
            if (match.Index > lastIndex)
            {
                string textBefore = input[lastIndex..match.Index];
                string cleanText = StripControlChars(textBefore);
                if (cleanText.Length > 0)
                    inlines.Add(CreateRun(cleanText));
            }

            // Process the escape sequence
            ProcessCsiSequence(match.Groups["params"].Value, match.Groups["final"].Value[0]);
            lastIndex = match.Index + match.Length;
        }

        // Add remaining text after last escape sequence
        if (lastIndex < input.Length)
        {
            string remainingText = input[lastIndex..];
            string cleanText = StripControlChars(remainingText);
            if (cleanText.Length > 0)
                inlines.Add(CreateRun(cleanText));
        }

        return inlines;
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
    /// Resets all formatting attributes to defaults.
    /// </summary>
    public void Reset()
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

    // ─── Private Methods ────────────────────────────────────────────────────

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
            Foreground = new SolidColorBrush(fg),
            FontWeight = _isBold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = _isItalic ? FontStyles.Italic : FontStyles.Normal,
        };

        if (bg != _defaultBackground)
            run.Background = new SolidColorBrush(bg);

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
            Reset();
            return;
        }

        var parts = paramString.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out int code)) continue;

            switch (code)
            {
                case 0: Reset(); break;
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

    /// <summary>
    /// Strips control characters (except newline and tab) from text.
    /// </summary>
    private static string StripControlChars(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == '\n' || c == '\r' || c == '\t' || !char.IsControl(c))
                sb.Append(c);
        }
        return sb.ToString();
    }
}
