using System.Text;

namespace CommandDeck.Helpers;

/// <summary>
/// Thread-safe plain-text output snapshot for a terminal session.
/// Strips ANSI escape sequences and enforces a maximum size cap.
/// </summary>
public sealed class TerminalOutputBuffer
{
    private const int DefaultMaxLength = 64 * 1024; // 64 KB

    private readonly StringBuilder _builder;
    private readonly object _lock = new();
    private readonly int _maxLength;

    /// <summary>
    /// Initializes the buffer with an optional capacity cap.
    /// </summary>
    /// <param name="maxLength">Maximum number of characters to retain. Defaults to 65 536 (64 KB).</param>
    public TerminalOutputBuffer(int maxLength = DefaultMaxLength)
    {
        _maxLength = maxLength > 0 ? maxLength : DefaultMaxLength;
        _builder = new StringBuilder(16 * 1024);
    }

    /// <summary>
    /// Appends raw terminal output to the snapshot, stripping ANSI sequences.
    /// Trims the oldest content when the buffer exceeds <see cref="_maxLength"/> characters.
    /// </summary>
    /// <param name="text">Raw output that may contain ANSI escape sequences.</param>
    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var plain = AnsiTextHelper.StripAnsi(text);

        lock (_lock)
        {
            _builder.Append(plain);

            if (_builder.Length > _maxLength)
            {
                var excess = _builder.Length - _maxLength;
                _builder.Remove(0, excess);
            }
        }
    }

    /// <summary>Returns the current plain-text content of the buffer.</summary>
    public string GetContent()
    {
        lock (_lock)
            return _builder.ToString();
    }

    /// <summary>Clears all content from the buffer.</summary>
    public void Clear()
    {
        lock (_lock)
            _builder.Clear();
    }

    /// <summary>
    /// Trims the buffer to retain only the last <paramref name="maxLines"/> lines.
    /// No-op when the buffer has fewer lines than the limit.
    /// </summary>
    /// <param name="maxLines">Maximum number of trailing lines to keep.</param>
    public void Trim(int maxLines)
    {
        if (maxLines <= 0)
            return;

        lock (_lock)
        {
            var content = _builder.ToString();
            if (string.IsNullOrEmpty(content))
                return;

            var lines = content.Split('\n');
            if (lines.Length <= maxLines)
                return;

            var trimmed = string.Join('\n', lines[^maxLines..]);
            _builder.Clear();
            _builder.Append(trimmed);
        }
    }
}
