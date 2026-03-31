using System.Text;

namespace CommandDeck.Helpers;

/// <summary>
/// Shared utility for stripping ANSI escape sequences from terminal output.
/// </summary>
public static class AnsiTextHelper
{
    /// <summary>
    /// Removes all ANSI escape sequences from <paramref name="input"/>, returning plain text.
    /// Handles CSI sequences (ESC[...m), OSC sequences (ESC]...BEL/ST), and simple ESC pairs.
    /// </summary>
    public static string StripAnsi(string input)
    {
        if (!input.Contains('\x1b'))
            return input;

        var sb = new StringBuilder(input.Length);
        int i = 0;

        while (i < input.Length)
        {
            if (input[i] == '\x1b' && i + 1 < input.Length)
            {
                char next = input[i + 1];

                if (next == '[')
                {
                    // CSI sequence: ESC [ (params) (final byte 0x40..0x7E)
                    i += 2;
                    while (i < input.Length && (input[i] < 0x40 || input[i] > 0x7E))
                        i++;
                    if (i < input.Length)
                        i++; // skip final byte
                    continue;
                }

                if (next == ']')
                {
                    // OSC sequence: ESC ] (text) (BEL=0x07 or ST=ESC\)
                    i += 2;
                    while (i < input.Length && input[i] != 0x07)
                    {
                        if (input[i] == '\x1b' && i + 1 < input.Length && input[i + 1] == '\\')
                        {
                            i += 2;
                            break;
                        }
                        i++;
                    }
                    if (i < input.Length)
                        i++; // skip BEL or the char after ESC\
                    continue;
                }

                // Other ESC sequences (ESC followed by single char)
                i += 2;
                continue;
            }

            // Control characters (except newline/tab) -> skip
            if (input[i] < 0x20 && input[i] != '\n' && input[i] != '\r' && input[i] != '\t')
            {
                i++;
                continue;
            }

            sb.Append(input[i]);
            i++;
        }

        return sb.ToString();
    }
}
