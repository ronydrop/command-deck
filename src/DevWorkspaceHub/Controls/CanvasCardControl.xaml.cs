using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.ViewModels;

namespace DevWorkspaceHub.Controls;

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

    public CanvasCardControl()
    {
        InitializeComponent();

        // Activate card when clicked anywhere on it
        MouseDown += (_, _) => RaiseEvent(new RoutedEventArgs(CardActivatedEvent, this));
    }

    // ─── Titlebar drag ───────────────────────────────────────────────────────

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // Double-click → request focus mode
        if (e.ClickCount == 2)
        {
            RaiseEvent(new RoutedEventArgs(CardFocusRequestedEvent, this));
            e.Handled = true;
            return;
        }

        if (DataContext is not CanvasItemViewModel vm) return;

        _isDragging = true;
        _dragStart = e.GetPosition(null); // screen coords
        _itemXAtDragStart = vm.X;
        _itemYAtDragStart = vm.Y;

        // B. Drag lift: scale card up to 1.03 in 120ms
        AnimateDragScale(1.0, 1.03, TimeSpan.FromMilliseconds(120));

        TitleBar.CaptureMouse();
        e.Handled = true;
    }

    // Grid snap size in world units (Shift+drag)
    private const double SnapGrid = 20;

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

        // B. Drag release: animate scale back to 1.0 in 150ms
        AnimateDragScale(1.03, 1.0, TimeSpan.FromMilliseconds(150));

        // Return keyboard focus to the terminal so arrow keys work immediately after drag.
        var terminal = FindVisualChild<DevWorkspaceHub.Controls.TerminalControl>(this);
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

    private void OnResizeDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is not CanvasItemViewModel vm) return;

        double zoom = GetCanvasZoom();
        vm.Width = Math.Max(320, vm.Width + e.HorizontalChange / zoom);
        vm.Height = Math.Max(220, vm.Height + e.VerticalChange / zoom);
        e.Handled = true;
    }

    // ─── Close ───────────────────────────────────────────────────────────────

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(CardCloseRequestedEvent, this));
        e.Handled = true;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// B. Animates DragScale ScaleX/Y from <paramref name="from"/> to <paramref name="to"/>
    /// over the given <paramref name="duration"/>.
    /// Uses FillBehavior=Stop and sets the final value in Completed so that
    /// subsequent animations are not blocked by a held animation clock.
    /// </summary>
    private void AnimateDragScale(double from, double to, TimeSpan duration)
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var anim = new DoubleAnimation(from, to, new Duration(duration))
        {
            EasingFunction = ease,
            FillBehavior   = FillBehavior.Stop
        };
        double finalValue = to;
        anim.Completed += (_, _) =>
        {
            DragScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            DragScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
            DragScale.ScaleX = finalValue;
            DragScale.ScaleY = finalValue;
        };
        DragScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, anim);
        DragScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(from, to, new Duration(duration))
            {
                EasingFunction = ease,
                FillBehavior   = FillBehavior.Stop
            });
    }

    /// <summary>
    /// Walks up the visual tree to find the TerminalCanvasView and reads the current zoom.
    /// Falls back to 1.0 if not found.
    /// </summary>
    private double GetCanvasZoom()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            if (current is Views.TerminalCanvasView canvasView)
                return canvasView.CurrentZoom;
        }
        return 1.0;
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
    private void OnAiRetryWithOpus(object s, RoutedEventArgs e) => RaiseAiAction(AiCardAction.RetryWithModel, "opus");
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
    ExplainMore,
    RetryWithModel
}

public class AiActionEventArgs : RoutedEventArgs
{
    public AiCardAction Action { get; init; }
    public string? ModelOrAlias { get; init; }

    public AiActionEventArgs(RoutedEvent routedEvent, object source)
        : base(routedEvent, source) { }
}
