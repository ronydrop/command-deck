using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CommandDeck.Helpers;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

public partial class RadialMenuControl : UserControl
{
    private static readonly string[] _staggerOpen =
    [
        "BtnVoice",
        "BtnImprovePrompt",
        "BtnRunSuggestion",
        "BtnCopyContext"
    ];

    private static readonly string[] _staggerClose =
    [
        "BtnCopyContext",
        "BtnRunSuggestion",
        "BtnImprovePrompt",
        "BtnVoice"
    ];

    public RadialMenuControl()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        IsVisibleChanged -= OnIsVisibleChanged;
        DataContextChanged -= OnDataContextChanged;
        Unloaded -= OnUnloaded;
        if (DataContext is AiOrbViewModel vm)
            vm.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AiOrbViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;

        if (e.NewValue is AiOrbViewModel newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiOrbViewModel.IsRadialMenuClosing)
            && sender is AiOrbViewModel vm
            && vm.IsRadialMenuClosing)
        {
            Dispatcher.Invoke(PlayStaggerClose);
        }
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
        ResetButtons();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        for (int i = 0; i < _staggerOpen.Length; i++)
        {
            if (FindName(_staggerOpen[i]) is not FrameworkElement btn) continue;
            var (opAnim, sxAnim, syAnim) = CreateButtonAnims(i, isOpening: true, ease);
            ApplyButtonAnims(btn, opAnim, sxAnim, syAnim);
        }
    }

    private void PlayStaggerClose()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        int lastFoundIndex = -1;
        for (int i = _staggerClose.Length - 1; i >= 0; i--)
        {
            if (FindName(_staggerClose[i]) is FrameworkElement) { lastFoundIndex = i; break; }
        }

        if (lastFoundIndex < 0)
        {
            if (DataContext is AiOrbViewModel vm)
                vm.CloseRadialMenuCommand.Execute(null);
            return;
        }

        for (int i = 0; i < _staggerClose.Length; i++)
        {
            if (FindName(_staggerClose[i]) is not FrameworkElement btn) continue;
            var (opAnim, sxAnim, syAnim) = CreateButtonAnims(i, isOpening: false, ease);

            if (i == lastFoundIndex)
                opAnim.Completed += (_, _) =>
                {
                    if (DataContext is AiOrbViewModel vm)
                        vm.CloseRadialMenuCommand.Execute(null);
                };

            ApplyButtonAnims(btn, opAnim, sxAnim, syAnim);
        }
    }

    private static (DoubleAnimation op, DoubleAnimation sx, DoubleAnimation sy) CreateButtonAnims(
        int index, bool isOpening, IEasingFunction ease)
    {
        double beginMs = index * OrbAnimationConstants.StaggerDelayMs;
        var begin = TimeSpan.FromMilliseconds(beginMs);

        var op = new DoubleAnimation
        {
            From = isOpening ? 0 : null,
            To   = isOpening ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(
                isOpening ? OrbAnimationConstants.OpenOpacityDurationMs : OrbAnimationConstants.CloseOpacityDurationMs),
            BeginTime = begin,
            EasingFunction = ease
        };
        var sx = new DoubleAnimation
        {
            From = isOpening ? 0.5 : null,
            To   = isOpening ? 1.0 : 0.5,
            Duration = TimeSpan.FromMilliseconds(
                isOpening ? OrbAnimationConstants.OpenScaleDurationMs : OrbAnimationConstants.CloseScaleDurationMs),
            BeginTime = begin,
            EasingFunction = ease
        };
        var sy = new DoubleAnimation
        {
            From = isOpening ? 0.5 : null,
            To   = isOpening ? 1.0 : 0.5,
            Duration = sx.Duration,
            BeginTime = begin,
            EasingFunction = ease
        };
        return (op, sx, sy);
    }

    private static void ApplyButtonAnims(FrameworkElement btn,
        DoubleAnimation opAnim, DoubleAnimation sxAnim, DoubleAnimation syAnim)
    {
        EnsureScaleTransform(btn);
        btn.BeginAnimation(OpacityProperty, opAnim);
        ((ScaleTransform)btn.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, sxAnim);
        ((ScaleTransform)btn.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, syAnim);
    }

    private void ResetButtons()
    {
        foreach (var name in _staggerOpen)
        {
            if (FindName(name) is not FrameworkElement btn) continue;

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

    private static (DoubleAnimation op, DoubleAnimation sx, DoubleAnimation sy) CreateButtonAnims(
        int index, bool isOpening, IEasingFunction ease)
    {
        var begin = TimeSpan.FromMilliseconds(index * OrbAnimationConstants.StaggerDelayMs);
        var op = new DoubleAnimation
        {
            From = isOpening ? 0 : (double?)null,
            To   = isOpening ? 1 : 0,
            Duration  = TimeSpan.FromMilliseconds(isOpening ? OrbAnimationConstants.OpenOpacityDurationMs : OrbAnimationConstants.CloseOpacityDurationMs),
            BeginTime = begin,
            EasingFunction = ease
        };
        var sx = new DoubleAnimation
        {
            From = isOpening ? 0.5 : (double?)null,
            To   = isOpening ? 1.0 : 0.5,
            Duration  = TimeSpan.FromMilliseconds(isOpening ? OrbAnimationConstants.OpenScaleDurationMs : OrbAnimationConstants.CloseScaleDurationMs),
            BeginTime = begin,
            EasingFunction = ease
        };
        var sy = new DoubleAnimation { From = sx.From, To = sx.To, Duration = sx.Duration, BeginTime = begin, EasingFunction = ease };
        return (op, sx, sy);
    }

    private static void ApplyButtonAnims(FrameworkElement btn,
        DoubleAnimation opAnim, DoubleAnimation sxAnim, DoubleAnimation syAnim)
    {
        EnsureScaleTransform(btn);
        btn.BeginAnimation(OpacityProperty, opAnim);
        ((ScaleTransform)btn.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, sxAnim);
        ((ScaleTransform)btn.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, syAnim);
    }

    private static void EnsureScaleTransform(FrameworkElement btn)
    {
        if (btn.RenderTransform is not ScaleTransform)
        {
            btn.RenderTransformOrigin = new Point(0.5, 0.5);
            btn.RenderTransform = new ScaleTransform(0.5, 0.5);
        }
    }
}