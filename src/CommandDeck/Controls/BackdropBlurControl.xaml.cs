using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace CommandDeck.Controls;

/// <summary>
/// A reusable "frosted glass" backdrop control.
///
/// USAGE: Place this control BEHIND overlay content (lower z-index) to give that overlay
/// a frosted-glass appearance.
///
/// LIMITATION: The blur is a static snapshot captured at <see cref="OnLoaded"/> time.
/// This is only suitable for overlays that appear over content that does not change
/// frequently, such as Command Palette, modal dialogs, and settings panes.
///
/// IMPLEMENTATION: Captures a <see cref="VisualBrush"/> of the parent element at the
/// position of this control, then applies a <see cref="BlurEffect"/> to create the
/// frosted-glass illusion. A semi-transparent tint overlay adds depth.
/// </summary>
public partial class BackdropBlurControl : UserControl
{
    // ─── Dependency Properties ───────────────────────────────────────────────

    public static readonly DependencyProperty BlurRadiusProperty =
        DependencyProperty.Register(nameof(BlurRadius), typeof(double),
            typeof(BackdropBlurControl), new PropertyMetadata(10.0, OnBlurRadiusChanged));

    /// <summary>Radius of the blur effect (default: 10).</summary>
    public double BlurRadius
    {
        get => (double)GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    public static readonly DependencyProperty TintColorProperty =
        DependencyProperty.Register(nameof(TintColor), typeof(Color),
            typeof(BackdropBlurControl),
            new PropertyMetadata(Color.FromArgb(0x99, 0x1E, 0x1E, 0x2E), OnTintColorChanged));

    /// <summary>Background tint color (default: dark #1E1E2E at 60% opacity).</summary>
    public Color TintColor
    {
        get => (Color)GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    // ─── Constructor ─────────────────────────────────────────────────────────

    public BackdropBlurControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    /// <summary>
    /// Captures a static snapshot of the parent visual positioned at this control's
    /// location, and applies it as a blurred background.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Sync DP values that may have been set before Loaded
        BlurFx.Radius   = BlurRadius;
        TintBrush.Color = TintColor;

        var parent = VisualTreeHelper.GetParent(this) as UIElement;
        if (parent is null || ActualWidth <= 0 || ActualHeight <= 0) return;

        try
        {
            // Get our position within the parent element
            var transform = TransformToAncestor(parent);
            var origin    = transform.Transform(new Point(0, 0));

            // VisualBrush with Viewbox matching our exact position within the parent.
            // This renders the parent's content at the coordinates occupied by this control,
            // creating the illusion of seeing through to what's behind.
            BackdropRect.Fill = new VisualBrush(parent)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox      = new Rect(origin, new Size(ActualWidth, ActualHeight)),
                Stretch      = Stretch.Fill,
                AlignmentX   = AlignmentX.Left,
                AlignmentY   = AlignmentY.Top
            };
        }
        catch
        {
            // If the parent isn't in the visual tree yet, the blur simply won't render.
            // The tint overlay still shows, which is acceptable.
        }
    }

    // ─── Property change callbacks ───────────────────────────────────────────

    private static void OnBlurRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BackdropBlurControl ctrl && ctrl.IsLoaded)
            ctrl.BlurFx.Radius = (double)e.NewValue;
    }

    private static void OnTintColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BackdropBlurControl ctrl && ctrl.IsLoaded)
            ctrl.TintBrush.Color = (Color)e.NewValue;
    }
}
