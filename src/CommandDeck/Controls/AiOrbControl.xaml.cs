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
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AiOrbViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;

        if (e.NewValue is AiOrbViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyProviderGlow(newVm.ActiveProviderColor);
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

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        // If the click originated inside the OrbButton (or any descendant), let the button handle it
        if (IsInsideOrbButton(e.OriginalSource as DependencyObject)) return;

        _isDragging = true;
        _wasDragged = false;
        _dragStart = e.GetPosition(null);
        _orbXAtDragStart = Canvas.GetLeft(this);
        _orbYAtDragStart = Canvas.GetTop(this);
        CaptureMouse();
        e.Handled = true;
    }

    /// <summary>Returns true if the element is OrbButton or any of its visual descendants.</summary>
    private bool IsInsideOrbButton(DependencyObject? element)
    {
        var current = element;
        while (current != null)
        {
            if (current == OrbButton) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_isDragging) return;

        var current = e.GetPosition(null);
        double dx = current.X - _dragStart.X;
        double dy = current.Y - _dragStart.Y;

        // Require 5px movement to start drag (to not interfere with click)
        if (!_wasDragged && Math.Abs(dx) < 5 && Math.Abs(dy) < 5) return;
        _wasDragged = true;

        var parent = VisualTreeHelper.GetParent(this) as FrameworkElement;
        double maxX = (parent?.ActualWidth ?? 1400) - ActualWidth;
        double maxY = (parent?.ActualHeight ?? 900) - ActualHeight;

        double newX = Math.Clamp(_orbXAtDragStart + dx, 0, maxX);
        double newY = Math.Clamp(_orbYAtDragStart + dy, 0, maxY);

        Canvas.SetLeft(this, newX);
        Canvas.SetTop(this, newY);

        if (DataContext is AiOrbViewModel vm)
        {
            vm.PositionX = newX;
            vm.PositionY = newY;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();

        if (_wasDragged && DataContext is AiOrbViewModel vm)
            vm.SavePosition();
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
