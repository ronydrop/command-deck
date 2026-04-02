using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

/// <summary>
/// AI Floating Orb — draggable, glass/neon style widget.
/// Handles drag positioning and state-driven animations.
/// </summary>
public partial class AiOrbControl : UserControl
{
    private bool _isDragging;
    private Point _dragStart;
    private double _orbXAtDragStart;
    private double _orbYAtDragStart;
    private bool _wasDragged;

    public AiOrbControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // AddHandler com handledEventsToo:true garante que o drag inicia mesmo quando
        // ButtonBase.OnMouseLeftButtonDown() marca e.Handled = true internamente.
        AddHandler(UIElement.MouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnMouseLeftButtonDownCore),
            handledEventsToo: true);
        AddHandler(UIElement.MouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnMouseLeftButtonUpCore),
            handledEventsToo: true);
    }

    // Desativa o LayoutClip automático do WPF que corta o glow/shadow nos limites
    // do ArrangeOverride (56×56 do UserControl). O DropShadowEffect e a GlowAura
    // (64×64 + BlurRadius=32) precisam de ~128×128px — sem este override aparecem
    // clipados em forma quadrada independentemente de ClipToBounds="False".
    protected override Geometry? GetLayoutClip(Size layoutSlotSize) => null;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AiOrbViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;

        if (e.NewValue is AiOrbViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyProviderGlow(newVm.ActiveProviderColor);
            ApplyState(newVm.State);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not AiOrbViewModel vm) return;

        switch (e.PropertyName)
        {
            case nameof(AiOrbViewModel.State):
                Dispatcher.Invoke(() => ApplyState(vm.State));
                break;
            case nameof(AiOrbViewModel.ActiveProviderColor):
                Dispatcher.Invoke(() => ApplyProviderGlow(vm.ActiveProviderColor));
                break;
        }
    }

    // ─── Drag support ────────────────────────────────────────────────────────

    private void OnMouseLeftButtonDownCore(object sender, MouseButtonEventArgs e)
    {
        // Start tracking from anywhere on the control.
        // Mouse capture is deferred to OnMouseMove after the 5px threshold,
        // so tap (no drag) still fires the toggle-menu logic in OnMouseLeftButtonUpCore.
        _isDragging = true;
        _wasDragged = false;

        // Usar o Canvas pai como referência de coordenadas — mesmo espaço de Canvas.Left/Top.
        var canvas = VisualTreeHelper.GetParent(this) as IInputElement;
        _dragStart = e.GetPosition(canvas);

        // Proteção contra NaN: Canvas.GetLeft retorna NaN se o binding ainda não foi avaliado.
        var leftVal = Canvas.GetLeft(this);
        var topVal  = Canvas.GetTop(this);
        if (DataContext is AiOrbViewModel vm)
        {
            _orbXAtDragStart = double.IsNaN(leftVal) ? vm.PositionX : leftVal;
            _orbYAtDragStart = double.IsNaN(topVal)  ? vm.PositionY : topVal;
        }
        else
        {
            _orbXAtDragStart = double.IsNaN(leftVal) ? 0 : leftVal;
            _orbYAtDragStart = double.IsNaN(topVal)  ? 0 : topVal;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_isDragging) return;
        if (DataContext is AiOrbViewModel { IsPositionLocked: true }) return;

        var canvas  = VisualTreeHelper.GetParent(this) as IInputElement;
        var current = e.GetPosition(canvas);
        double dx = current.X - _dragStart.X;
        double dy = current.Y - _dragStart.Y;

        // Require 5px movement to confirm drag (avoids interfering with taps)
        if (!_wasDragged && Math.Abs(dx) < 5 && Math.Abs(dy) < 5) return;

        if (!_wasDragged)
        {
            _wasDragged = true;
            CaptureMouse(); // Capture mouse so events arrive even outside the control
        }

        var parent = VisualTreeHelper.GetParent(this) as FrameworkElement;
        double maxX = (parent?.ActualWidth ?? 1400) - ActualWidth;
        double maxY = (parent?.ActualHeight ?? 900) - ActualHeight;

        double newX = Math.Clamp(_orbXAtDragStart + dx, 0, maxX);
        double newY = Math.Clamp(_orbYAtDragStart + dy, 0, maxY);

        // TwoWay binding em Canvas.Left/Top propaga para o Canvas automaticamente.
        if (DataContext is AiOrbViewModel vm)
        {
            vm.PositionX = newX;
            vm.PositionY = newY;
        }
    }

    private void OnMouseLeftButtonUpCore(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;

        if (_wasDragged)
        {
            // Foi um drag: liberar captura, fazer snap-to-edge e salvar posição.
            ReleaseMouseCapture();
            if (DataContext is AiOrbViewModel vm)
            {
                SnapToEdge(vm);
                vm.SavePosition();
            }
        }
        else
        {
            // Foi um tap simples: toggle do menu radial.
            if (DataContext is AiOrbViewModel vm)
                vm.ToggleRadialMenuCommand.Execute(null);
        }
    }

    /// <summary>
    /// Se o orb estiver dentro de 48px de uma borda, anima suavemente até 16px da borda.
    /// Evita que o orb fique parcialmente fora da tela.
    /// </summary>
    private void SnapToEdge(AiOrbViewModel vm)
    {
        var parent = VisualTreeHelper.GetParent(this) as FrameworkElement;
        if (parent is null) return;

        const double snapThreshold = 48;
        const double snapMargin    = 16;

        double maxX = parent.ActualWidth  - ActualWidth;
        double maxY = parent.ActualHeight - ActualHeight;

        double targetX = vm.PositionX;
        double targetY = vm.PositionY;

        // Horizontal snap
        if (targetX < snapThreshold)
            targetX = snapMargin;
        else if (targetX > maxX - snapThreshold)
            targetX = maxX - snapMargin;

        // Vertical snap
        if (targetY < snapThreshold)
            targetY = snapMargin;
        else if (targetY > maxY - snapThreshold)
            targetY = maxY - snapMargin;

        if (Math.Abs(targetX - vm.PositionX) < 1 && Math.Abs(targetY - vm.PositionY) < 1)
            return; // Already in place — skip animation

        // Animate via Canvas attached properties.
        // Completed callbacks restore the ViewModel values after the animation finishes,
        // ensuring the TwoWay binding reflects the snapped position.
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var left = new DoubleAnimation { To = targetX, Duration = TimeSpan.FromMilliseconds(280), EasingFunction = ease };
        var top  = new DoubleAnimation { To = targetY, Duration = TimeSpan.FromMilliseconds(280), EasingFunction = ease };
        left.Completed += (_, _) => vm.PositionX = targetX;
        top.Completed  += (_, _) => vm.PositionY = targetY;

        BeginAnimation(Canvas.LeftProperty, left);
        BeginAnimation(Canvas.TopProperty, top);
    }

    // ─── State-driven animations ─────────────────────────────────────────────

    private void ApplyState(OrbState state)
    {
        // Stop running storyboards before switching
        StopSb("PulseAnimation");
        StopSb("SpinAnimation");

        ProcessingRing.Visibility = Visibility.Collapsed;
        RecordingRing.Visibility = Visibility.Collapsed;
        RobotIcon.Visibility = Visibility.Collapsed;
        MicIcon.Visibility = Visibility.Collapsed;
        ProcessingDots.Visibility = Visibility.Collapsed;

        switch (state)
        {
            case OrbState.Idle:
            case OrbState.Hover:
            case OrbState.Active:
                RobotIcon.Visibility = Visibility.Visible;
                break;

            case OrbState.Recording:
                MicIcon.Visibility = Visibility.Visible;
                RecordingRing.Visibility = Visibility.Visible;
                BeginSb("PulseAnimation");
                break;

            case OrbState.Processing:
                ProcessingDots.Visibility = Visibility.Visible;
                ProcessingRing.Visibility = Visibility.Visible;
                BeginSb("SpinAnimation");
                break;
        }
    }

    private void ApplyProviderGlow(string hexColor)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            GlowBrush.Color = color;
            GlowAuraEffect.Color = color;
            OrbGlow.Color = color;
            ProcessingRing.Stroke = new SolidColorBrush(color);
        }
        catch { /* ignore invalid color string */ }
    }

    /// <summary>Starts a named storyboard from this UserControl's Resources (controllable).</summary>
    private void BeginSb(string key)
    {
        if (TryFindResource(key) is Storyboard sb)
            sb.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
    }

    /// <summary>Stops a named controllable storyboard.</summary>
    private void StopSb(string key)
    {
        if (TryFindResource(key) is Storyboard sb)
            sb.Stop(this);
    }
}
