using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Helpers;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// ViewModel for a single terminal tab. Handles output parsing and input routing.
/// </summary>
public partial class TerminalViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    private readonly ITerminalService _terminalService;
    private readonly IDatabaseService _db;
    private readonly ITerminalBackgroundService? _backgroundService;
    private readonly AnsiParser _ansiParser;
    private readonly Dispatcher _dispatcher;

    // ─── Output throttling ───────────────────────────────────────────────────
    private readonly StringBuilder _outputBuffer = new();
    private readonly object _bufferLock = new();
    private bool _flushScheduled;

    private bool _disposed;

    // ─── Deferred initialization ─────────────────────────────────────────────
    private bool _sessionStarted;
    private ShellType _pendingShellType;
    private string? _pendingWorkDir;
    private string? _pendingProjectId;

    // Theme-resolved terminal colors
    private readonly Color _terminalFg;
    private readonly Color _terminalBg;

    [ObservableProperty]
    private TerminalSession? _session;

    [ObservableProperty]
    private string _title = "Terminal";

    [ObservableProperty]
    private ShellType _shellType = ShellType.WSL;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private FlowDocument _outputDocument;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private string _statusText = "Starting...";

    /// <summary>True once <see cref="StartSessionAsync"/> has been called (even if it failed).</summary>
    public bool IsSessionStarted => _sessionStarted;

    /// <summary>Current cursor column position from the ANSI parser's screen buffer.</summary>
    public int CursorColumn => _ansiParser.CursorColumn;

    // ─── Terminal Background ────────────────────────────────────────────────

    [ObservableProperty]
    private ImageSource? _backgroundImage;

    [ObservableProperty]
    private double _backgroundOpacity;

    [ObservableProperty]
    private Stretch _backgroundStretch = Stretch.UniformToFill;

    [ObservableProperty]
    private double _overlayOpacity;

    [ObservableProperty]
    private bool _isDarkOverlay = true;

    [ObservableProperty]
    private bool _hasBackground;

    public TerminalViewModel(ITerminalService terminalService, IDatabaseService db,
                             ITerminalBackgroundService? backgroundService = null)
    {
        _terminalService = terminalService;
        _db = db;
        _backgroundService = backgroundService;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        // Resolve terminal colors from the active theme
        _terminalFg = Application.Current?.TryFindResource("TextColor") is Color fg
            ? fg : ThemeColors.CatppuccinText;
        _terminalBg = Application.Current?.TryFindResource("BaseBg") is Color bg
            ? bg : ThemeColors.CatppuccinBase;

        _ansiParser = new AnsiParser(_terminalFg, _terminalBg);

        _outputDocument = new FlowDocument
        {
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize = 14,
            PagePadding = new Thickness(8, 0, 8, 0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(_terminalFg)
        };

        // Connect the parser to the document — it will manage all paragraphs internally
        _ansiParser.Initialize(_outputDocument);

        _ansiParser.TitleChanged += newTitle =>
        {
            _dispatcher.Invoke(() => Title = newTitle);
        };

        _terminalService.OutputReceived += OnOutputReceived;
        _terminalService.SessionExited += OnSessionExited;

        // Subscribe to terminal background changes
        if (_backgroundService != null)
        {
            _backgroundService.BackgroundChanged += OnBackgroundChanged;
            SyncBackgroundFromService();
        }
    }

    /// <summary>
    /// Prepares the terminal with session parameters but does NOT create the ConPTY session yet.
    /// Call <see cref="StartSessionAsync"/> after the control reports its actual pixel dimensions.
    /// </summary>
    public Task PrepareAsync(ShellType shellType, string? workingDirectory = null, string? projectId = null)
    {
        _pendingShellType = shellType;
        _pendingWorkDir = workingDirectory;
        _pendingProjectId = projectId;
        ShellType = shellType;
        Title = shellType.GetDisplayName();
        StatusText = "Waiting for layout...";
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates the ConPTY session with the exact terminal dimensions measured by the control.
    /// Called from <see cref="Controls.TerminalControl.OnLoaded"/> once layout is complete.
    /// </summary>
    public async Task StartSessionAsync(short columns, short rows)
    {
        if (_sessionStarted) return;
        _sessionStarted = true;
        StatusText = "Connecting...";

        try
        {
            Session = await _terminalService.CreateSessionAsync(
                _pendingShellType, _pendingWorkDir, _pendingProjectId, columns, rows);
            _ansiParser.SetSize(columns, rows);
            IsConnected = Session.Status == TerminalStatus.Running;
            StatusText = IsConnected ? "Connected" : "Error";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            IsConnected = false;
        }
    }

    /// <summary>
    /// Initializes the terminal by creating a new ConPTY session.
    /// </summary>
    public async Task InitializeAsync(ShellType shellType, string? workingDirectory = null, string? projectId = null)
    {
        await PrepareAsync(shellType, workingDirectory, projectId);
    }

    /// <summary>
    /// Sends text input to the terminal (e.g., a typed command + Enter).
    /// </summary>
    [RelayCommand]
    private async Task SendInput()
    {
        if (Session == null || string.IsNullOrEmpty(InputText)) return;
        var cmd = InputText.Trim();
        await _terminalService.WriteAsync(Session.Id, InputText + "\r");
        InputText = string.Empty;
        if (!string.IsNullOrWhiteSpace(cmd))
            _ = _db.AddCommandHistoryAsync(Session.Id, cmd);
    }

    /// <summary>
    /// Sends raw key data (for keyboard event routing from the terminal control).
    /// </summary>
    public async Task SendKeyDataAsync(string data)
    {
        if (Session == null) return;
        await _terminalService.WriteAsync(Session.Id, data);
    }

    /// <summary>
    /// Executes a command in the terminal.
    /// </summary>
    public async Task ExecuteCommandAsync(string command)
    {
        if (Session == null) return;
        await _terminalService.WriteAsync(Session.Id, command + "\r");
        if (!string.IsNullOrWhiteSpace(command))
            _ = _db.AddCommandHistoryAsync(Session.Id, command.Trim());
    }

    /// <summary>
    /// Resizes the terminal.
    /// </summary>
    public void ResizeTerminal(short columns, short rows)
    {
        if (Session == null) return;
        _terminalService.Resize(Session.Id, columns, rows);
        _ansiParser.SetSize(columns, rows);
    }

    /// <summary>
    /// Clears the terminal output.
    /// </summary>
    [RelayCommand]
    private void ClearOutput()
    {
        _dispatcher.Invoke(() => _ansiParser.Reset());
    }

    /// <summary>
    /// Closes this terminal session.
    /// </summary>
    [RelayCommand]
    private async Task CloseTerminal()
    {
        if (Session != null)
            await _terminalService.CloseSessionAsync(Session.Id);
    }

    // ─── Private Methods ────────────────────────────────────────────────────

    private void OnOutputReceived(string sessionId, string output)
    {
        if (!_sessionStarted || Session?.Id != sessionId) return;

        lock (_bufferLock)
        {
            _outputBuffer.Append(output);

            if (!_flushScheduled)
            {
                _flushScheduled = true;
                // DispatcherPriority.Normal ensures rendering keeps up with output
                _dispatcher.BeginInvoke(DispatcherPriority.Normal, FlushOutputBuffer);
            }
        }
    }

    private void FlushOutputBuffer()
    {
        string buffered;
        lock (_bufferLock)
        {
            buffered = _outputBuffer.ToString();
            _outputBuffer.Clear();
            _flushScheduled = false;
        }

        if (string.IsNullOrEmpty(buffered)) return;

        try
        {
            _ansiParser.ParseAndRender(buffered);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TerminalVM] ParseAndRender error: {ex}");
        }
    }

    private void OnSessionExited(string sessionId)
    {
        if (_disposed || Session?.Id != sessionId) return;

        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            IsConnected = false;
            StatusText = "Disconnected";
            // Render exit message in red via ANSI codes (color 31 = Catppuccin red)
            _ansiParser.ParseAndRender("\r\n\x1B[31m[Process exited]\x1B[0m\r\n");
        });
    }

    // ─── Background Sync ────────────────────────────────────────────────────

    private void OnBackgroundChanged()
    {
        _dispatcher.BeginInvoke(SyncBackgroundFromService);
    }

    private void SyncBackgroundFromService()
    {
        if (_backgroundService == null) return;

        BackgroundImage  = _backgroundService.ProcessedImage;
        BackgroundOpacity = _backgroundService.Opacity;
        BackgroundStretch = _backgroundService.WallpaperStretch;
        OverlayOpacity   = _backgroundService.OverlayOpacity;
        IsDarkOverlay    = _backgroundService.IsDarkOverlay;
        HasBackground    = _backgroundService.HasWallpaper;
    }

    public void Dispose()
    {
        _disposed = true;
        _terminalService.OutputReceived -= OnOutputReceived;
        _terminalService.SessionExited  -= OnSessionExited;

        if (_backgroundService != null)
            _backgroundService.BackgroundChanged -= OnBackgroundChanged;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Asynchronously disposes the ViewModel, closing the ConPTY session before releasing resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();

        if (Session != null)
        {
            try
            {
                await _terminalService.CloseSessionAsync(Session.Id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TerminalViewModel] DisposeAsync failed: {ex.Message}");
            }
        }
    }
}
