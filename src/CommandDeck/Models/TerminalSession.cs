using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CommandDeck.Models;

/// <summary>
/// Represents an active terminal session with its associated process.
/// </summary>
public partial class TerminalSession : ObservableObject, IDisposable
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _title = "Terminal";

    [ObservableProperty]
    private ShellType _shellType = ShellType.WSL;

    [ObservableProperty]
    private TerminalStatus _status = TerminalStatus.Starting;

    [ObservableProperty]
    private string? _projectId;

    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    [ObservableProperty]
    private int _processId;

    [ObservableProperty]
    private DateTime _startedAt = DateTime.Now;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>
    /// Handle to the pseudo console (IntPtr).
    /// </summary>
    public IntPtr PseudoConsoleHandle { get; set; }

    /// <summary>
    /// The process associated with this terminal session.
    /// </summary>
    public System.Diagnostics.Process? Process { get; set; }

    /// <summary>
    /// Stream for writing input to the terminal.
    /// </summary>
    public Stream? InputStream { get; set; }

    /// <summary>
    /// Stream for reading output from the terminal.
    /// </summary>
    public Stream? OutputStream { get; set; }

    /// <summary>
    /// Cancellation token source for the read loop.
    /// </summary>
    public CancellationTokenSource? CancellationSource { get; set; }

    public void Dispose()
    {
        CancellationSource?.Cancel();
        CancellationSource?.Dispose();
        InputStream?.Dispose();
        OutputStream?.Dispose();
        try { Process?.Kill(entireProcessTree: true); } catch { }
        Process?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Status of a terminal session.
/// </summary>
public enum TerminalStatus
{
    Starting,
    Running,
    Stopped,
    Error
}
