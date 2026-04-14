using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Models;

namespace CommandDeck.ViewModels;

/// <summary>
/// Partial class — Pomodoro Timer logic for <see cref="WidgetCanvasItemViewModel"/>.
/// Standard Pomodoro: 25 min work → 5 min short break (×4 cycles) → 15 min long break.
/// Uses a <see cref="DispatcherTimer"/> so property updates land on the UI thread.
/// State is persisted in <see cref="CanvasItemModel.Metadata"/>.
/// </summary>
public partial class WidgetCanvasItemViewModel
{
    public enum PomodoroPhase { Working, ShortBreak, LongBreak }

    // ─── Durations ────────────────────────────────────────────────────────────
    private const int WorkSeconds       = 25 * 60;
    private const int ShortBreakSeconds = 5  * 60;
    private const int LongBreakSeconds  = 15 * 60;
    private const int TotalCycles       = 4;

    // ─── Observable state ────────────────────────────────────────────────────
    // Field name differs from the enum type to avoid the CS0102 name collision
    // that the source generator would create (property "PomodoroPhase" ≡ type "PomodoroPhase").

    [ObservableProperty] private PomodoroPhase _currentPomodoroPhase  = PomodoroPhase.Working;
    [ObservableProperty] private int    _pomodoroSecondsRemaining      = WorkSeconds;
    [ObservableProperty] private bool   _pomodoroIsRunning;
    [ObservableProperty] private int    _pomodoroCycleCount            = 0;
    [ObservableProperty] private double _pomodoroRemainingPercent      = 1.0;

    // ─── Computed display ────────────────────────────────────────────────────

    public string PomodoroTimeDisplay =>
        $"{PomodoroSecondsRemaining / 60:D2}:{PomodoroSecondsRemaining % 60:D2}";

    public string PomodoroPhaseLabel => CurrentPomodoroPhase switch
    {
        PomodoroPhase.Working    => "Foco",
        PomodoroPhase.ShortBreak => "Pausa curta",
        PomodoroPhase.LongBreak  => "Pausa longa",
        _                        => string.Empty
    };

    partial void OnPomodoroSecondsRemainingChanged(int value)
    {
        OnPropertyChanged(nameof(PomodoroTimeDisplay));
        UpdateRemainingPercent();
    }

    partial void OnCurrentPomodoroPhaseChanged(PomodoroPhase value)
    {
        OnPropertyChanged(nameof(PomodoroPhaseLabel));
    }

    // ─── Timer ───────────────────────────────────────────────────────────────

    private DispatcherTimer? _pomodoroTimer;

    private void InitPomodoro()
    {
        _pomodoroTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pomodoroTimer.Tick += OnPomodoroTick;
        RestorePomodoroState();
    }

    private void StopPomodoro()
    {
        _pomodoroTimer?.Stop();
        if (_pomodoroTimer is not null)
            _pomodoroTimer.Tick -= OnPomodoroTick;
        _pomodoroTimer = null;
    }

    private void OnPomodoroTick(object? sender, EventArgs e)
    {
        if (PomodoroSecondsRemaining <= 0)
        {
            AdvancePomodoroPhase();
            return;
        }

        PomodoroSecondsRemaining--;
        PersistPomodoroState();
    }

    // ─── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void PomodoroStartPause()
    {
        if (_pomodoroTimer is null) return;

        if (PomodoroIsRunning)
        {
            _pomodoroTimer.Stop();
            PomodoroIsRunning = false;
        }
        else
        {
            _pomodoroTimer.Start();
            PomodoroIsRunning = true;
        }

        PersistPomodoroState();
    }

    [RelayCommand]
    private void PomodoroSkip()
    {
        AdvancePomodoroPhase();
    }

    [RelayCommand]
    private void PomodoroReset()
    {
        _pomodoroTimer?.Stop();
        PomodoroIsRunning = false;
        CurrentPomodoroPhase = PomodoroPhase.Working;
        PomodoroSecondsRemaining = WorkSeconds;
        PomodoroCycleCount = 0;
        UpdateRemainingPercent();
        PersistPomodoroState();
    }

    // ─── State machine ────────────────────────────────────────────────────────

    private void AdvancePomodoroPhase()
    {
        _pomodoroTimer?.Stop();
        PomodoroIsRunning = false;

        if (CurrentPomodoroPhase == PomodoroPhase.Working)
        {
            PomodoroCycleCount++;
            var isLongBreak = PomodoroCycleCount % TotalCycles == 0;

            CurrentPomodoroPhase     = isLongBreak ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
            PomodoroSecondsRemaining = isLongBreak ? LongBreakSeconds : ShortBreakSeconds;

            NotifyPomodoroPhaseEnd(isLongBreak
                ? "Pausa longa! 15 minutos de descanso."
                : "Pausa curta! 5 minutos.");
        }
        else
        {
            CurrentPomodoroPhase     = PomodoroPhase.Working;
            PomodoroSecondsRemaining = WorkSeconds;
            NotifyPomodoroPhaseEnd("Hora de focar! 25 minutos.");
        }

        UpdateRemainingPercent();
        PersistPomodoroState();
    }

    private void NotifyPomodoroPhaseEnd(string message)
    {
        _notificationService?.Notify(
            title:    "Pomodoro",
            type:     NotificationType.Info,
            source:   NotificationSource.System,
            message:  message,
            duration: TimeSpan.FromSeconds(5));
    }

    private void UpdateRemainingPercent()
    {
        int total = CurrentPomodoroPhase switch
        {
            PomodoroPhase.Working    => WorkSeconds,
            PomodoroPhase.ShortBreak => ShortBreakSeconds,
            PomodoroPhase.LongBreak  => LongBreakSeconds,
            _                        => WorkSeconds
        };

        PomodoroRemainingPercent = total > 0 ? (double)PomodoroSecondsRemaining / total : 0.0;
    }

    // ─── Persistence ──────────────────────────────────────────────────────────

    private void PersistPomodoroState()
    {
        Model.Metadata["pomodoroPhase"]   = CurrentPomodoroPhase.ToString();
        Model.Metadata["pomodoroSeconds"] = PomodoroSecondsRemaining.ToString();
        Model.Metadata["pomodoroCycles"]  = PomodoroCycleCount.ToString();
        Model.Metadata["pomodoroRunning"] = PomodoroIsRunning.ToString();
    }

    private void RestorePomodoroState()
    {
        if (Model.Metadata.TryGetValue("pomodoroPhase", out var phaseStr)
            && Enum.TryParse<PomodoroPhase>(phaseStr, out var phase))
        {
            CurrentPomodoroPhase = phase;
        }

        if (Model.Metadata.TryGetValue("pomodoroSeconds", out var secStr)
            && int.TryParse(secStr, out var sec))
        {
            PomodoroSecondsRemaining = sec;
        }

        if (Model.Metadata.TryGetValue("pomodoroCycles", out var cycStr)
            && int.TryParse(cycStr, out var cyc))
        {
            PomodoroCycleCount = cyc;
        }

        // Never auto-resume across app restarts
        PomodoroIsRunning = false;
        UpdateRemainingPercent();
    }
}
