using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CommandDeck.Models;
using CommandDeck.Services;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

/// <summary>
/// Code-behind for the spatial canvas card.
/// Handles per-card drag (titlebar) and resize (bottom-right Thumb).
/// Close and focus-mode triggers bubble up as routed events.
/// </summary>
public partial class CanvasCardControl : UserControl
{
    // ─── Routed events (bubble up to TerminalCanvasView) ────────────────────

    public static readonly RoutedEvent CardCloseRequestedEvent =
        EventManager.RegisterRoutedEvent(nameof(CardCloseRequested),
            RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CanvasCardControl));

    public event RoutedEventHandler CardCloseRequested
    {
        add => AddHandler(CardCloseRequestedEvent, value);
        remove => RemoveHandler(CardCloseRequestedEvent, value);
    }

    public static readonly RoutedEvent CardFocusRequestedEvent =
        EventManager.RegisterRoutedEvent(nameof(CardFocusRequested),
            RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CanvasCardControl));

    public event RoutedEventHandler CardFocusRequested
    {
        add => AddHandler(CardFocusRequestedEvent, value);
        remove => RemoveHandler(CardFocusRequestedEvent, value);
    }

    public static readonly RoutedEvent CardActivatedEvent =
        EventManager.RegisterRoutedEvent(nameof(CardActivated),
            RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CanvasCardControl));

    public event RoutedEventHandler CardActivated
    {
        add => AddHandler(CardActivatedEvent, value);
        remove => RemoveHandler(CardActivatedEvent, value);
    }

    // ─── AI action routed event ─────────────────────────────────────────────

    public static readonly RoutedEvent AiActionRequestedEvent =
        EventManager.RegisterRoutedEvent(nameof(AiActionRequested),
            RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CanvasCardControl));

    public event RoutedEventHandler AiActionRequested
    {
        add => AddHandler(AiActionRequestedEvent, value);
        remove => RemoveHandler(AiActionRequestedEvent, value);
    }

    // ─── Drag state ──────────────────────────────────────────────────────────

    private bool _isDragging;
    private Point _dragStart;
    private double _itemXAtDragStart;
    private double _itemYAtDragStart;

    // Cached canvas view reference — avoids walking visual tree on every MouseMove
    private Views.TerminalCanvasView? _cachedCanvasView;

    public CanvasCardControl()
    {
        InitializeComponent();

        // Activate card when clicked anywhere on it — including when a child already
        // set e.Handled = true (e.g., TitleBar drag, TerminalControl.OnMouseDown).
        AddHandler(UIElement.MouseDownEvent,
            (MouseButtonEventHandler)((_, _) => RaiseEvent(new RoutedEventArgs(CardActivatedEvent, this))),
            handledEventsToo: true);

        Loaded += (_, _) => CacheCanvasView();

        // Re-apply the rounded clip when IsTiledMode toggles (corners go 8→0 or 0→8)
        DataContextChanged += OnDataContextChanged;
    }

    private CanvasItemViewModel? _trackedVm;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_trackedVm is not null)
            _trackedVm.PropertyChanged -= OnVmPropertyChanged;

        _trackedVm = e.NewValue as CanvasItemViewModel;

        if (_trackedVm is not null)
            _trackedVm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CanvasItemViewModel.IsTiledMode))
            RefreshContentGridClip();
    }

    private void CacheCanvasView()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            if (current is Views.TerminalCanvasView canvasView)
            {
                _cachedCanvasView = canvasView;
                return;
            }
        }
    }

    // ─── Titlebar drag ───────────────────────────────────────────────────────

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // If the click originated from a Button (e.g. CloseButton), let it handle its own click
        if (e.OriginalSource is DependencyObject src && IsDescendantOfButton(src)) return;

        // Double-click → request focus mode
        if (e.ClickCount == 2)
        {
            RaiseEvent(new RoutedEventArgs(CardFocusRequestedEvent, this));
            e.Handled = true;
            return;
        }

        if (DataContext is not CanvasItemViewModel vm) return;

        // No drag in tiled mode
        if (IsTiledMode()) return;

        _isDragging = true;
        _dragStart = e.GetPosition(null); // screen coords
        _itemXAtDragStart = vm.X;
        _itemYAtDragStart = vm.Y;

        TitleBar.CaptureMouse();
        e.Handled = true;
    }

    // Grid snap size in world units (Shift+drag)
    private const double SnapGrid = 80;

    private void OnTitleBarMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || DataContext is not CanvasItemViewModel vm) return;

        var current = e.GetPosition(null);
        double dx = current.X - _dragStart.X;
        double dy = current.Y - _dragStart.Y;

        double zoom = GetCanvasZoom();

        double rawX = _itemXAtDragStart + dx / zoom;
        double rawY = _itemYAtDragStart + dy / zoom;

        // Shift held → snap to nearest grid multiple
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            rawX = Math.Round(rawX / SnapGrid) * SnapGrid;
            rawY = Math.Round(rawY / SnapGrid) * SnapGrid;
        }

        vm.X = rawX;
        vm.Y = rawY;
    }

    private void OnTitleBarMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        TitleBar.ReleaseMouseCapture();

        // Return keyboard focus to the terminal so arrow keys work immediately after drag.
        var terminal = FindVisualChild<CommandDeck.Controls.TerminalControl>(this);
        terminal?.FocusInput();

        e.Handled = true;
    }

    private static T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var found = FindVisualChild<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    // ─── Resize ──────────────────────────────────────────────────────────────

    private void OnResizeDragStarted(object sender, DragStartedEventArgs e)
    {
        SizeIndicator.Visibility = Visibility.Visible;
    }

    private void OnResizeDragCompleted(object sender, DragCompletedEventArgs e)
    {
        SizeIndicator.Visibility = Visibility.Collapsed;
    }

    private void UpdateSizeIndicator(double width, double height)
    {
        SizeIndicatorText.Text = $"{(int)width} × {(int)height}";
    }

    private void OnResizeDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is not CanvasItemViewModel vm) return;
        if (IsTiledMode()) return;

        double zoom = GetCanvasZoom();
        vm.Width = Math.Clamp(vm.Width + e.HorizontalChange / zoom, 320, SystemParameters.PrimaryScreenWidth);
        vm.Height = Math.Clamp(vm.Height + e.VerticalChange / zoom, 220, SystemParameters.PrimaryScreenHeight);
        UpdateSizeIndicator(vm.Width, vm.Height);
        e.Handled = true;
    }

    private void OnResizeRightDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is not CanvasItemViewModel vm) return;
        if (IsTiledMode()) return;

        double zoom = GetCanvasZoom();
        vm.Width = Math.Clamp(vm.Width + e.HorizontalChange / zoom, 320, SystemParameters.PrimaryScreenWidth);
        UpdateSizeIndicator(vm.Width, vm.Height);
        e.Handled = true;
    }

    private void OnResizeBottomDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is not CanvasItemViewModel vm) return;
        if (IsTiledMode()) return;

        double zoom = GetCanvasZoom();
        vm.Height = Math.Clamp(vm.Height + e.VerticalChange / zoom, 220, SystemParameters.PrimaryScreenHeight);
        UpdateSizeIndicator(vm.Width, vm.Height);
        e.Handled = true;
    }

    // ─── Close ───────────────────────────────────────────────────────────────

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(CardCloseRequestedEvent, this));
        e.Handled = true;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static bool IsDescendantOfButton(DependencyObject element)
    {
        var current = element;
        while (current is not null)
        {
            if (current is System.Windows.Controls.Button) return true;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    /// <summary>
    /// Returns the current canvas zoom level using the cached TerminalCanvasView reference.
    /// Falls back to walking the visual tree if the cache is stale.
    /// </summary>
    private double GetCanvasZoom()
    {
        if (_cachedCanvasView is not null)
            return _cachedCanvasView.CurrentZoom;

        // Cache miss — walk tree once and cache for future calls
        CacheCanvasView();
        return _cachedCanvasView?.CurrentZoom ?? 1.0;
    }

    /// <summary>
    /// Checks whether the canvas is currently in tiled layout mode.
    /// Walks the visual tree to find the MainViewModel.
    /// </summary>
    private bool IsTiledMode()
    {
        var mainVm = (Window.GetWindow(this)?.DataContext as ViewModels.MainViewModel);
        return mainVm?.CanvasViewModel?.IsTiledMode == true;
    }

    // ─── Content grid rounded clip ───────────────────────────────────────────

    /// <summary>
    /// Dynamically clips the inner content grid to the card's rounded corners.
    /// A static RectangleGeometry can't work because the rounded clip corners must
    /// be at the actual card edges — not at a hardcoded large coordinate.
    /// Called on SizeChanged so the clip always matches the real card size.
    /// </summary>
    private void OnContentGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var grid = (Grid)sender;
        var radius = (_trackedVm?.IsTiledMode == true) ? 0.0 : 6.0;
        grid.Clip = new RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
            radius, radius);
    }

    /// <summary>
    /// Re-applies the clip with the correct radius when tiled mode toggles,
    /// even if the card size hasn't changed.
    /// </summary>
    internal void RefreshContentGridClip()
    {
        if (ContentGrid.ActualWidth <= 0) return;
        var radius = (_trackedVm?.IsTiledMode == true) ? 0.0 : 6.0;
        ContentGrid.Clip = new RectangleGeometry(
            new Rect(0, 0, ContentGrid.ActualWidth, ContentGrid.ActualHeight),
            radius, radius);
    }

    // ─── AI context menu handlers ─────────────────────────────────────────

    private void RaiseAiAction(AiCardAction action, string? model = null)
    {
        RaiseEvent(new AiActionEventArgs(AiActionRequestedEvent, this)
        {
            Action = action,
            ModelOrAlias = model
        });
    }

    private void OnAiFixError(object s, RoutedEventArgs e) => RaiseAiAction(AiCardAction.FixError);
    private void OnAiExplainOutput(object s, RoutedEventArgs e) => RaiseAiAction(AiCardAction.ExplainOutput);
    private void OnAiSuggestCommand(object s, RoutedEventArgs e) => RaiseAiAction(AiCardAction.SuggestCommand);
    private void OnAiSendContext(object s, RoutedEventArgs e) => RaiseAiAction(AiCardAction.SendContext);
    private void OnAiOpenSonnet(object s, RoutedEventArgs e) => RaiseAiAction(AiCardAction.LaunchModel, "sonnet");
    private void OnAiOpenOpus(object s, RoutedEventArgs e) => RaiseAiAction(AiCardAction.LaunchModel, "opus");
    private void OnAiOpenHaiku(object s, RoutedEventArgs e) => RaiseAiAction(AiCardAction.LaunchModel, "haiku");
    private void OnAiOpenAgent(object s, RoutedEventArgs e) => RaiseAiAction(AiCardAction.LaunchModel, "agent");
    private void OnAiRunAgain(object s, RoutedEventArgs e) => RaiseAiAction(AiCardAction.RunAgain);
    private void OnAiFixAgain(object s, RoutedEventArgs e) => RaiseAiAction(AiCardAction.FixAgain);
    private void OnAiExplainMore(object s, RoutedEventArgs e) => RaiseAiAction(AiCardAction.ExplainMore);
}

// ─── AI action event args ───────────────────────────────────────────────

public enum AiCardAction
{
    FixError,
    ExplainOutput,
    SuggestCommand,
    SendContext,
    LaunchModel,
    RunAgain,
    FixAgain,
    ExplainMore
}

public class AiActionEventArgs : RoutedEventArgs
{
    public AiCardAction Action { get; init; }
    public string? ModelOrAlias { get; init; }

    public AiActionEventArgs(RoutedEvent routedEvent, object source)
        : base(routedEvent, source) { }
}
