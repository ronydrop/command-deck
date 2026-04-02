using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CommandDeck.Helpers;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

public partial class AiOrbControl : UserControl
{
    private const double RadialPopupSize = 220;
    private const double PopupViewportMargin = 8;
    private bool _isDragging;
    private Point _dragStart;
    private double _orbXAtDragStart;
    private double _orbYAtDragStart;
    private bool _wasDragged;
    private int _snapCompletionCount;

    public AiOrbControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => UpdateRadialPopupPlacement();
        SizeChanged += (_, _) => UpdateRadialPopupPlacement();

        AddHandler(UIElement.MouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnMouseLeftButtonDownCore),
            handledEventsToo: true);
        AddHandler(UIElement.MouseMoveEvent,
            new MouseEventHandler(OnMouseMoveCore),
            handledEventsToo: true);
        AddHandler(UIElement.MouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnMouseLeftButtonUpCore),
            handledEventsToo: true);

        MouseEnter += OnMouseEnterCore;
        MouseLeave += OnMouseLeaveCore;
    }

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
            case nameof(AiOrbViewModel.PositionX):
            case nameof(AiOrbViewModel.PositionY):
            case nameof(AiOrbViewModel.IsRadialMenuOpen):
                Dispatcher.Invoke(UpdateRadialPopupPlacement);
                break;
        }
    }

    private void OnMouseEnterCore(object sender, MouseEventArgs e)
    {
        if (DataContext is AiOrbViewModel vm)
        {
            vm.IsHovered = true;
            BeginSb("HoverOnAnimation");
        }
    }

    private void OnMouseLeaveCore(object sender, MouseEventArgs e)
    {
        if (DataContext is AiOrbViewModel vm)
        {
            vm.IsHovered = false;
            BeginSb("HoverOffAnimation");
        }
    }

    private void OnMouseLeftButtonDownCore(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _wasDragged = false;

        var canvas = VisualTreeHelper.GetParent(this) as IInputElement;
        _dragStart = e.GetPosition(canvas);

        if (DataContext is AiOrbViewModel vm)
        {
            _orbXAtDragStart = vm.PositionX;
            _orbYAtDragStart = vm.PositionY;
        }
        else
        {
            var leftVal = Canvas.GetLeft(this);
            var topVal = Canvas.GetTop(this);
            _orbXAtDragStart = double.IsNaN(leftVal) ? 0 : leftVal;
            _orbYAtDragStart = double.IsNaN(topVal) ? 0 : topVal;
        }
    }

    private void OnMouseMoveCore(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        if (DataContext is not AiOrbViewModel vm) return;
        if (vm.IsPositionLocked) return;

        var canvas = VisualTreeHelper.GetParent(this) as IInputElement;
        var current = e.GetPosition(canvas);
        double dx = current.X - _dragStart.X;
        double dy = current.Y - _dragStart.Y;

        if (!_wasDragged && Math.Abs(dx) < 5 && Math.Abs(dy) < 5) return;

        if (!_wasDragged)
        {
            _wasDragged = true;
            BeginAnimation(Canvas.LeftProperty, null);
            BeginAnimation(Canvas.TopProperty, null);
            CaptureMouse();
        }

        var parent = VisualTreeHelper.GetParent(this) as FrameworkElement;
        double maxX = (parent?.ActualWidth ?? 1400) - ActualWidth;
        double maxY = (parent?.ActualHeight ?? 900) - ActualHeight;

        vm.PositionX = Math.Clamp(_orbXAtDragStart + dx, 0, maxX);
        vm.PositionY = Math.Clamp(_orbYAtDragStart + dy, 0, maxY);
    }

    private void OnMouseLeftButtonUpCore(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;

        if (_wasDragged)
        {
            ReleaseMouseCapture();
            if (DataContext is AiOrbViewModel vm)
                SnapToEdge(vm);
        }
        else
        {
            if (DataContext is AiOrbViewModel vm)
                vm.ToggleRadialMenuCommand.Execute(null);
        }
    }

    private void SnapToEdge(AiOrbViewModel vm)
    {
        var parent = VisualTreeHelper.GetParent(this) as FrameworkElement;
        if (parent is null)
        {
            vm.SavePosition();
            return;
        }

        double maxX = parent.ActualWidth - ActualWidth;
        double maxY = parent.ActualHeight - ActualHeight;

        double targetX = vm.PositionX;
        double targetY = vm.PositionY;

        if (targetX < OrbAnimationConstants.SnapThreshold)
            targetX = OrbAnimationConstants.SnapMargin;
        else if (targetX > maxX - OrbAnimationConstants.SnapThreshold)
            targetX = maxX - OrbAnimationConstants.SnapMargin;

        if (targetY < OrbAnimationConstants.SnapThreshold)
            targetY = OrbAnimationConstants.SnapMargin;
        else if (targetY > maxY - OrbAnimationConstants.SnapThreshold)
            targetY = maxY - OrbAnimationConstants.SnapMargin;

        if (Math.Abs(targetX - vm.PositionX) < 1 && Math.Abs(targetY - vm.PositionY) < 1)
        {
            vm.SavePosition();
            return;
        }

        _snapCompletionCount = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var left = new DoubleAnimation { To = targetX, Duration = TimeSpan.FromMilliseconds(OrbAnimationConstants.SnapDurationMs), EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        var top = new DoubleAnimation { To = targetY, Duration = TimeSpan.FromMilliseconds(OrbAnimationConstants.SnapDurationMs), EasingFunction = ease, FillBehavior = FillBehavior.Stop };

        left.Completed += OnSnapAnimationCompleted;
        top.Completed += OnSnapAnimationCompleted;

        vm.PositionX = targetX;
        vm.PositionY = targetY;

        BeginAnimation(Canvas.LeftProperty, left);
        BeginAnimation(Canvas.TopProperty, top);
    }

    private void OnSnapAnimationCompleted(object? sender, EventArgs e)
    {
        if (Interlocked.Increment(ref _snapCompletionCount) == 2)
        {
            if (DataContext is AiOrbViewModel vm)
                vm.SavePosition();
            Interlocked.Exchange(ref _snapCompletionCount, 0);
        }
    }

    private void ApplyState(OrbState state)
    {
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
            case OrbState.MenuOpen:
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
        catch { }
    }

    private void UpdateRadialPopupPlacement()
    {
        if (DataContext is not AiOrbViewModel vm)
            return;

        var parent = VisualTreeHelper.GetParent(this) as FrameworkElement;
        if (parent is null || parent.ActualWidth <= 0 || parent.ActualHeight <= 0)
            return;

        double desiredLeft = vm.PositionX + ((ActualWidth - RadialPopupSize) / 2);
        double desiredTop = vm.PositionY + ((ActualHeight - RadialPopupSize) / 2);

        double clampedLeft = Math.Clamp(desiredLeft, PopupViewportMargin, Math.Max(PopupViewportMargin, parent.ActualWidth - RadialPopupSize - PopupViewportMargin));
        double clampedTop = Math.Clamp(desiredTop, PopupViewportMargin, Math.Max(PopupViewportMargin, parent.ActualHeight - RadialPopupSize - PopupViewportMargin));

        RadialPopup.HorizontalOffset = clampedLeft - vm.PositionX;
        RadialPopup.VerticalOffset = clampedTop - vm.PositionY;
    }

    private void BeginSb(string key)
    {
        if (TryFindResource(key) is Storyboard sb)
            sb.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
    }

    private void StopSb(string key)
    {
        if (TryFindResource(key) is Storyboard sb)
            sb.Stop(this);
    }
}