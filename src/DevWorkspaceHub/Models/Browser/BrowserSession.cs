namespace DevWorkspaceHub.Models.Browser;

public enum BrowserSessionState
{
    Disconnected,
    Connecting,
    Connected,
    Error,
    Loading
}

public class BrowserSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? ProjectId { get; set; }
    public string Url { get; set; } = string.Empty;
    public int Port { get; set; }
    public BrowserSessionState State { get; set; } = BrowserSessionState.Disconnected;
    public DateTime? ConnectedAt { get; set; }
    public DateTime? LastNavigationAt { get; set; }
    public List<string> NavigationHistory { get; set; } = new();
    public bool IsPickerActive { get; set; }
}
