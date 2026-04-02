using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CommandDeck.Controls;

/// <summary>
/// Radial menu displayed around the AI Orb when activated.
/// Positions 5 action buttons at computed orbital positions and
/// plays a stagger open animation when it becomes visible.
/// </summary>
public partial class RadialMenuControl : UserControl
{
    // Stagger order: Agent → Voice → Improve → Run → Copy
    private static readonly string[] _staggerOrder =
    [
        "BtnChooseAgent",
        "BtnVoice",
        "BtnImprovePrompt",
        "BtnRunSuggestion",
        "BtnCopyContext"
    ];

    private const double StaggerDelayMs   = 30;
    private const double OpacityDuration  = 150;
    private const double ScaleDuration    = 220;

    public RadialMenuControl()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
            PlayStaggerOpen();
        else
            ResetButtons();
    }

    private void PlayStaggerOpen()
    {
        // Reset first so re-opening from a half-animated state looks clean.
        ResetButtons();

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        for (int i = 0; i < _staggerOrder.Length; i++)
        {
            if (FindName(_staggerOrder[i]) is not FrameworkElement btn) continue;

            double beginTime = i * StaggerDelayMs;

            // Opacity: 0 → 1
            var opAnim = new DoubleAnimation
            {
                From         = 0,
                To           = 1,
                Duration     = TimeSpan.FromMilliseconds(OpacityDuration),
                BeginTime    = TimeSpan.FromMilliseconds(beginTime),
                EasingFunction = ease
            };

            // ScaleX: 0.5 → 1.0
            var sxAnim = new DoubleAnimation
            {
                From         = 0.5,
                To           = 1.0,
                Duration     = TimeSpan.FromMilliseconds(ScaleDuration),
                BeginTime    = TimeSpan.FromMilliseconds(beginTime),
                EasingFunction = ease
            };

            // ScaleY: 0.5 → 1.0
            var syAnim = new DoubleAnimation
            {
                From         = 0.5,
                To           = 1.0,
                Duration     = TimeSpan.FromMilliseconds(ScaleDuration),
                BeginTime    = TimeSpan.FromMilliseconds(beginTime),
                EasingFunction = ease
            };

            // Ensure the button has a ScaleTransform to animate
            if (btn.RenderTransform is not ScaleTransform)
            {
                btn.RenderTransformOrigin = new Point(0.5, 0.5);
                btn.RenderTransform = new ScaleTransform(0.5, 0.5);
            }

            btn.BeginAnimation(OpacityProperty, opAnim);
            ((ScaleTransform)btn.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, sxAnim);
            ((ScaleTransform)btn.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, syAnim);
        }
    }

    private void ResetButtons()
    {
        foreach (var name in _staggerOrder)
        {
            if (FindName(name) is not FrameworkElement btn) continue;

            // Clear running animations before resetting values
            btn.BeginAnimation(OpacityProperty, null);
            btn.Opacity = 0;

            if (btn.RenderTransform is ScaleTransform st)
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                st.ScaleX = 0.5;
                st.ScaleY = 0.5;
            }
            else
            {
                btn.RenderTransformOrigin = new Point(0.5, 0.5);
                btn.RenderTransform = new ScaleTransform(0.5, 0.5);
            }
        }
    }
}
