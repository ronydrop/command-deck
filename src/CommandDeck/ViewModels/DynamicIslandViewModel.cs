using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// ViewModel for the Dynamic Island floating overlay window.
/// Tracks active terminal sessions and provides navigation actions.
/// </summary>
public partial class DynamicIslandViewModel : ObservableObject, IDisposable
{
    private readonly ITerminalSessionService _sessionService;
    private readonly ISettingsService _settingsService;
    private readonly Lazy<MainViewModel> _mainViewModel;
    private DispatcherTimer? _durationTimer;
    private bool _disposed;
    private bool _savingVisibility;

    public ObservableCollection<DynamicIslandSessionItem> Sessions { get; } = new();

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private int _activeSessionCount;

    [ObservableProperty]
    private bool _hasBusySession;

    public DynamicIslandViewModel(
        ITerminalSessionService sessionService,
        ISettingsService settingsService,
        Lazy<MainViewModel> mainViewModel)
    {
        _sessionService = sessionService;
        _settingsService = settingsService;
        _mainViewModel = mainViewModel;

        _sessionService.SessionCreated += OnSessionCreated;
        _sessionService.SessionClosed += OnSessionClosed;
        _sessionService.SessionStateChanged += OnSessionStateChanged;
    }

    /// <summary>
    /// Loads existing active sessions. Called from App.InitializeServicesAsync after DI is ready.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            IsVisible = settings.IsDynamicIslandEnabled;

            var active = _sessionService.GetActiveSessions();
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var session in active)
                    AddSessionItem(session);

                RefreshCounts();

                // Start timer on the UI thread so it captures the correct Dispatcher
                _durationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _durationTimer.Tick += (_, _) => UpdateDurations();
                _durationTimer.Start();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DynamicIsland] InitializeAsync failed: {ex}");
        }
    }

    [RelayCommand]
    private void NavigateToSession(string sessionId)
    {
        try
        {
            _mainViewModel.Value.FocusSessionById(sessionId);
            Application.Current.MainWindow?.Activate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DynamicIsland] NavigateToSession failed: {ex}");
        }
    }

    [RelayCommand]
    private async Task CloseSession(string sessionId)
    {
        try
        {
            await _sessionService.CloseSessionAsync(sessionId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DynamicIsland] CloseSession failed: {ex}");
        }
    }

    [RelayCommand]
    private async Task ToggleVisibility()
    {
        // Guard against rapid double-invocation causing a race on the settings file
        if (_savingVisibility) return;

        IsVisible = !IsVisible;
        _savingVisibility = true;
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            settings.IsDynamicIslandEnabled = IsVisible;
            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DynamicIsland] ToggleVisibility save failed: {ex}");
        }
        finally
        {
            _savingVisibility = false;
        }
    }

    private void OnSessionCreated(TerminalSessionModel model)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            AddSessionItem(model);
            RefreshCounts();
        });
    }

    private void OnSessionClosed(string sessionId)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var item = Sessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (item != null)
                Sessions.Remove(item);

            RefreshCounts();
        });
    }

    private void OnSessionStateChanged(string sessionId, SessionState newState)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var item = Sessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (item != null)
                item.SessionState = newState;

            RefreshCounts();
        });
    }

    private void AddSessionItem(TerminalSessionModel model)
    {
        var item = new DynamicIslandSessionItem
        {
            SessionId = model.Id,
            Title = model.Title,
            ShellType = model.ShellType,
            SessionState = model.SessionState,
            IsAiSession = model.IsAiSession,
            AiModelUsed = model.AiModelUsed,
            CreatedAt = model.CreatedAt
        };
        item.UpdateDuration();
        Sessions.Add(item);
    }

    private void RefreshCounts()
    {
        ActiveSessionCount = Sessions.Count;
        HasBusySession = Sessions.Any(s => s.SessionState == SessionState.Busy);
    }

    private void UpdateDurations()
    {
        foreach (var item in Sessions)
            item.UpdateDuration();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sessionService.SessionCreated -= OnSessionCreated;
        _sessionService.SessionClosed -= OnSessionClosed;
        _sessionService.SessionStateChanged -= OnSessionStateChanged;

        _durationTimer?.Stop();
    }
}
