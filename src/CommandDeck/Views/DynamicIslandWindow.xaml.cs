using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CommandDeck.ViewModels;

namespace CommandDeck.Views;

/// <summary>
/// Dynamic Island — floating overlay window that shows active terminal sessions.
/// Non-activating (never steals focus), always on top, pill-shaped.
/// </summary>
public partial class DynamicIslandWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly DynamicIslandViewModel _viewModel;
    private DispatcherTimer? _collapseTimer;
    private DispatcherTimer? _expandTimer;

    private const double CollapsedMaxHeight = 0;
    private const double ExpandedMaxHeight = 500;
    private const double AnimDuration = 0.28; // seconds

    public DynamicIslandWindow(DynamicIslandViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Keep title bar in sync whenever the theme changes
        App.ThemeApplied += OnThemeApplied;

        Closed += (_, _) =>
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
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
        // Apply WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW so the window never steals focus
        // and is hidden from Alt+Tab — same P/Invoke pattern as MainWindow DWM interop
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Bind ItemsSource once — ObservableCollection notifies changes automatically
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

    private void UpdatePosition()
    {
        Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
        Top = 8;
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

        // Use theme resource colors — respects active theme (Catppuccin, Dracula, etc.)
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
        _collapseTimer?.Stop();

        _expandTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _expandTimer.Tick += (_, _) =>
        {
            _expandTimer?.Stop();
            Expand();
        };
        _expandTimer.Start();
    }

    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _expandTimer?.Stop();

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer?.Stop();
            Collapse();
        };
        _collapseTimer.Start();
    }

    private void Expand()
    {
        if (ExpandedContent.Visibility == Visibility.Visible) return;

        PillContent.Visibility = Visibility.Collapsed;
        ExpandedContent.Visibility = Visibility.Visible;

        var anim = new DoubleAnimation(CollapsedMaxHeight, ExpandedMaxHeight,
            new Duration(TimeSpan.FromSeconds(AnimDuration)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        ExpandedContent.BeginAnimation(MaxHeightProperty, anim);

        UpdatePosition();
    }

    private void Collapse()
    {
        if (ExpandedContent.Visibility == Visibility.Collapsed) return;

        // Fix: use the known constant, not the live MaxHeight (which may be mid-animation)
        var anim = new DoubleAnimation(ExpandedMaxHeight, CollapsedMaxHeight,
            new Duration(TimeSpan.FromSeconds(AnimDuration)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        anim.Completed += (_, _) =>
        {
            ExpandedContent.Visibility = Visibility.Collapsed;
            PillContent.Visibility = Visibility.Visible;
            UpdatePosition();
        };

        ExpandedContent.BeginAnimation(MaxHeightProperty, anim);
    }

    // ─── Click handlers ──────────────────────────────────────────────────────

    private void OnCollapseClick(object sender, RoutedEventArgs e)
    {
        _collapseTimer?.Stop();
        _expandTimer?.Stop();
        Collapse();
    }

    private void OnSessionRowClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string sessionId)
            _viewModel.NavigateToSessionCommand.Execute(sessionId);
    }

    private void OnSessionCloseClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string sessionId)
            _ = _viewModel.CloseSessionCommand.ExecuteAsync(sessionId);
    }

    private void OnOpenCommandDeckClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Application.Current.MainWindow?.Activate();
        Application.Current.MainWindow?.Focus();
    }
}
