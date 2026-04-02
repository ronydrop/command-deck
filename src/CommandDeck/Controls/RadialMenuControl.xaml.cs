using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

/// <summary>
/// Radial menu displayed around the AI Orb when activated.
/// Positions 5 action buttons at computed orbital positions and
/// plays a stagger open/close animation when it becomes visible/hidden.
/// </summary>
public partial class RadialMenuControl : UserControl
{
    // Open stagger order: Agent → Voice → Improve → Run → Copy
    private static readonly string[] _staggerOpen =
    [
        "BtnChooseAgent",
        "BtnVoice",
        "BtnImprovePrompt",
        "BtnRunSuggestion",
        "BtnCopyContext"
    ];

    // Close stagger order: reverso (Copy → Run → Improve → Voice → Agent)
    private static readonly string[] _staggerClose =
    [
        "BtnCopyContext",
        "BtnRunSuggestion",
        "BtnImprovePrompt",
        "BtnVoice",
        "BtnChooseAgent"
    ];

    private const double StaggerDelayMs  = 30;
    private const double OpenOpDuration  = 150;
    private const double OpenScDuration  = 220;
    private const double CloseOpDuration = 110;
    private const double CloseScDuration = 140;

    public RadialMenuControl()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
        DataContextChanged += OnDataContextChanged;
    }

    // ─── DataContext / ViewModel observation ─────────────────────────────────

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

    // ─── Visibility-driven open ───────────────────────────────────────────────

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
            PlayStaggerOpen();
        // Close is driven by IsRadialMenuClosing; reset handles instant-close edge cases.
        else
            ResetButtons();
    }

    // ─── Open animation ───────────────────────────────────────────────────────

    private void PlayStaggerOpen()
    {
        ResetButtons();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        for (int i = 0; i < _staggerOpen.Length; i++)
        {
            if (FindName(_staggerOpen[i]) is not FrameworkElement btn) continue;
            double beginTime = i * StaggerDelayMs;

            var opAnim = new DoubleAnimation
            {
                From = 0, To = 1,
                Duration = TimeSpan.FromMilliseconds(OpenOpDuration),
                BeginTime = TimeSpan.FromMilliseconds(beginTime),
                EasingFunction = ease
            };
            var sxAnim = new DoubleAnimation
            {
                From = 0.5, To = 1.0,
                Duration = TimeSpan.FromMilliseconds(OpenScDuration),
                BeginTime = TimeSpan.FromMilliseconds(beginTime),
                EasingFunction = ease
            };
            var syAnim = new DoubleAnimation
            {
                From = 0.5, To = 1.0,
                Duration = TimeSpan.FromMilliseconds(OpenScDuration),
                BeginTime = TimeSpan.FromMilliseconds(beginTime),
                EasingFunction = ease
            };

            EnsureScaleTransform(btn);
            btn.BeginAnimation(OpacityProperty, opAnim);
            ((ScaleTransform)btn.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, sxAnim);
            ((ScaleTransform)btn.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, syAnim);
        }
    }

    // ─── Close animation ──────────────────────────────────────────────────────

    private void PlayStaggerClose()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        // Identificar o último botão existente para anexar o callback de finalização
        int lastFoundIndex = -1;
        for (int i = _staggerClose.Length - 1; i >= 0; i--)
        {
            if (FindName(_staggerClose[i]) is FrameworkElement)
            {
                lastFoundIndex = i;
                break;
            }
        }

        if (lastFoundIndex < 0)
        {
            // Nenhum botão encontrado — fechar imediatamente
            if (DataContext is AiOrbViewModel vm)
                vm.CloseRadialMenuCommand.Execute(null);
            return;
        }

        for (int i = 0; i < _staggerClose.Length; i++)
        {
            if (FindName(_staggerClose[i]) is not FrameworkElement btn) continue;
            double beginTime = i * StaggerDelayMs;

            var opAnim = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(CloseOpDuration),
                BeginTime = TimeSpan.FromMilliseconds(beginTime),
                EasingFunction = ease
            };
            var sxAnim = new DoubleAnimation
            {
                To = 0.5,
                Duration = TimeSpan.FromMilliseconds(CloseScDuration),
                BeginTime = TimeSpan.FromMilliseconds(beginTime),
                EasingFunction = ease
            };
            var syAnim = new DoubleAnimation
            {
                To = 0.5,
                Duration = TimeSpan.FromMilliseconds(CloseScDuration),
                BeginTime = TimeSpan.FromMilliseconds(beginTime),
                EasingFunction = ease
            };

            // Último botão encontrado — ao terminar dispara CloseRadialMenuCommand
            if (i == lastFoundIndex)
            {
                opAnim.Completed += (_, _) =>
                {
                    if (DataContext is AiOrbViewModel vm)
                        vm.CloseRadialMenuCommand.Execute(null);
                };
            }

            EnsureScaleTransform(btn);
            btn.BeginAnimation(OpacityProperty, opAnim);
            ((ScaleTransform)btn.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, sxAnim);
            ((ScaleTransform)btn.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, syAnim);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

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

    private static void EnsureScaleTransform(FrameworkElement btn)
    {
        if (btn.RenderTransform is not ScaleTransform)
        {
            btn.RenderTransformOrigin = new Point(0.5, 0.5);
            btn.RenderTransform = new ScaleTransform(0.5, 0.5);
        }
    }
}
