using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DevWorkspaceHub.Controls;
using DevWorkspaceHub.ViewModels;

namespace DevWorkspaceHub.Views;

/// <summary>
/// Spatial infinite canvas for terminals and widgets.
///
/// Responsibilities (UI-only, not in ViewModel):
///   - Drag-to-pan canvas (middle mouse OR plain left-drag on empty space)
///   - Ctrl+Scroll zoom centred on cursor
///   - Animated focus mode (double-click card) and exit (ESC / Ver Todos)
///   - Routing card events (close, focus, activate) to TerminalCanvasViewModel
/// </summary>
public partial class TerminalCanvasView : UserControl
{
    // ─── Constants ───────────────────────────────────────────────────────────

    private const double MinZoom  = 0.25;
    private const double MaxZoom  = 2.0;
    private const double ZoomStep = 0.12;

    // ─── Camera drag state ────────────────────────────────────────────────

    private bool   _isPanning;
    private Point  _panStart;
    private double _panOriginX;
    private double _panOriginY;

    // ─── Momentum / inertia state ─────────────────────────────────────────

    private Point           _lastPanPos;
    private DateTime        _lastPanTime;
    private double          _momentumVelX;
    private double          _momentumVelY;
    private DispatcherTimer? _momentumTimer;

    // ─── Pre-focus snapshot (for animated return) ─────────────────────────

    private double _preFocusScale;
    private double _preFocusTransX;
    private double _preFocusTransY;

    // ─── ViewModels ──────────────────────────────────────────────────────

    private MainViewModel?           _mainVm;
    private TerminalCanvasViewModel? _canvasVm;

    /// <summary>Current zoom level — exposed for CanvasCardControl drag scaling.</summary>
    public double CurrentZoom => CanvasScale.ScaleX;

    // ─── Constructor ─────────────────────────────────────────────────────

    public TerminalCanvasView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ResolveViewModels();

        if (_canvasVm is not null)
        {
            _canvasVm.FocusItemRequested  += OnFocusItemRequested;
            _canvasVm.FitAllRequested     += OnFitAllRequested;
            _canvasVm.ExitFocusModeRequested += OnExitFocusModeRequested;
        }

        var window = Window.GetWindow(this);
        if (window is not null)
            window.KeyDown += OnWindowKeyDown;

        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is not null)
            window.KeyDown -= OnWindowKeyDown;

        if (_canvasVm is not null)
        {
            _canvasVm.FocusItemRequested -= OnFocusItemRequested;
            _canvasVm.FitAllRequested -= OnFitAllRequested;
            _canvasVm.ExitFocusModeRequested -= OnExitFocusModeRequested;
        }

        _momentumTimer?.Stop();
        _momentumTimer = null;

        Unloaded -= OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        ResolveViewModels();
    }

    private void ResolveViewModels()
    {
        _mainVm = (DataContext as MainViewModel) ??
                  (Window.GetWindow(this)?.DataContext as MainViewModel);

        _canvasVm = _mainVm?.CanvasViewModel;
    }

    // ─── Keyboard (global) ────────────────────────────────────────────────

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _canvasVm?.IsFocusMode == true)
        {
            ExitFocusMode(animated: true);
            e.Handled = true;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Handled at window level
    }

    // ─── Canvas pan (mouse down on empty background) ──────────────────────

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Pan with middle mouse OR left button on empty canvas background
        bool isMiddle = e.ChangedButton == MouseButton.Middle;
        bool isLeft   = e.ChangedButton == MouseButton.Left && IsCanvasBackground(e.Source);

        if (!isMiddle && !isLeft) return;

        _isPanning   = true;
        _panStart    = e.GetPosition(ViewportArea);
        _panOriginX  = CanvasTranslate.X;
        _panOriginY  = CanvasTranslate.Y;

        _momentumTimer?.Stop();
        _momentumVelX = 0;
        _momentumVelY = 0;
        _lastPanPos   = _panStart;
        _lastPanTime  = DateTime.UtcNow;

        Mouse.Capture(ViewportArea);
        ViewportArea.Cursor = Cursors.SizeAll;
        e.Handled = false;
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;

        var pos = e.GetPosition(ViewportArea);
        CanvasTranslate.X = _panOriginX + pos.X - _panStart.X;
        CanvasTranslate.Y = _panOriginY + pos.Y - _panStart.Y;

        var now = DateTime.UtcNow;
        double dt = (now - _lastPanTime).TotalSeconds;
        if (dt > 0 && dt < 0.1)
        {
            _momentumVelX = (pos.X - _lastPanPos.X) / dt;
            _momentumVelY = (pos.Y - _lastPanPos.Y) / dt;
        }
        _lastPanPos  = pos;
        _lastPanTime = now;
        _canvasVm?.SyncCamera(CanvasTranslate.X, CanvasTranslate.Y, CanvasScale.ScaleX);
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        Mouse.Capture(null);
        ViewportArea.Cursor = Cursors.Arrow;
        _canvasVm?.SyncCamera(CanvasTranslate.X, CanvasTranslate.Y, CanvasScale.ScaleX);

        double speed = Math.Sqrt(_momentumVelX * _momentumVelX + _momentumVelY * _momentumVelY);
        if (speed < 50) { _momentumVelX = 0; _momentumVelY = 0; return; }

        _momentumTimer?.Stop();
        _momentumTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _momentumTimer.Tick += ApplyMomentum;
        _momentumTimer.Start();
    }

    /// <summary>
    /// Returns true if the mouse event source is the canvas background
    /// (not inside a terminal or widget card).
    /// </summary>
    private bool IsCanvasBackground(object source)
    {
        var element = source as DependencyObject;
        while (element is not null)
        {
            if (element is TerminalControl)  return false;
            if (element is RichTextBox)      return false;
            if (element is TextBox)          return false;
            if (element is CanvasCardControl) return false;
            if (element == ViewportArea)     return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return true;
    }

    // ─── Zoom (Ctrl + Scroll) ─────────────────────────────────────────────

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
            return;

        e.Handled = true;

        double oldScale = CanvasScale.ScaleX;
        double delta    = e.Delta > 0 ? ZoomStep : -ZoomStep;
        double newScale = Math.Clamp(oldScale + delta, MinZoom, MaxZoom);

        if (Math.Abs(newScale - oldScale) < 0.001) return;

        var mousePos = e.GetPosition(ViewportArea);
        double worldX = (mousePos.X - CanvasTranslate.X) / oldScale;
        double worldY = (mousePos.Y - CanvasTranslate.Y) / oldScale;

        double targetTransX = mousePos.X - worldX * newScale;
        double targetTransY = mousePos.Y - worldY * newScale;

        var duration = new Duration(TimeSpan.FromMilliseconds(100));
        var ease     = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        double finalScale  = newScale;
        double finalTransX = targetTransX;
        double finalTransY = targetTransY;

        var scaleAnim = new DoubleAnimation(newScale, duration)
            { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        scaleAnim.Completed += (_, _) =>
        {
            CanvasScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            CanvasScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            CanvasScale.ScaleX = finalScale;
            CanvasScale.ScaleY = finalScale;
        };
        CanvasScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        CanvasScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(newScale, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });

        var transXAnim = new DoubleAnimation(targetTransX, duration)
            { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        transXAnim.Completed += (_, _) =>
        {
            CanvasTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            CanvasTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            CanvasTranslate.X = finalTransX;
            CanvasTranslate.Y = finalTransY;
        };
        CanvasTranslate.BeginAnimation(TranslateTransform.XProperty, transXAnim);
        CanvasTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(targetTransY, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });

        UpdateZoomLabel(newScale);
    }

    private void ApplyMomentum(object? sender, EventArgs e)
    {
        const double Friction = 0.88;
        const double StopThreshold = 0.5;

        _momentumVelX *= Friction;
        _momentumVelY *= Friction;

        if (Math.Abs(_momentumVelX) < StopThreshold && Math.Abs(_momentumVelY) < StopThreshold)
        {
            _momentumVelX = 0;
            _momentumVelY = 0;
            _momentumTimer?.Stop();
            _canvasVm?.SyncCamera(CanvasTranslate.X, CanvasTranslate.Y, CanvasScale.ScaleX);
            return;
        }

        double frameSeconds = 0.016;
        CanvasTranslate.X += _momentumVelX * frameSeconds;
        CanvasTranslate.Y += _momentumVelY * frameSeconds;
    }

    // ─── Card routed events ───────────────────────────────────────────────

    private void OnCardActivated(object sender, RoutedEventArgs e)
    {
        if (GetItemViewModel(e.OriginalSource) is TerminalCanvasItemViewModel tvm)
        {
            _canvasVm?.SetActiveTerminal(tvm);

            // Focus the terminal's HiddenInput so keyboard events (including arrows) are captured.
            var card = e.Source as CanvasCardControl;
            if (card is not null)
            {
                var terminal = FindVisualChild<TerminalControl>(card);
                terminal?.FocusInput();
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var found = FindVisualChild<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    private void OnCardCloseRequested(object sender, RoutedEventArgs e)
    {
        // D. Animate the card out before removing it from the collection.
        var item = GetItemViewModel(e.OriginalSource);
        if (item is null) return;

        // Find the ContentPresenter that wraps this card in the ItemsControl.
        var cardElement = FindContentPresenterForItem(item);

        if (cardElement is not null)
        {
            // Animate out, then execute the actual removal in the callback.
            AnimateCardOut(cardElement, () =>
            {
                if (item is TerminalCanvasItemViewModel tvm)
                    _mainVm?.CloseTerminalCommand.Execute(tvm.Terminal);
                else
                    _mainVm?.CanvasViewModel.Items.Remove(item);
            });
        }
        else
        {
            // Fallback: no presenter found, remove immediately.
            if (item is TerminalCanvasItemViewModel tvm)
                _mainVm?.CloseTerminalCommand.Execute(tvm.Terminal);
            else
                _mainVm?.CanvasViewModel.Items.Remove(item);
        }
    }

    /// <summary>
    /// D. Animates a card element out (Opacity 1→0, Scale 1→0.9, 180ms),
    /// then calls <paramref name="onComplete"/> on the dispatcher.
    /// Uses FillBehavior=Stop and sets final values in Completed to avoid
    /// animation clock conflicts.
    /// </summary>
    private void AnimateCardOut(FrameworkElement card, Action onComplete)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(180));
        var ease     = new QuadraticEase { EasingMode = EasingMode.EaseIn };

        // Ensure the element has a ScaleTransform for the shrink effect.
        var scale = new System.Windows.Media.ScaleTransform(1.0, 1.0);
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = scale;

        bool completed = false;

        void Finish(object? s, EventArgs ev)
        {
            if (completed) return;
            completed = true;
            // Set final values immediately and clear animation clocks.
            card.BeginAnimation(UIElement.OpacityProperty, null);
            card.Opacity = 0;
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 0.9;
            scale.ScaleY = 0.9;
            Dispatcher.BeginInvoke(onComplete);
        }

        // Opacity: 1 → 0
        var opacityAnim = new DoubleAnimation(1.0, 0.0, duration)
        {
            EasingFunction = ease,
            FillBehavior   = FillBehavior.Stop
        };
        opacityAnim.Completed += Finish;
        card.BeginAnimation(UIElement.OpacityProperty, opacityAnim);

        // ScaleX: 1 → 0.9
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1.0, 0.9, duration)
            {
                EasingFunction = ease,
                FillBehavior   = FillBehavior.Stop
            });

        // ScaleY: 1 → 0.9
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1.0, 0.9, duration)
            {
                EasingFunction = ease,
                FillBehavior   = FillBehavior.Stop
            });
    }

    /// <summary>
    /// D. Walks the visual tree under <see cref="ItemsHost"/> to find the
    /// ContentPresenter whose DataContext matches <paramref name="item"/>.
    /// Returns null if not found.
    /// </summary>
    private FrameworkElement? FindContentPresenterForItem(CanvasItemViewModel item)
    {
        return FindVisualChild<ContentPresenter>(ItemsHost,
            cp => cp.DataContext == item);
    }

    /// <summary>
    /// Recursively searches the visual tree rooted at <paramref name="root"/>
    /// for a child of type <typeparamref name="T"/> matching <paramref name="predicate"/>.
    /// </summary>
    private static T? FindVisualChild<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed && predicate(typed)) return typed;
            var found = FindVisualChild(child, predicate);
            if (found is not null) return found;
        }
        return null;
    }

    private void OnCardFocusRequested(object sender, RoutedEventArgs e)
    {
        if (GetItemViewModel(e.OriginalSource) is CanvasItemViewModel item)
        {
            if (item is TerminalCanvasItemViewModel tvm)
                _canvasVm?.RequestFocus(tvm);
            else
                AnimateFocusOnItem(item);
        }
    }

    // ─── CanvasViewModel event handlers (from VM → View animations) ──────

    private void OnFocusItemRequested(TerminalCanvasItemViewModel item)
    {
        AnimateFocusOnItem(item);
    }

    private void OnFitAllRequested()
    {
        AnimateFitAll();
    }

    private void OnExitFocusModeRequested()
    {
        ExitFocusMode(animated: true);
    }

    // ─── Sidebar ─────────────────────────────────────────────────────────

    private void OnSidebarItemClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as Border)?.DataContext is TerminalCanvasItemViewModel item)
        {
            _canvasVm?.SetActiveTerminal(item);
            CenterOnItem(item, animated: true);
        }
    }

    // ─── Widget buttons ───────────────────────────────────────────────────

    private void OnAddGitWidgetClick(object sender, RoutedEventArgs e)
    {
        _canvasVm?.AddGitWidgetCommand.Execute(null);
    }

    private void OnAddProcessWidgetClick(object sender, RoutedEventArgs e)
    {
        _canvasVm?.AddProcessWidgetCommand.Execute(null);
    }

    // ─── Toolbar buttons ──────────────────────────────────────────────────

    private void OnVerTodosClick(object sender, RoutedEventArgs e)
    {
        ExitFocusMode(animated: true);
    }

    // ─── Focus mode ───────────────────────────────────────────────────────

    private void AnimateFocusOnItem(CanvasItemViewModel item)
    {
        _preFocusScale  = CanvasScale.ScaleX;
        _preFocusTransX = CanvasTranslate.X;
        _preFocusTransY = CanvasTranslate.Y;

        double viewW = ViewportArea.ActualWidth;
        double viewH = ViewportArea.ActualHeight;

        // Scale to fill 90% of viewport
        double targetScaleX = (viewW * 0.90) / item.Width;
        double targetScaleY = (viewH * 0.90) / item.Height;
        double targetScale  = Math.Clamp(Math.Min(targetScaleX, targetScaleY), MinZoom, MaxZoom);

        // Centre item in viewport
        double itemCX     = item.X + item.Width  / 2.0;
        double itemCY     = item.Y + item.Height / 2.0;
        double targetTransX = viewW / 2.0 - itemCX * targetScale;
        double targetTransY = viewH / 2.0 - itemCY * targetScale;

        AnimateCanvasTo(targetScale, targetTransX, targetTransY);
        AnimateFocusOverlay(0.35);

        BtnVerTodos.Visibility = Visibility.Visible;
    }

    private void ExitFocusMode(bool animated)
    {
        _canvasVm?.ExitFocusModeCommand.Execute(null);
        BtnVerTodos.Visibility = Visibility.Collapsed;
        AnimateFocusOverlay(0);

        if (!animated) return;

        AnimateCanvasTo(_preFocusScale, _preFocusTransX, _preFocusTransY);
    }

    // ─── Fit all ─────────────────────────────────────────────────────────

    private void AnimateFitAll()
    {
        if (_canvasVm?.Items.Count == 0) return;

        double viewW = ViewportArea.ActualWidth;
        double viewH = ViewportArea.ActualHeight;

        var models = _canvasVm!.Items.Select(i => i.Model).ToList();

        double minX = models.Min(i => i.X);
        double minY = models.Min(i => i.Y);
        double maxX = models.Max(i => i.X + i.Width);
        double maxY = models.Max(i => i.Y + i.Height);

        double contentW = maxX - minX;
        double contentH = maxY - minY;

        const double padding = 60;
        double scaleX = (viewW - padding * 2) / contentW;
        double scaleY = (viewH - padding * 2) / contentH;
        double scale  = Math.Clamp(Math.Min(scaleX, scaleY), MinZoom, MaxZoom);

        double transX = (viewW - contentW * scale) / 2.0 - minX * scale;
        double transY = (viewH - contentH * scale) / 2.0 - minY * scale;

        AnimateCanvasTo(scale, transX, transY);
    }

    private void CenterOnItem(CanvasItemViewModel item, bool animated)
    {
        double viewW = ViewportArea.ActualWidth;
        double viewH = ViewportArea.ActualHeight;

        double itemCX = item.X + item.Width  / 2.0;
        double itemCY = item.Y + item.Height / 2.0;
        double targetTransX = viewW / 2.0 - itemCX * CanvasScale.ScaleX;
        double targetTransY = viewH / 2.0 - itemCY * CanvasScale.ScaleX;

        if (animated)
            AnimateCanvasTo(CanvasScale.ScaleX, targetTransX, targetTransY);
        else
        {
            CanvasTranslate.X = targetTransX;
            CanvasTranslate.Y = targetTransY;
        }
    }

    // ─── Animations ───────────────────────────────────────────────────────

    private void AnimateCanvasTo(double targetScale, double targetTransX, double targetTransY)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(280));
        var ease     = new CubicEase { EasingMode = EasingMode.EaseInOut };

        double finalScale  = targetScale;
        double finalTransX = targetTransX;
        double finalTransY = targetTransY;

        // Scale animation
        var scaleAnim = new DoubleAnimation(targetScale, duration)
            { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        scaleAnim.Completed += (_, _) =>
        {
            CanvasScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            CanvasScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            CanvasScale.ScaleX = finalScale;
            CanvasScale.ScaleY = finalScale;
            _canvasVm?.SyncCamera(finalTransX, finalTransY, finalScale);
        };
        CanvasScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        CanvasScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(targetScale, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });

        // TranslateX animation
        var transXAnim = new DoubleAnimation(targetTransX, duration)
            { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        transXAnim.Completed += (_, _) =>
        {
            CanvasTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            CanvasTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            CanvasTranslate.X = finalTransX;
            CanvasTranslate.Y = finalTransY;
        };
        CanvasTranslate.BeginAnimation(TranslateTransform.XProperty, transXAnim);
        CanvasTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(targetTransY, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });

        UpdateZoomLabel(targetScale);
    }

    private void AnimateFocusOverlay(double targetOpacity)
    {
        var anim = new DoubleAnimation(targetOpacity, new Duration(TimeSpan.FromMilliseconds(200)));
        FocusOverlay.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private void UpdateZoomLabel(double scale)
    {
        ZoomLabel.Text = $"{(int)Math.Round(scale * 100)}%";
        if (_canvasVm is not null) _canvasVm.ZoomPercent = (int)Math.Round(scale * 100);
    }

    /// <summary>Gets the CanvasItemViewModel from the data context of the event source.</summary>
    private static CanvasItemViewModel? GetItemViewModel(object source)
    {
        var element = source as DependencyObject;
        while (element is not null)
        {
            if (element is FrameworkElement fe && fe.DataContext is CanvasItemViewModel vm)
                return vm;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    // ─── AI context menu actions ──────────────────────────────────────────

    private void OnAiActionRequested(object sender, RoutedEventArgs e)
    {
        if (e is not AiActionEventArgs args) return;
        if (_mainVm is null) return;

        var tvm = GetItemViewModel(e.OriginalSource) as TerminalCanvasItemViewModel;
        if (tvm is null) return;

        // Activate the terminal that was right-clicked
        _canvasVm?.SetActiveTerminal(tvm);
        _mainVm.ActiveTerminal = tvm.Terminal;

        // Find the matching command in the palette and execute it
        var cmdId = args.Action switch
        {
            AiCardAction.FixError       => "ai.fix.error",
            AiCardAction.ExplainOutput  => "ai.explain.output",
            AiCardAction.SuggestCommand => "ai.suggest.command",
            AiCardAction.SendContext    => "ai.send.output",
            AiCardAction.LaunchModel    => $"ai.launch.{args.ModelOrAlias ?? "cc"}",
            AiCardAction.RunAgain       => null,
            _ => null
        };

        if (args.Action == AiCardAction.LaunchModel && args.ModelOrAlias is not null)
        {
            // Direct launch: map to the right palette command
            var launchCmdId = args.ModelOrAlias switch
            {
                "sonnet" => "ai.launch.sonnet",
                "opus"   => "ai.launch.opus",
                "haiku"  => "ai.launch.haiku",
                "agent"  => "ai.launch.agent",
                _ => "ai.launch.cc"
            };
            ExecutePaletteCommand(launchCmdId);
            return;
        }

        if (args.Action == AiCardAction.RunAgain)
        {
            HandleContinuation(tvm, Models.AiContinuationType.RunAgain);
            return;
        }

        if (args.Action == AiCardAction.FixAgain)
        {
            HandleContinuation(tvm, Models.AiContinuationType.FixAgain);
            return;
        }

        if (args.Action == AiCardAction.ExplainMore)
        {
            HandleContinuation(tvm, Models.AiContinuationType.ExplainMore);
            return;
        }

        if (args.Action == AiCardAction.RetryWithModel)
        {
            HandleContinuation(tvm, Models.AiContinuationType.RetryWithModel, 
                args.ModelOrAlias == "opus" ? Models.AiModelSlot.Opus : Models.AiModelSlot.Sonnet);
            return;
        }

        if (cmdId is not null)
            ExecutePaletteCommand(cmdId);
    }

    private void ExecutePaletteCommand(string commandId)
    {
        try
        {
            var paletteService = App.Services.GetService(typeof(Services.ICommandPaletteService)) as Services.ICommandPaletteService;
            if (paletteService is null) return;

            var wslCmd = paletteService.SearchCommands(commandId)
                .FirstOrDefault(c => c.Id == commandId);
            if (wslCmd?.Action is not null)
            {
                _ = wslCmd.Action();
                return;
            }

            var winCmd = paletteService.Search(commandId)
                .FirstOrDefault(c => c.Id == commandId);
            winCmd?.Execute();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiAction] {ex.Message}");
        }
    }

    private void HandleContinuation(TerminalCanvasItemViewModel tvm, Models.AiContinuationType type, Models.AiModelSlot? overrideSlot = null)
    {
        try
        {
            var continuationService = App.Services.GetService(typeof(Services.IAiContinuationService)) as Services.IAiContinuationService;
            if (continuationService is null) return;

            var sessionId = tvm.Terminal.Session?.Id;
            if (sessionId is null) return;

            var continuation = continuationService.BuildContinuation(sessionId, type, overrideSlot);
            if (continuation is null) return;

            var cmdId = continuationService.ResolvePaletteCommandId(continuation);
            if (cmdId is not null)
                ExecutePaletteCommand(cmdId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiContinuation:{type}] {ex.Message}");
        }
    }
}
