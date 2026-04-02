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

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

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
    private const double PillWidth         = 200;
    private const double ExpandedWidth     = 340;
    private const double ExpandedMaxHeight = 500;
    private const double ExpandDuration    = 0.35;  // seconds
    private const double CollapseDuration  = 0.28;
    private const double SlideDuration     = 0.30;
    private const double SlideShowDuration = 0.40;

    // ─── State ───────────────────────────────────────────────────────────────
    private readonly DynamicIslandViewModel _viewModel;
    private readonly System.Windows.Threading.DispatcherTimer _collapseTimer;
    private bool _isAnimating;
    private bool _pendingShow;       // notification arrived while minimize anim was running
    private bool _isPersistent;      // true = locked open; hover ignored until clicked again
    private double _restingTop = 8;  // persisted so slide-down can return to correct position

    public DynamicIslandWindow(DynamicIslandViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _collapseTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        _collapseTimer.Tick += (_, _) => { _collapseTimer.Stop(); AnimateCollapse(); };

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.RequestShowFromMinimized += OnRequestShowFromMinimized;

        App.ThemeApplied += OnThemeApplied;

        Closed += (_, _) =>
        {
            _collapseTimer.Stop();
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.RequestShowFromMinimized -= OnRequestShowFromMinimized;
            _viewModel.Sessions.CollectionChanged -= OnSessionsCollectionChanged;
            App.ThemeApplied -= OnThemeApplied;
        };
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DynamicIslandViewModel.IsVisible))
        {
            if (_viewModel.IsVisible) Show();
            else Hide();
        }
        else if (e.PropertyName == nameof(DynamicIslandViewModel.ActiveSessionCount) ||
                 e.PropertyName == nameof(DynamicIslandViewModel.HasBusySession))
        {
            UpdatePillDisplay();
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

        UpdatePosition();
        UpdatePillDisplay();
        UpdateEmptyLabel();

        if (!_viewModel.IsVisible)
            Hide();
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
            Left = work.Left + (work.Right - work.Left - ActualWidth) / 2;
            Top = work.Top + 8;
        }
        else
        {
            Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
            Top = 8;
        }

        _restingTop = Top;
    }

    private void CenterHorizontally(double windowWidth)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
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
            Left = work.Left + (work.Right - work.Left - windowWidth) / 2;
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
        if (!_isAnimating && ExpandedContent.Visibility == Visibility.Collapsed)
            AnimateExpand();
    }

    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isPersistent) return;
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
        var widthAnim = new DoubleAnimation(PillWidth, ExpandedWidth, dur350) { EasingFunction = easeOut };
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
        };
    }

    // ─── Collapse (Expanded → Pill) ───────────────────────────────────────────

    private void AnimateCollapse()
    {
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
        var widthAnim = new DoubleAnimation(ExpandedWidth, PillWidth, dur280) { EasingFunction = easeIn };
        widthAnim.Completed += (_, _) =>
        {
            this.BeginAnimation(WidthProperty, null);
            this.Width = PillWidth;
            CenterHorizontally(PillWidth);
            _isAnimating = false;
        };
        this.BeginAnimation(WidthProperty, widthAnim);

        // 4. Recenter immediately (will follow as width animates)
        CenterHorizontally(PillWidth);

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
        if (_isAnimating) return;
        _isPersistent = false;
        _collapseTimer.Stop();
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
        this.Width = PillWidth;
        CenterHorizontally(PillWidth);

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

            // If a notification arrived during animation, show immediately
            if (_pendingShow)
            {
                _pendingShow = false;
                OnRequestShowFromMinimized();
            }
        };

        this.BeginAnimation(TopProperty, topAnim);
        this.BeginAnimation(OpacityProperty, fadeAnim);
    }

    // ─── Slide down from off-screen (Minimized → Pill) ───────────────────────

    private void OnRequestShowFromMinimized()
    {
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
        this.Width = PillWidth;

        // Position off-screen above
        CenterHorizontally(PillWidth);
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
