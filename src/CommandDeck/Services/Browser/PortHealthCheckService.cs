using System.Collections.Concurrent;
using System.Net.Sockets;

namespace CommandDeck.Services.Browser;

public enum PortHealthState { Unknown, Healthy, Down }

public class PortHealthCheckService : IDisposable
{
    private readonly ConcurrentDictionary<int, PortHealthState> _monitored = new();
    private readonly ConcurrentDictionary<int, int> _consecutiveCount = new();
    private Timer? _timer;
    private bool _disposed;

    private const int AntiFlappingThreshold = 2;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);

    public event Action<int, bool>? PortStateChanged;

    public PortHealthCheckService()
    {
        _timer = new Timer(OnTimerTick, null, CheckInterval, CheckInterval);
    }

    public void StartMonitoring(int port)
    {
        _monitored.TryAdd(port, PortHealthState.Unknown);
        _consecutiveCount.TryAdd(port, 0);
    }

    public void StopMonitoring(int port)
    {
        _monitored.TryRemove(port, out _);
        _consecutiveCount.TryRemove(port, out _);
    }

    public async Task<bool> IsPortHealthyAsync(int port)
    {
        try
        {
            using var cts = new CancellationTokenSource(ConnectTimeout);
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async void OnTimerTick(object? state)
    {
        var ports = _monitored.Keys.ToArray();

        foreach (var port in ports)
        {
            if (!_monitored.TryGetValue(port, out var currentState))
                continue;

            var isHealthy = await IsPortHealthyAsync(port);
            var newState = isHealthy ? PortHealthState.Healthy : PortHealthState.Down;

            if (newState == currentState)
            {
                _consecutiveCount[port] = 0;
                continue;
            }

            var count = _consecutiveCount.AddOrUpdate(port, 1, (_, c) => c + 1);

            if (count >= AntiFlappingThreshold)
            {
                _monitored[port] = newState;
                _consecutiveCount[port] = 0;
                PortStateChanged?.Invoke(port, isHealthy);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
        GC.SuppressFinalize(this);
    }
}
