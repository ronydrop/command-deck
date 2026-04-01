using System.Collections.Concurrent;

namespace CommandDeck.Helpers;

/// <summary>
/// Thread-safe ring buffer for terminal command history with automatic deduplication.
/// </summary>
public sealed class CommandRingBuffer
{
    private readonly ConcurrentQueue<string> _commands = new();
    private readonly int _capacity;
    private int _totalCount;

    /// <summary>
    /// Total number of commands ever added (including those trimmed from the buffer).
    /// </summary>
    public int TotalCount => _totalCount;

    /// <summary>Initializes a new ring buffer with the given maximum capacity.</summary>
    /// <param name="capacity">Maximum number of commands to retain. Defaults to 100.</param>
    public CommandRingBuffer(int capacity = 100)
    {
        _capacity = capacity > 0 ? capacity : 100;
    }

    /// <summary>
    /// Adds a command to the buffer. Empty / whitespace-only commands are ignored.
    /// Consecutive identical commands are deduplicated.
    /// When the buffer is full, the oldest entry is removed.
    /// </summary>
    /// <param name="command">The raw command text.</param>
    public void Add(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        var trimmed = command.Trim();

        // Deduplicate: skip if the last recorded command is identical
        if (_commands.TryPeek(out var last) && last == trimmed)
            return;

        _commands.Enqueue(trimmed);

        // Trim oldest entries when over capacity
        while (_commands.Count > _capacity)
            _commands.TryDequeue(out _);

        Interlocked.Increment(ref _totalCount);
    }

    /// <summary>
    /// Returns all commands currently in the buffer, oldest first.
    /// </summary>
    public IReadOnlyList<string> GetAll() => _commands.ToArray();

    /// <summary>
    /// Returns the command at the given index relative to the most recent entry.
    /// Index 0 returns the newest command; returns <see langword="null"/> when out of range.
    /// </summary>
    /// <param name="index">0-based offset from the most recent command.</param>
    public string? GetAt(int index)
    {
        var snapshot = _commands.ToArray();
        var reverseIndex = snapshot.Length - 1 - index;
        if (reverseIndex < 0 || reverseIndex >= snapshot.Length)
            return null;
        return snapshot[reverseIndex];
    }

    /// <summary>Removes all commands from the buffer and resets the total count.</summary>
    public void Clear()
    {
        while (_commands.TryDequeue(out _)) { }
        _totalCount = 0;
    }
}
