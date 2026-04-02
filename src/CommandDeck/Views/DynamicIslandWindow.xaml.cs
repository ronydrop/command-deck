using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CommandDeck.Models;
using CommandDeck.ViewModels;

namespace CommandDeck.Views;

/// <summary>
/// Dynamic Island — floating overlay window that shows active terminal sessions.
/// Non-activating (never steals focus), always on top, pill-shaped.
/// Click pill to expand; click '−' to slide up off-screen; auto-shows on notifications.
/// </summary>
public partial class DynamicIslandWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    // ─── Animation constants ─────────────────────────────────────────────────
    private const double PillWidthCompact  = 200;
    private const double PillWidthWithEvent = 300;
    private const double ExpandedWidth     = 360;
    private const double ExpandedMaxHeight = 500;
    private const double ExpandDuration    = 0.35;  // seconds
    private const double CollapseDuration  = 0.28;
    private const double SlideDuration     = 0.30;
    private const double SlideShowDuration = 0.40;

    // ─── State ───────────────────────────────────────────────────────────────
    private readonly DynamicIslandViewModel _viewModel;
    private readonly System.Windows.Threading.DispatcherTimer _collapseTimer;
    private readonly System.Windows.Threading.DispatcherTimer _hoverWatchTimer;
    private readonly System.Windows.Threading.DispatcherTimer _proximityTimer;
    private bool _isAnimating;
    private bool _pendingShow;       // notification arrived while minimize anim was running
    private bool _isPersistent;      // true = locked open; hover ignored until clicked again
    private bool _isMinimized;       // true while hidden off-screen via AnimateMinimize
    private string? _lastPrimaryEventId;
    private double _restingTop = 8;  // WPF DIPs — resting Y position of the island
    private int _screenWorkTop = 0;  // physical pixels — top of monitor work area (for proximity)

    // ─── Mouse helpers (Win32) ───────────────────────────────────────────────

    // WS_EX_NOACTIVATE windows stop receiving WM_MOUSEMOVE when the mouse leaves —
    // WPF's Mouse.GetPosition returns the last cached (stale) position. Use GetCursorPos.
    private bool IsMouseOverPill()
    {
        if (!GetCursorPos(out var pt)) return true;
        try
        {
            var tl = PillBorder.PointToScreen(new Point(0, 0));
            var br = PillBorder.PointToScreen(new Point(PillBorder.ActualWidth, PillBorder.ActualHeight));
            return pt.X >= tl.X && pt.X <= br.X && pt.Y >= tl.Y && pt.Y <= br.Y;
        }
        catch { return true; }
    }

    // Returns true when the cursor is within the proximity zone at the top of the screen.
    private bool IsMouseNearTop()
    {
        if (!GetCursorPos(out var pt)) return false;
        return pt.Y <= _screenWorkTop + 20; // 20 physical-pixel proximity zone
    }

    public DynamicIslandWindow(DynamicIslandViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _collapseTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!IsMouseOverPill() && !_isPersistent)
                AnimateCollapse();
        };

        // Polls mouse position while expanded — WS_EX_NOACTIVATE windows don't reliably get MouseLeave
        _hoverWatchTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _hoverWatchTimer.Tick += (_, _) =>
        {
            if (_isPersistent || _isAnimating) return;
            if (!IsMouseOverPill())
            {
                _hoverWatchTimer.Stop();
                _collapseTimer.Start();
            }
        };

        // Polls cursor proximity to the top edge while minimized off-screen
        _proximityTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _proximityTimer.Tick += (_, _) =>
        {
            if (!_isMinimized) { _proximityTimer.Stop(); return; }
            if (IsMouseNearTop())
            {
                _proximityTimer.Stop();
                OnRequestShowFromMinimized();
            }
        };

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.RequestShowFromMinimized += OnRequestShowFromMinimized;

        App.ThemeApplied += OnThemeApplied;

        Closed += (_, _) =>
        {
            _collapseTimer.Stop();
            _hoverWatchTimer.Stop();
            _proximityTimer.Stop();
            HeartbeatEllipse.IsVisibleChanged -= OnHeartbeatIsVisibleChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.RequestShowFromMinimized -= OnRequestShowFromMinimized;
            _viewModel.Sessions.CollectionChanged -= OnSessionsCollectionChanged;
            App.ThemeApplied -= OnThemeApplied;
        };
    }

    private void ResetToPillState()
    {
        _collapseTimer.Stop();
        _hoverWatchTimer.Stop();
        _proximityTimer.Stop();
        _isAnimating = false;
        _isPersistent = false;
        _isMinimized = false;

        // Cancel any in-flight animations
        this.BeginAnimation(WidthProperty, null);
        this.BeginAnimation(TopProperty, null);
        this.BeginAnimation(OpacityProperty, null);
        ExpandedContent.BeginAnimation(MaxHeightProperty, null);
        ExpandedContent.BeginAnimation(OpacityProperty, null);
        PillContent.BeginAnimation(OpacityProperty, null);

        ExpandedContent.Visibility = Visibility.Collapsed;
        ExpandedContent.MaxHeight  = 0;
        ExpandedContent.Opacity    = 0;
        PillContent.Visibility     = Visibility.Visible;
        PillContent.Opacity        = 1;
        this.Width   = GetPillTargetWidth();
        this.Opacity = 1;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DynamicIslandViewModel.IsVisible))
        {
            if (_viewModel.IsVisible)
            {
                // If minimized (stale off-screen state), restore via the slide-in path
                if (_isMinimized) OnRequestShowFromMinimized();
                else
                {
                    ResetToPillState();
                    CenterHorizontally(GetPillTargetWidth());
                    Show();
                }
            }
            else
            {
                _proximityTimer.Stop();
                Hide();
            }
        }
        else if (e.PropertyName == nameof(DynamicIslandViewModel.ActiveSessionCount) ||
                 e.PropertyName == nameof(DynamicIslandViewModel.HasBusySession) ||
                 e.PropertyName == nameof(DynamicIslandViewModel.PrimaryEvent) ||
                 e.PropertyName == nameof(DynamicIslandViewModel.CurrentPreview) ||
                 e.PropertyName == nameof(DynamicIslandViewModel.IsPrimaryAgentBusy))
        {
            UpdatePillDisplay();
            if (e.PropertyName == nameof(DynamicIslandViewModel.PrimaryEvent))
                AnimatePrimaryEventEmphasis();
        }
    }

    private void OnThemeApplied(Color _) => UpdatePillDisplay();

    private void OnSourceInitialized(object sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SessionList.ItemsSource = _viewModel.Sessions;
        _viewModel.Sessions.CollectionChanged += OnSessionsCollectionChanged;

        HeartbeatEllipse.IsVisibleChanged += OnHeartbeatIsVisibleChanged;

        ResetToPillState();
        UpdatePosition();
        UpdatePillDisplay();
        UpdateEmptyLabel();

        if (!_viewModel.IsVisible)
            Hide();
    }

    private void OnHeartbeatIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (Resources["HeartbeatPulseStoryboard"] is not Storyboard sb) return;
        if (HeartbeatEllipse.IsVisible)
            sb.Begin(HeartbeatEllipse, true);
        else
        {
            HeartbeatEllipse.BeginAnimation(UIElement.OpacityProperty, null);
            HeartbeatEllipse.Opacity = 1;
        }
    }

    private void OnSessionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyLabel();
        UpdatePillDisplay();
    }

    // ─── Position ────────────────────────────────────────────────────────────

    private void UpdatePosition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // GetMonitorInfo returns physical pixels; WPF uses DIPs — must divide by DPI scale
        var source = PresentationSource.FromVisual(this);
        var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        if (hwnd == IntPtr.Zero)
        {
            Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
            Top = 8;
            _restingTop = Top;
            return;
        }

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(monitor, ref info))
        {
            var work = info.rcWork;
            double workLeft  = work.Left  / dpiX;
            double workRight = work.Right / dpiX;
            double workTop   = work.Top   / dpiY;
            Left = workLeft + (workRight - workLeft - ActualWidth) / 2;
            Top  = workTop + 8;
            _screenWorkTop = info.rcWork.Top; // physical pixels for proximity detection
        }
        else
        {
            Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
            Top = 8;
            _screenWorkTop = 0;
        }

        _restingTop = Top;
    }

    private void CenterHorizontally(double windowWidth)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        var source = PresentationSource.FromVisual(this);
        var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        if (hwnd == IntPtr.Zero)
        {
            Left = (SystemParameters.PrimaryScreenWidth - windowWidth) / 2;
            return;
        }

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(monitor, ref info))
        {
            var work = info.rcWork;
            double workLeft  = work.Left  / dpiX;
            double workRight = work.Right / dpiX;
            Left = workLeft + (workRight - workLeft - windowWidth) / 2;
        }
        else
        {
            Left = (SystemParameters.PrimaryScreenWidth - windowWidth) / 2;
        }
    }

    private void UpdateEmptyLabel()
    {
        EmptyLabel.Visibility = _viewModel.Sessions.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>Collapsed pill width: wider when a primary island event is shown.</summary>
    private double GetPillTargetWidth() =>
        _viewModel.PrimaryEvent is null ? PillWidthCompact : PillWidthWithEvent;

    private void UpdatePillDisplay()
    {
        var count = _viewModel.ActiveSessionCount;
        SessionCountText.Text = count == 0
            ? "Nenhuma sessão"
            : count == 1 ? "1 sessão ativa" : $"{count} sessões ativas";

        var dotColor = count == 0
            ? GetThemeColor("Surface2")
            : _viewModel.HasBusySession
                ? GetThemeColor("AccentBlue")
                : GetThemeColor("AccentGreen");

        StatusDotBrush.Color = dotColor;

        // Resize pill when feed shows a primary event (only while collapsed)
        if (!_isAnimating && ExpandedContent.Visibility == Visibility.Collapsed)
        {
            var target = GetPillTargetWidth();
            this.BeginAnimation(WidthProperty, null);
            this.Width = target;
            CenterHorizontally(target);
        }
    }

    private void AnimatePrimaryEventEmphasis()
    {
        var currentId = _viewModel.PrimaryEvent?.EventId;
        if (string.IsNullOrWhiteSpace(currentId) || string.Equals(_lastPrimaryEventId, currentId, StringComparison.Ordinal))
            return;

        _lastPrimaryEventId = currentId;

        var scale = new ScaleTransform(1, 1);
        PillBorder.RenderTransformOrigin = new Point(0.5, 0.5);
        PillBorder.RenderTransform = scale;

        var storyboard = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(260));
        var scaleX = new DoubleAnimation(1, 1.025, duration)
        {
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var scaleY = new DoubleAnimation(1, 1.02, duration)
        {
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(scaleX, PillBorder);
        Storyboard.SetTarget(scaleY, PillBorder);
        Storyboard.SetTargetProperty(scaleX, new PropertyPath("RenderTransform.ScaleX"));
        Storyboard.SetTargetProperty(scaleY, new PropertyPath("RenderTransform.ScaleY"));

        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        storyboard.Begin();
    }

    private static Color GetThemeColor(string key) =>
        Application.Current.Resources[key] is Color c
            ? c
            : Color.FromRgb(0x58, 0x5B, 0x70);

    // ─── Hover expand / collapse ─────────────────────────────────────────────

    private void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isPersistent) return;
        _collapseTimer.Stop();
        _hoverWatchTimer.Stop();
        if (!_isAnimating && ExpandedContent.Visibility == Visibility.Collapsed)
            AnimateExpand();
    }

    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // May not fire reliably for WS_EX_NOACTIVATE — _hoverWatchTimer is the primary mechanism
        if (_isPersistent) return;
        _collapseTimer.Stop();
        if (!_isAnimating)
            _collapseTimer.Start();
    }

    // ─── Click: toggle persistent lock ───────────────────────────────────────

    private void OnPillClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.Handled) return;  // child element (button, row) already handled it
        if (_isAnimating) return;

        if (_isPersistent)
        {
            // Unlock: collapse and return to hover mode
            _isPersistent = false;
            _collapseTimer.Stop();
            AnimateCollapse();
        }
        else
        {
            // Lock open (expand if not already expanded)
            _isPersistent = true;
            _collapseTimer.Stop();
            if (ExpandedContent.Visibility == Visibility.Collapsed)
                AnimateExpand();
        }
    }

    // ─── Expand (Pill → Expanded) ─────────────────────────────────────────────

    private void AnimateExpand()
    {
        _isAnimating = true;

        // Show expanded content at zero opacity so layout is measured
        ExpandedContent.Visibility = Visibility.Visible;

        var easeOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var easeIn  = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var dur350  = new Duration(TimeSpan.FromSeconds(ExpandDuration));
        var dur200  = new Duration(TimeSpan.FromSeconds(0.20));
        var dur100  = new Duration(TimeSpan.FromSeconds(0.10));

        // 1. Window width pill → expanded
        var fromW = this.Width;
        var widthAnim = new DoubleAnimation(fromW, ExpandedWidth, dur350) { EasingFunction = easeOut };
        this.BeginAnimation(WidthProperty, widthAnim);

        // 2. Recenter horizontally (Left follows width change)
        CenterHorizontally(ExpandedWidth);

        // 3. MaxHeight 0 → 500
        var heightAnim = new DoubleAnimation(0, ExpandedMaxHeight, dur350) { EasingFunction = easeOut };
        ExpandedContent.BeginAnimation(MaxHeightProperty, heightAnim);

        // 4. Expanded content opacity 0 → 1 with 150ms delay
        var opacityAnim = new DoubleAnimation(0, 1, dur200)
        {
            BeginTime = TimeSpan.FromMilliseconds(150),
            EasingFunction = easeOut
        };
        ExpandedContent.BeginAnimation(OpacityProperty, opacityAnim);

        // 5. Pill content opacity 1 → 0
        var pillFadeOut = new DoubleAnimation(1, 0, dur100) { EasingFunction = easeIn };
        pillFadeOut.Completed += (_, _) =>
        {
            PillContent.Visibility = Visibility.Collapsed;
            PillContent.BeginAnimation(OpacityProperty, null);
            PillContent.Opacity = 1; // reset for next collapse
        };
        PillContent.BeginAnimation(OpacityProperty, pillFadeOut);

        // Release width animation after complete so manual assignment works again
        widthAnim.Completed += (_, _) =>
        {
            this.BeginAnimation(WidthProperty, null);
            this.Width = ExpandedWidth;
            _isAnimating = false;

            if (!_isPersistent)
            {
                if (!IsMouseOverPill())
                    _collapseTimer.Start();   // mouse already left during animation
                else
                    _hoverWatchTimer.Start(); // start polling; collapses when mouse leaves
            }
        };
    }

    // ─── Collapse (Expanded → Pill) ───────────────────────────────────────────

    private void AnimateCollapse()
    {
        _hoverWatchTimer.Stop();
        _isAnimating = true;

        var easeIn  = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var easeOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var dur280  = new Duration(TimeSpan.FromSeconds(CollapseDuration));
        var dur100  = new Duration(TimeSpan.FromSeconds(0.10));

        // 1. Expanded content opacity 1 → 0
        var opacityAnim = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(0.15)))
        {
            EasingFunction = easeIn
        };
        ExpandedContent.BeginAnimation(OpacityProperty, opacityAnim);

        // 2. MaxHeight 500 → 0
        var heightAnim = new DoubleAnimation(ExpandedMaxHeight, 0, dur280) { EasingFunction = easeIn };
        heightAnim.Completed += (_, _) =>
        {
            ExpandedContent.Visibility = Visibility.Collapsed;
            ExpandedContent.BeginAnimation(MaxHeightProperty, null);
            ExpandedContent.MaxHeight = 0;
            ExpandedContent.BeginAnimation(OpacityProperty, null);
            ExpandedContent.Opacity = 0;
        };
        ExpandedContent.BeginAnimation(MaxHeightProperty, heightAnim);

        // 3. Window width expanded → pill
        var toPill = GetPillTargetWidth();
        var widthAnim = new DoubleAnimation(ExpandedWidth, toPill, dur280) { EasingFunction = easeIn };
        widthAnim.Completed += (_, _) =>
        {
            this.BeginAnimation(WidthProperty, null);
            this.Width = toPill;
            CenterHorizontally(toPill);
            _isAnimating = false;

            // Mouse re-entered while collapsing — expand again
            if (IsMouseOverPill() && !_isPersistent)
                AnimateExpand();
        };
        this.BeginAnimation(WidthProperty, widthAnim);

        // 4. Recenter immediately (will follow as width animates)
        CenterHorizontally(toPill);

        // 5. Pill content fade in
        PillContent.Visibility = Visibility.Visible;
        PillContent.Opacity = 0;
        var pillFadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(0.18)))
        {
            BeginTime = TimeSpan.FromMilliseconds(100),
            EasingFunction = easeOut
        };
        pillFadeIn.Completed += (_, _) =>
        {
            PillContent.BeginAnimation(OpacityProperty, null);
            PillContent.Opacity = 1;
        };
        PillContent.BeginAnimation(OpacityProperty, pillFadeIn);
    }

    // ─── Minimize button click ────────────────────────────────────────────────

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // prevent bubble-up to OnPillClick
        // Reset state fully so minimize always works regardless of current animation
        _collapseTimer.Stop();
        _hoverWatchTimer.Stop();
        _isPersistent = false;
        _isAnimating = false;
        _viewModel.MinimizeCommand.Execute(null); // sets IslandState = Minimized on VM
        AnimateMinimize();
    }

    // ─── Slide up off-screen (Expanded → Minimized) ───────────────────────────

    private void AnimateMinimize()
    {
        _isAnimating = true;

        // First collapse expanded content instantly
        ExpandedContent.Visibility = Visibility.Collapsed;
        ExpandedContent.BeginAnimation(MaxHeightProperty, null);
        ExpandedContent.MaxHeight = 0;
        ExpandedContent.BeginAnimation(OpacityProperty, null);
        ExpandedContent.Opacity = 0;
        PillContent.Visibility = Visibility.Visible;
        PillContent.Opacity = 1;
        this.BeginAnimation(WidthProperty, null);
        this.Width = GetPillTargetWidth();
        CenterHorizontally(GetPillTargetWidth());

        var easeIn = new ExponentialEase { EasingMode = EasingMode.EaseIn, Exponent = 4 };
        var dur300 = new Duration(TimeSpan.FromSeconds(SlideDuration));

        double targetTop = -(ActualHeight + 20);

        var topAnim = new DoubleAnimation(Top, targetTop, dur300) { EasingFunction = easeIn };
        var fadeAnim = new DoubleAnimation(1, 0.3, dur300)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        topAnim.Completed += (_, _) =>
        {
            this.BeginAnimation(TopProperty, null);
            this.BeginAnimation(OpacityProperty, null);
            Hide();
            _isAnimating = false;
            _isMinimized = true;

            if (_pendingShow)
            {
                _pendingShow = false;
                OnRequestShowFromMinimized();
            }
            else
            {
                // Watch for mouse proximity to the top edge — slide back in when near
                _proximityTimer.Start();
            }
        };

        this.BeginAnimation(TopProperty, topAnim);
        this.BeginAnimation(OpacityProperty, fadeAnim);
    }

    // ─── Slide down from off-screen (Minimized → Pill) ───────────────────────

    private void OnRequestShowFromMinimized()
    {
        _proximityTimer.Stop();

        // If currently animating minimize, flag and let Completed handle it
        if (_isAnimating)
        {
            _pendingShow = true;
            return;
        }

        if (!_viewModel.IsVisible) return;

        // Ensure pill state is reset
        ExpandedContent.Visibility = Visibility.Collapsed;
        ExpandedContent.MaxHeight = 0;
        ExpandedContent.Opacity = 0;
        PillContent.Visibility = Visibility.Visible;
        PillContent.Opacity = 1;
        var pillW = GetPillTargetWidth();
        this.Width = pillW;

        // Position off-screen above
        CenterHorizontally(pillW);
        this.Opacity = 0.3;
        Top = -(_restingTop + ActualHeight + 20);

        Show();

        _isAnimating = true;

        var easeOut = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 4 };
        var dur400  = new Duration(TimeSpan.FromSeconds(SlideShowDuration));

        var topAnim  = new DoubleAnimation(Top, _restingTop, dur400) { EasingFunction = easeOut };
        var fadeAnim = new DoubleAnimation(0.3, 1, new Duration(TimeSpan.FromSeconds(0.25)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        topAnim.Completed += (_, _) =>
        {
            this.BeginAnimation(TopProperty, null);
            this.Top = _restingTop;
            this.BeginAnimation(OpacityProperty, null);
            this.Opacity = 1;
            _isAnimating = false;
            _isMinimized = false;

            // If mouse is already over the pill (brought it up via proximity), expand
            if (IsMouseOverPill() && !_isPersistent)
                AnimateExpand();
        };

        this.BeginAnimation(TopProperty, topAnim);
        this.BeginAnimation(OpacityProperty, fadeAnim);
    }

    // ─── Click handlers ──────────────────────────────────────────────────────

    private void OnSessionRowClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string sessionId)
            _viewModel.NavigateToSessionCommand.Execute(sessionId);
        e.Handled = true; // prevent bubble-up to OnPillClick
    }

    private void OnSessionCloseClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string sessionId)
            _ = _viewModel.CloseSessionCommand.ExecuteAsync(sessionId);
        // Button.Click sets e.Handled automatically
    }

    private void OnOpenCommandDeckClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var main = Application.Current.MainWindow;
        if (main != null)
        {
            if (!main.IsVisible) main.Show();
            if (main.WindowState == WindowState.Minimized)
                main.WindowState = WindowState.Normal;
            main.Activate();
            main.Focus();
        }
        e.Handled = true; // prevent bubble-up to OnPillClick
    }
}
