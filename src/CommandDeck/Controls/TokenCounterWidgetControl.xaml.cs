using System.Windows.Controls;
using System.Windows.Threading;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

/// <summary>
/// Code-behind for the Token Counter widget control.
/// Debounces the estimator TextBox at 250 ms to avoid excessive re-calculations on fast typing.
/// All session-level stats are driven by <see cref="WidgetCanvasItemViewModel"/> via bindings.
/// </summary>
public partial class TokenCounterWidgetControl : UserControl
{
    private readonly DispatcherTimer _debounce;

    public TokenCounterWidgetControl()
    {
        InitializeComponent();

        _debounce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _debounce.Tick += OnDebounceElapsed;

        EstimatorTextBox.TextChanged += OnEstimatorTextChanged;
    }

    private void OnEstimatorTextChanged(object sender, TextChangedEventArgs e)
    {
        // Restart the debounce window on each keystroke.
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnDebounceElapsed(object? sender, EventArgs e)
    {
        _debounce.Stop();
        if (DataContext is not WidgetCanvasItemViewModel vm) return;
        vm.EstimateTokens(EstimatorTextBox.Text ?? string.Empty);
    }
}
