using DevWorkspaceHub.Models.Browser;

namespace DevWorkspaceHub.Services.Browser;

public class SelectionHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public string TagSummary { get; set; } = string.Empty;
    public string CssSelector { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? SentToAgent { get; set; }
    public string? Intent { get; set; }
    public ElementCaptureData? FullData { get; set; }
}

public interface ISelectionHistoryService
{
    IReadOnlyList<SelectionHistoryEntry> RecentEntries { get; }
    void Add(SelectionHistoryEntry entry);
    void Clear();
    event Action? HistoryChanged;
}

public class SelectionHistoryService : ISelectionHistoryService
{
    private readonly List<SelectionHistoryEntry> _entries = new();
    private readonly object _lock = new();
    private const int MaxEntries = 50;

    public event Action? HistoryChanged;

    public IReadOnlyList<SelectionHistoryEntry> RecentEntries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList().AsReadOnly();
            }
        }
    }

    public void Add(SelectionHistoryEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);

            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveAt(0);
            }
        }

        HistoryChanged?.Invoke();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }

        HistoryChanged?.Invoke();
    }
}
