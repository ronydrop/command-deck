using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

/// <summary>
/// Code-behind for the Pomodoro Timer widget.
/// Handles the circular arc progress indicator and cycle-dot opacity.
/// The arc is redrawn whenever <see cref="WidgetCanvasItemViewModel.PomodoroRemainingPercent"/> changes.
/// </summary>
public partial class PomodoroWidgetControl : UserControl
{
    // Arc geometry constants (must match the XAML Path setup)
    private const double CenterX = 70.0;
    private const double CenterY = 70.0;
    private const double Radius  = 60.0;

    private WidgetCanvasItemViewModel? _vm;

    public PomodoroWidgetControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => UpdateArc(1.0); // Default full arc on load
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as WidgetCanvasItemViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            UpdateArc(_vm.PomodoroRemainingPercent);
            UpdateCycleDots(_vm.PomodoroCycleCount);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WidgetCanvasItemViewModel.PomodoroRemainingPercent):
                UpdateArc(_vm!.PomodoroRemainingPercent);
                break;
            case nameof(WidgetCanvasItemViewModel.PomodoroCycleCount):
                UpdateCycleDots(_vm!.PomodoroCycleCount);
                break;
        }
    }

    /// <summary>
    /// Redraws the circular arc based on <paramref name="remaining"/> (0.0–1.0).
    /// 1.0 = full circle (phase just started), 0.0 = no arc (phase complete).
    /// Uses trigonometry to compute the ArcSegment endpoint.
    /// </summary>
    private void UpdateArc(double remaining)
    {
        remaining = Math.Clamp(remaining, 0.0, 1.0);

        if (remaining <= 0.001)
        {
            // Hide the arc — collapse path
            ArcPath.Visibility = Visibility.Collapsed;
            return;
        }

        ArcPath.Visibility = Visibility.Visible;

        if (remaining >= 0.999)
        {
            // Full circle: draw two 180° arcs to avoid degenerate geometry
            var topPoint    = new Point(CenterX, CenterY - Radius);
            var bottomPoint = new Point(CenterX, CenterY + Radius);
            var arcSize     = new Size(Radius, Radius);

            ArcFigure.StartPoint = topPoint;
            ArcSegment.Point = bottomPoint;
            ArcSegment.Size  = arcSize;
            ArcSegment.IsLargeArc = true;
            ArcSegment.SweepDirection = SweepDirection.Clockwise;
            return;
        }

        // Partial arc: start at 12 o'clock, sweep clockwise by (remaining * 360°)
        double angle   = remaining * 2 * Math.PI;
        double endX    = CenterX + Radius * Math.Sin(angle);
        double endY    = CenterY - Radius * Math.Cos(angle);

        ArcFigure.StartPoint        = new Point(CenterX, CenterY - Radius);
        ArcSegment.Point            = new Point(endX, endY);
        ArcSegment.Size             = new Size(Radius, Radius);
        ArcSegment.IsLargeArc       = remaining > 0.5;
        ArcSegment.SweepDirection   = SweepDirection.Clockwise;
    }

    /// <summary>
    /// Updates the four cycle indicator dots based on completed cycles (mod 4).
    /// A completed cycle in the current round lights up with full opacity.
    /// </summary>
    private void UpdateCycleDots(int cycleCount)
    {
        int completed = cycleCount % 4;
        Ellipse[] dots = [Dot1, Dot2, Dot3, Dot4];

        for (int i = 0; i < dots.Length; i++)
            dots[i].Opacity = i < completed ? 1.0 : 0.25;
    }
}
