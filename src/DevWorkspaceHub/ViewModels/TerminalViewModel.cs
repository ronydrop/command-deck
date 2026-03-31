using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevWorkspaceHub.Helpers;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.Services;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// ViewModel for a single terminal tab. Handles output parsing and input routing.
/// </summary>
public partial class TerminalViewModel : ObservableObject, IDisposable
{
    private readonly ITerminalService _terminalService;
    private readonly IDatabaseService _db;
    private readonly ITerminalBackgroundService? _backgroundService;
    private readonly AnsiParser _ansiParser = new();
    private readonly Dispatcher _dispatcher;

    // ─── Output throttling ───────────────────────────────────────────────────
    private readonly StringBuilder _outputBuffer = new();
    private readonly object _bufferLock = new();
    private bool _flushScheduled;

    private static readonly SolidColorBrush DefaultBgBrush =
        new(Color.FromRgb(0x1E, 0x1E, 0x2E));

    static TerminalViewModel()
    {
        DefaultBgBrush.Freeze();
    }

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

    /// <summary>Current cursor column position from the ANSI parser's line buffer.</summary>
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

    private Paragraph _currentParagraph;
    private int _maxScrollbackLines = 2000;

    public TerminalViewModel(ITerminalService terminalService, IDatabaseService db,
                             ITerminalBackgroundService? backgroundService = null)
    {
        _terminalService = terminalService;
        _db = db;
        _backgroundService = backgroundService;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _outputDocument = new FlowDocument
        {
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize = 14,
            PagePadding = new Thickness(8, 0, 8, 0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4))
        };

        _currentParagraph = new Paragraph { Margin = new Thickness(0) };
        _outputDocument.Blocks.Add(_currentParagraph);

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
    /// Initializes the terminal by creating a new ConPTY session.
    /// </summary>
    public async Task InitializeAsync(ShellType shellType, string? workingDirectory = null, string? projectId = null)
    {
        ShellType = shellType;
        Title = shellType.GetDisplayName();
        StatusText = "Connecting...";

        try
        {
            Session = await _terminalService.CreateSessionAsync(shellType, workingDirectory, projectId);
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
        _ansiParser.SetColumns(columns);
    }

    /// <summary>
    /// Clears the terminal output.
    /// </summary>
    [RelayCommand]
    private void ClearOutput()
    {
        _dispatcher.Invoke(() =>
        {
            OutputDocument.Blocks.Clear();
            _currentParagraph = new Paragraph { Margin = new Thickness(0) };
            OutputDocument.Blocks.Add(_currentParagraph);
            _ansiParser.Reset();
        });
    }

    /// <summary>
    /// Closes this terminal session.
    /// </summary>
    [RelayCommand]
    private async Task CloseTerminal()
    {
        if (Session != null)
        {
            await _terminalService.CloseSessionAsync(Session.Id);
        }
    }

    // ─── Private Methods ────────────────────────────────────────────────────

    private void OnOutputReceived(string sessionId, string output)
    {
        if (Session?.Id != sessionId) return;

        // Acumula output no buffer e agenda flush com throttle
        lock (_bufferLock)
        {
            _outputBuffer.Append(output);

            if (!_flushScheduled)
            {
                _flushScheduled = true;
                _dispatcher.BeginInvoke(DispatcherPriority.Background, FlushOutputBuffer);
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
            _ansiParser.ParseAndRender(buffered, _currentParagraph);
            TrimScrollback();
        }
        catch
        {
            _currentParagraph.Inlines.Add(new Run(buffered)
            {
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCD, 0xD6, 0xF4))
            });
        }
    }

    private void OnSessionExited(string sessionId)
    {
        if (Session?.Id != sessionId) return;

        _dispatcher.Invoke(() =>
        {
            IsConnected = false;
            StatusText = "Disconnected";

            _currentParagraph.Inlines.Add(new Run("\r\n[Process exited]\r\n")
            {
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8))
            });
        });
    }

    private void TrimScrollback()
    {
        var count = _currentParagraph.Inlines.Count;
        // Only trim when significantly over the limit (125%), trim down to 75%
        // This reduces trim frequency while keeping memory bounded
        var trimThreshold = (int)(_maxScrollbackLines * 1.25);
        if (count <= trimThreshold) return;

        var targetCount = (int)(_maxScrollbackLines * 0.75);
        var toRemove = count - targetCount;
        var inlinesToRemove = new List<System.Windows.Documents.Inline>(toRemove);
        var current = _currentParagraph.Inlines.FirstInline;
        for (int i = 0; i < toRemove && current != null; i++)
        {
            inlinesToRemove.Add(current);
            current = current.NextInline;
        }
        foreach (var inline in inlinesToRemove)
            _currentParagraph.Inlines.Remove(inline);

        // Adjust committed inline count after trimming from the front
        _ansiParser.AdjustCommittedCount(toRemove);
    }

    // ─── Background Sync ────────────────────────────────────────────────────

    private void OnBackgroundChanged()
    {
        _dispatcher.Invoke(SyncBackgroundFromService);
    }

    private void SyncBackgroundFromService()
    {
        if (_backgroundService == null) return;

        BackgroundImage = _backgroundService.ProcessedImage;
        BackgroundOpacity = _backgroundService.Opacity;
        BackgroundStretch = _backgroundService.WallpaperStretch;
        OverlayOpacity = _backgroundService.OverlayOpacity;
        IsDarkOverlay = _backgroundService.IsDarkOverlay;
        HasBackground = _backgroundService.HasWallpaper;
    }

    public void Dispose()
    {
        _terminalService.OutputReceived -= OnOutputReceived;
        _terminalService.SessionExited -= OnSessionExited;

        if (_backgroundService != null)
            _backgroundService.BackgroundChanged -= OnBackgroundChanged;

        if (Session != null)
        {
            _ = _terminalService.CloseSessionAsync(Session.Id);
        }

        GC.SuppressFinalize(this);
    }
}
