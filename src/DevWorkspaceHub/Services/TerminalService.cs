using System.Collections.Concurrent;
using DevWorkspaceHub.Helpers;
using DevWorkspaceHub.Models;

namespace DevWorkspaceHub.Services;

/// <summary>
/// Manages terminal sessions using the Windows ConPTY API.
/// Each session spawns a real pseudo console process.
/// </summary>
public class TerminalService : ITerminalService, IDisposable
{
    private readonly ConcurrentDictionary<string, TerminalSession> _sessions = new();
    private readonly ConcurrentDictionary<string, ConPtyHelper.ConPtySession> _conPtySessions = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _readCancellations = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _closeLocks = new();

    public event Action<string, string>? OutputReceived;
    public event Action<string>? SessionExited;
    public event Action<string, string>? TitleChanged;

    public async Task<TerminalSession> CreateSessionAsync(
        ShellType shellType,
        string? workingDirectory = null,
        string? projectId = null,
        short columns = 120,
        short rows = 30)
    {
        var session = new TerminalSession
        {
            ShellType = shellType,
            Title = shellType.GetDisplayName(),
            WorkingDirectory = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ProjectId = projectId,
            Status = TerminalStatus.Starting
        };

        try
        {
            // Build the command string
            string executable = shellType.GetExecutablePath();
            string arguments = shellType.GetArguments();
            string command = string.IsNullOrEmpty(arguments) ? executable : $"{executable} {arguments}";

            // Create the ConPTY session
            var conPtySession = ConPtyHelper.CreateSession(
                command,
                session.WorkingDirectory,
                columns,
                rows);

            session.PseudoConsoleHandle = conPtySession.PseudoConsoleHandle;
            session.ProcessId = conPtySession.ProcessInfo.dwProcessId;
            session.Status = TerminalStatus.Running;

            _sessions[session.Id] = session;
            _conPtySessions[session.Id] = conPtySession;

            // Start reading output asynchronously
            var cts = new CancellationTokenSource();
            _readCancellations[session.Id] = cts;
            session.CancellationSource = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await ConPtyHelper.ReadOutputAsync(conPtySession, output =>
                    {
                        OutputReceived?.Invoke(session.Id, output);
                    }, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown via cancellation token
                }
                catch (Exception)
                {
                    // I/O error from process exit or unexpected failure
                }
                finally
                {
                    // Always mark session as stopped and notify subscribers
                    session.Status = TerminalStatus.Stopped;
                    SessionExited?.Invoke(session.Id);
                }
            });
        }
        catch (Exception ex)
        {
            session.Status = TerminalStatus.Error;
            session.Title = $"Error: {ex.Message}";
            _sessions[session.Id] = session;
        }

        return session;
    }

    public async Task WriteAsync(string sessionId, string text)
    {
        if (_conPtySessions.TryGetValue(sessionId, out var conPtySession))
        {
            await ConPtyHelper.WriteInputAsync(conPtySession, text);
        }
    }

    public async Task SendKeyAsync(string sessionId, string keySequence)
    {
        await WriteAsync(sessionId, keySequence);
    }

    public void Resize(string sessionId, short columns, short rows)
    {
        if (_sessions.TryGetValue(sessionId, out var session) &&
            session.PseudoConsoleHandle != IntPtr.Zero)
        {
            try
            {
                ConPtyHelper.Resize(session.PseudoConsoleHandle, columns, rows);
            }
            catch
            {
                // Resize may fail if process already exited
            }
        }
    }

    public async Task CloseSessionAsync(string sessionId)
    {
        // Per-session semaphore prevents double-dispose if two callers race on the same ID.
        var closeLock = _closeLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await closeLock.WaitAsync();
        try
        {
            if (_readCancellations.TryRemove(sessionId, out var cts))
            {
                await cts.CancelAsync();
                cts.Dispose();
            }

            if (_conPtySessions.TryRemove(sessionId, out var conPtySession))
            {
                conPtySession.Dispose();
            }

            if (_sessions.TryRemove(sessionId, out var session))
            {
                session.Status = TerminalStatus.Stopped;
            }
        }
        finally
        {
            closeLock.Release();
            _closeLocks.TryRemove(sessionId, out _);
        }
    }

    public async Task CloseAllSessionsAsync()
    {
        var sessionIds = _sessions.Keys.ToList();
        foreach (var id in sessionIds)
        {
            await CloseSessionAsync(id);
        }
    }

    public IReadOnlyList<TerminalSession> GetSessions() =>
        _sessions.Values.ToList().AsReadOnly();

    public TerminalSession? GetSession(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;

    public void Dispose()
    {
        // Run cleanup on the thread pool to avoid deadlocking a SynchronizationContext
        // (e.g. WPF UI thread). By this point OnExit already closed sessions, so
        // this call is typically a fast no-op.
        try
        {
            Task.Run(CloseAllSessionsAsync).GetAwaiter().GetResult();
        }
        catch { }

        GC.SuppressFinalize(this);
    }
}
