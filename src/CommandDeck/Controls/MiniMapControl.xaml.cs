using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

/// <summary>
/// Code-behind for the mini-map overlay.
///
/// Responsibilities (UI-only):
///   - Notify the ViewModel about the current viewport size so it can compute the viewport rect.
///   - Forward mouse clicks and drags on the map canvas to the ViewModel.
///   - Animate collapse/expand of the panel and chevron rotation.
/// </summary>
public partial class MiniMapControl : UserControl
{
    // ─── Drag state for viewport-rect panning ────────────────────────────────

    private bool   _isDragging;
    private Point  _dragStart;
    private double _dragOffsetAtStartX;
    private double _dragOffsetAtStartY;

    // ─── Collapse animation state ─────────────────────────────────────────────

    private MiniMapViewModel? _vm;

    private static readonly Duration AnimDuration = new(TimeSpan.FromMilliseconds(220));
    private static readonly IEasingFunction AnimEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    // ─── Constructor ─────────────────────────────────────────────────────────

    public MiniMapControl()
    {
        InitializeComponent();

        Loaded             += OnLoaded;
        SizeChanged        += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BindViewModel(DataContext as MiniMapViewModel);
        NotifyViewportSize();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        BindViewModel(e.NewValue as MiniMapViewModel);
    }

    private void BindViewModel(MiniMapViewModel? vm)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = vm;

        if (_vm is null) return;

        _vm.PropertyChanged += OnVmPropertyChanged;

        // Apply initial state without animation
        BeginAnimation(HeightProperty, null);
        Height = _vm.IsCollapsed ? 22 : _vm.MiniMapHeight;
        ChevronRotation.Angle = _vm.IsCollapsed ? 0 : 180;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MiniMapViewModel.IsCollapsed))
            AnimateToggle(_vm!.IsCollapsed);
    }

    /// <summary>Animates height and chevron when the user collapses or expands the panel.</summary>
    private void AnimateToggle(bool collapse)
    {
        if (_vm is null) return;

        double targetHeight = collapse ? 22 : _vm.MiniMapHeight;
        double targetAngle  = collapse ? 0   : 180;

        BeginAnimation(HeightProperty,
            new DoubleAnimation(targetHeight, AnimDuration) { EasingFunction = AnimEase });

        ChevronRotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(targetAngle, AnimDuration) { EasingFunction = AnimEase });
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        NotifyViewportSize();
    }

    // ─── Mouse interactions ───────────────────────────────────────────────────

    private void OnMapMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        var vm = DataContext as MiniMapViewModel;
        if (vm is null) return;

        var pos = e.GetPosition(MapCanvas);

        // If the click is inside the viewport rect, start a drag
        if (IsInsideViewportRect(pos, vm))
        {
            _isDragging         = true;
            _dragStart          = pos;
            _dragOffsetAtStartX = vm.ViewportRectX;
            _dragOffsetAtStartY = vm.ViewportRectY;
            MapCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        // Otherwise treat as a click-to-navigate
        double vpW = GetHostViewportWidth();
        double vpH = GetHostViewportHeight();
        vm.HandleMiniMapClick(pos.X, pos.Y, vpW, vpH);
        e.Handled = true;
    }

    private void OnMapMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var vm = DataContext as MiniMapViewModel;
        if (vm is null) return;

        var pos = e.GetPosition(MapCanvas);
        double dx = pos.X - _dragStart.X;
        double dy = pos.Y - _dragStart.Y;

        // Target centre of viewport rect in mini-map space
        double targetMmCX = _dragOffsetAtStartX + vm.ViewportRectW / 2.0 + dx;
        double targetMmCY = _dragOffsetAtStartY + vm.ViewportRectH / 2.0 + dy;

        double vpW = GetHostViewportWidth();
        double vpH = GetHostViewportHeight();

        vm.HandleMiniMapClick(targetMmCX, targetMmCY, vpW, vpH);
        e.Handled = true;
    }

    private void OnMapMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        MapCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Tells the ViewModel about the current host viewport dimensions.
    /// We walk up the visual tree looking for a Grid named "ViewportArea".
    /// Falls back to stored last-known values inside the ViewModel if not found.
    /// </summary>
    private void NotifyViewportSize()
    {
        var vm = DataContext as MiniMapViewModel;
        if (vm is null) return;

        var (w, h) = FindHostViewportSize();
        if (w > 0 && h > 0)
            vm.UpdateViewport(w, h);
    }

    private double GetHostViewportWidth()
    {
        var (w, _) = FindHostViewportSize();
        return w > 0 ? w : 800;
    }

    private double GetHostViewportHeight()
    {
        var (_, h) = FindHostViewportSize();
        return h > 0 ? h : 600;
    }

    /// <summary>
    /// Walks up the visual tree to find the "ViewportArea" grid used in TerminalCanvasView.
    /// </summary>
    private (double width, double height) FindHostViewportSize()
    {
        System.Windows.DependencyObject? current = this;
        while (current is not null)
        {
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            if (current is System.Windows.FrameworkElement fe && fe.Name == "ViewportArea")
                return (fe.ActualWidth, fe.ActualHeight);
        }
        return (0, 0);
    }

    /// <summary>Returns true when <paramref name="pos"/> falls inside the current viewport rect.</summary>
    private static bool IsInsideViewportRect(Point pos, MiniMapViewModel vm)
    {
        return pos.X >= vm.ViewportRectX
            && pos.X <= vm.ViewportRectX + vm.ViewportRectW
            && pos.Y >= vm.ViewportRectY
            && pos.Y <= vm.ViewportRectY + vm.ViewportRectH;
    }
}
