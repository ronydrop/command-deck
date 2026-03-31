using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommandDeck.Controls;
using CommandDeck.Models;
using CommandDeck.ViewModels;

namespace CommandDeck.Views;

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

    // ─── Zoom target state (accumulated across rapid scroll ticks) ──────

    private double _zoomTargetScale = 1.0;
    private double _zoomTargetTransX;
    private double _zoomTargetTransY;
    private bool   _zoomTargetsInitialized;

    // ─── Lerp-based zoom interpolation ────────────────────────────────
    private double _zoomCurrentScale = 1.0;
    private double _zoomCurrentTransX = 40.0;
    private double _zoomCurrentTransY = 40.0;
    private bool   _zoomLerpActive;
    private const double ZoomLerpFactor  = 0.18;
    private const double ZoomLerpEpsilon = 0.0005;
    private bool _zoomRequiresCtrl = true;

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
            _canvasVm.LayoutModeChanged += OnLayoutModeChanged;

            // Read zoom mode from settings and react to changes
            _zoomRequiresCtrl = _canvasVm.ZoomRequiresCtrl;
            _canvasVm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(TerminalCanvasViewModel.ZoomRequiresCtrl))
                    _zoomRequiresCtrl = _canvasVm.ZoomRequiresCtrl;
            };
        }


        // Feed viewport size to ViewModel for tiled layout calculation
        ViewportArea.SizeChanged += OnViewportSizeChanged;

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

        ViewportArea.SizeChanged -= OnViewportSizeChanged;

        if (_canvasVm is not null)
        {
            _canvasVm.FocusItemRequested -= OnFocusItemRequested;
            _canvasVm.FitAllRequested -= OnFitAllRequested;
            _canvasVm.ExitFocusModeRequested -= OnExitFocusModeRequested;
            _canvasVm.LayoutModeChanged -= OnLayoutModeChanged;
        }

        _momentumTimer?.Stop();
        _momentumTimer = null;
        StopZoomLerp();

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

    // ─── Layout mode change (from ViewModel) ──────────────────────────────

    private void OnLayoutModeChanged(LayoutMode newMode)
    {
        if (newMode == LayoutMode.Tiled)
        {
            // Reset camera: scale 1, Y locked to 0, X to 0 (user can pan horizontally)
            CanvasScale.ScaleX = 1;
            CanvasScale.ScaleY = 1;
            CanvasTranslate.X = 0;
            CanvasTranslate.Y = 0;
            _zoomTargetsInitialized = false;
            _canvasVm?.SyncCamera(0, 0, 1);
        }
        else
        {
            // Restore camera from snapshot when returning to canvas
            var snapshot = _canvasVm?.PopCameraSnapshot();
            if (snapshot is not null)
            {
                CanvasScale.ScaleX = snapshot.Zoom;
                CanvasScale.ScaleY = snapshot.Zoom;
                CanvasTranslate.X = snapshot.OffsetX;
                CanvasTranslate.Y = snapshot.OffsetY;
                _zoomTargetsInitialized = false;
                _canvasVm?.SyncCamera(snapshot.OffsetX, snapshot.OffsetY, snapshot.Zoom);
            }
        }
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _canvasVm?.OnViewportSizeChanged(e.NewSize.Width, e.NewSize.Height);
    }

    // ─── Keyboard (global) ────────────────────────────────────────────────

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _canvasVm?.IsFocusMode == true)
        {
            ExitFocusMode(animated: true);
            e.Handled = true;
            return;
        }

        // Ctrl+V: paste image from clipboard onto canvas
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (TryPasteImageFromClipboard())
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
        // Allow panning in both canvas and tiled modes
        if (_canvasVm is null) return;

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

        // Stop any active zoom lerp and reset targets.
        StopZoomLerp();

        Mouse.Capture(ViewportArea);
        ViewportArea.Cursor = Cursors.SizeAll;
        e.Handled = false;
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;

        var pos = e.GetPosition(ViewportArea);
        CanvasTranslate.X = _panOriginX + pos.X - _panStart.X;

        // In tiled mode, lock vertical axis (horizontal strip only)
        if (_canvasVm?.IsTiledMode != true)
            CanvasTranslate.Y = _panOriginY + pos.Y - _panStart.Y;


        var now = DateTime.UtcNow;
        double dt = (now - _lastPanTime).TotalSeconds;
        if (dt > 0 && dt < 0.1)
        {
            _momentumVelX = (pos.X - _lastPanPos.X) / dt;
            _momentumVelY = _canvasVm?.IsTiledMode == true ? 0 : (pos.Y - _lastPanPos.Y) / dt;
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
        _momentumVelX = 0;
        _momentumVelY = 0;
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
        return false;
    }

    // ─── Zoom (Scroll / Ctrl+Scroll) ───────────────────────────────────────

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // No zoom in tiled mode
        if (_canvasVm?.IsCanvasMode != true) return;

        // Check zoom mode: if CtrlScroll, require Ctrl key
        if (_zoomRequiresCtrl &&
            !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
            return;

        e.Handled = true;

        // On first zoom tick (or after pan/focus resets targets), seed from current transforms.
        if (!_zoomTargetsInitialized)
        {
            _zoomTargetScale  = CanvasScale.ScaleX;
            _zoomTargetTransX = CanvasTranslate.X;
            _zoomTargetTransY = CanvasTranslate.Y;
            _zoomCurrentScale  = CanvasScale.ScaleX;
            _zoomCurrentTransX = CanvasTranslate.X;
            _zoomCurrentTransY = CanvasTranslate.Y;
            _zoomTargetsInitialized = true;
        }

        // Accumulate zoom delta against the TARGET (not the mid-lerp value).
        double oldScale = _zoomTargetScale;
        double delta    = e.Delta > 0 ? ZoomStep : -ZoomStep;
        double newScale = Math.Clamp(oldScale + delta, MinZoom, MaxZoom);

        if (Math.Abs(newScale - oldScale) < 0.001) return;

        // Compute world-space point under cursor using target transforms.
        var mousePos = e.GetPosition(ViewportArea);
        double worldX = (mousePos.X - _zoomTargetTransX) / oldScale;
        double worldY = (mousePos.Y - _zoomTargetTransY) / oldScale;

        double targetTransX = mousePos.X - worldX * newScale;
        double targetTransY = mousePos.Y - worldY * newScale;

        // Update accumulated targets.
        _zoomTargetScale  = newScale;
        _zoomTargetTransX = targetTransX;
        _zoomTargetTransY = targetTransY;

        UpdateZoomLabel(newScale);

        // Start the per-frame lerp if not already running.
        if (!_zoomLerpActive)
        {
            _zoomLerpActive = true;
            CompositionTarget.Rendering += OnZoomLerpFrame;
        }
    }

    private void OnZoomLerpFrame(object? sender, EventArgs e)
    {
        // Lerp current values toward targets
        _zoomCurrentScale  += (_zoomTargetScale  - _zoomCurrentScale)  * ZoomLerpFactor;
        _zoomCurrentTransX += (_zoomTargetTransX - _zoomCurrentTransX) * ZoomLerpFactor;
        _zoomCurrentTransY += (_zoomTargetTransY - _zoomCurrentTransY) * ZoomLerpFactor;

        // Apply to transforms directly (no animation objects)
        CanvasScale.ScaleX = _zoomCurrentScale;
        CanvasScale.ScaleY = _zoomCurrentScale;
        CanvasTranslate.X  = _zoomCurrentTransX;
        CanvasTranslate.Y  = _zoomCurrentTransY;


        // Check convergence
        bool converged =
            Math.Abs(_zoomTargetScale  - _zoomCurrentScale)  < ZoomLerpEpsilon &&
            Math.Abs(_zoomTargetTransX - _zoomCurrentTransX) < ZoomLerpEpsilon &&
            Math.Abs(_zoomTargetTransY - _zoomCurrentTransY) < ZoomLerpEpsilon;

        if (converged)
        {
            // Snap to exact target values
            CanvasScale.ScaleX = _zoomTargetScale;
            CanvasScale.ScaleY = _zoomTargetScale;
            CanvasTranslate.X  = _zoomTargetTransX;
            CanvasTranslate.Y  = _zoomTargetTransY;
            _zoomCurrentScale  = _zoomTargetScale;
            _zoomCurrentTransX = _zoomTargetTransX;
            _zoomCurrentTransY = _zoomTargetTransY;

            // Stop rendering callback
            CompositionTarget.Rendering -= OnZoomLerpFrame;
            _zoomLerpActive = false;
            _zoomTargetsInitialized = false;

            // Sync to ViewModel (replaces the debounced timer)
            _canvasVm?.SyncCamera(CanvasTranslate.X, CanvasTranslate.Y, CanvasScale.ScaleX);
        }
    }

    private void StopZoomLerp()
    {
        if (_zoomLerpActive)
        {
            CompositionTarget.Rendering -= OnZoomLerpFrame;
            _zoomLerpActive = false;
        }
        _zoomTargetsInitialized = false;
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
        if (_canvasVm?.IsTiledMode != true)
            CanvasTranslate.Y += _momentumVelY * frameSeconds;

    }

    // ─── Empty state parallax ──────────────────────────────────────────────

    /// <summary>
    /// Moves the empty-state placeholder to follow canvas pan/zoom,
    /// giving the impression that it floats on the canvas like a terminal card.
    /// Uses a damped factor so it moves slightly less than the canvas (parallax feel).
    /// </summary>
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
        // Prefer sender.DataContext (direct reference), fall back to visual-tree walk.
        var item = (sender as FrameworkElement)?.DataContext as CanvasItemViewModel
                   ?? GetItemViewModel(e.OriginalSource);
        if (item is null) return;

        // Execute removal immediately — don't delay behind animation callback.
        if (item is TerminalCanvasItemViewModel tvm)
            _mainVm?.CloseTerminalCommand.Execute(tvm.Terminal);
        else
            _mainVm?.CanvasViewModel.Items.Remove(item);

        // Fire-and-forget animation on the card element (optional visual feedback).
        var cardElement = FindContentPresenterForItem(item);
        if (cardElement is not null)
            AnimateCardOut(cardElement, () => { });
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

    private void OnSidebarCloseClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // prevent OnSidebarItemClick from firing
        if ((sender as FrameworkElement)?.DataContext is TerminalCanvasItemViewModel item)
        {
            _mainVm?.CloseTerminalCommand.Execute(item.Terminal);
        }
    }

    // ─── Widget buttons ───────────────────────────────────────────────────

    private void OnToggleGitWidgetClick(object sender, RoutedEventArgs e)
    {
        if (_canvasVm is null) return;

        // Git widget (320x280) — top-right corner of visible viewport
        const double widgetW = 320;
        const double margin = 20;
        double vpX = ViewportArea.ActualWidth - widgetW - margin;
        double vpY = margin;

        double canvasX = (vpX - CanvasTranslate.X) / CanvasScale.ScaleX;
        double canvasY = (vpY - CanvasTranslate.Y) / CanvasScale.ScaleY;

        _canvasVm.ToggleGitWidget(canvasX, canvasY);
    }

    private void OnToggleProcessWidgetClick(object sender, RoutedEventArgs e)
    {
        if (_canvasVm is null) return;

        // Process widget (400x350) — to the left of the Git widget area
        const double gitW = 320;
        const double processW = 400;
        const double margin = 20;
        const double gap = 20;
        double vpX = ViewportArea.ActualWidth - gitW - processW - gap - margin;
        double vpY = margin;

        double canvasX = (vpX - CanvasTranslate.X) / CanvasScale.ScaleX;
        double canvasY = (vpY - CanvasTranslate.Y) / CanvasScale.ScaleY;

        _canvasVm.ToggleProcessWidget(canvasX, canvasY);
    }

    private void OnAddNoteWidgetClick(object sender, RoutedEventArgs e)
    {
        if (_canvasVm is null) return;

        const double margin = 20;
        double vpX = margin + 60;
        double vpY = ViewportArea.ActualHeight - 260 - margin;

        double canvasX = (vpX - CanvasTranslate.X) / CanvasScale.ScaleX;
        double canvasY = (vpY - CanvasTranslate.Y) / CanvasScale.ScaleY;

        _canvasVm.AddNoteWidget(canvasX, canvasY);
    }

    // ─── Image paste / drop ─────────────────────────────────────────────

    /// <summary>
    /// Attempts to paste an image from the clipboard onto the canvas.
    /// Returns true if an image was pasted.
    /// </summary>
    private bool TryPasteImageFromClipboard()
    {
        if (_canvasVm is null) return false;

        // Check if a terminal or text input is focused — don't intercept paste there
        var focused = Keyboard.FocusedElement;
        if (focused is System.Windows.Controls.Primitives.TextBoxBase) return false;

        BitmapSource? bitmap = null;
        string? filePath = null;

        // Priority 1: bitmap image in clipboard
        if (Clipboard.ContainsImage())
        {
            bitmap = Clipboard.GetImage();
        }
        // Priority 2: image file in clipboard (e.g. copied from Explorer)
        else if (Clipboard.ContainsFileDropList())
        {
            var files = Clipboard.GetFileDropList();
            foreach (string? f in files)
            {
                if (f is not null && IsImageFile(f))
                {
                    filePath = f;
                    break;
                }
            }
        }

        if (bitmap is null && filePath is null) return false;

        // Place at center of current viewport
        double vpCenterX = ViewportArea.ActualWidth / 2.0;
        double vpCenterY = ViewportArea.ActualHeight / 2.0;
        double canvasX = (vpCenterX - CanvasTranslate.X) / CanvasScale.ScaleX - 200;
        double canvasY = (vpCenterY - CanvasTranslate.Y) / CanvasScale.ScaleY - 150;

        var item = _canvasVm.AddImageWidget(canvasX, canvasY);

        if (filePath is not null)
            item.ImagePath = filePath;
        else if (bitmap is not null)
            item.SetImageFromBitmap(bitmap);

        return true;
    }

    private void OnViewportDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;

        e.Handled = true;
    }

    private void OnViewportDrop(object sender, DragEventArgs e)
    {
        if (_canvasVm is null) return;

        // Get drop position in canvas coordinates
        var dropPos = e.GetPosition(ViewportArea);
        double canvasX = (dropPos.X - CanvasTranslate.X) / CanvasScale.ScaleX;
        double canvasY = (dropPos.Y - CanvasTranslate.Y) / CanvasScale.ScaleY;

        // File drop from Explorer
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files is null) return;

            double offsetX = 0;
            foreach (var file in files)
            {
                if (!IsImageFile(file)) continue;

                var item = _canvasVm.AddImageWidget(canvasX + offsetX, canvasY);
                item.ImagePath = file;
                offsetX += 420; // cascade horizontally
                e.Handled = true;
            }
            return;
        }

        // Bitmap data drop
        if (e.Data.GetDataPresent(DataFormats.Bitmap))
        {
            if (e.Data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            {
                var item = _canvasVm.AddImageWidget(canvasX, canvasY);
                item.SetImageFromBitmap(bitmap);
                e.Handled = true;
            }
        }
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".ico" or ".tiff" or ".tif";
    }

    // ─── Toolbar buttons ──────────────────────────────────────────────────

    private void OnVerTodosClick(object sender, RoutedEventArgs e)
    {
        ExitFocusMode(animated: true);
    }

    private void OnAiDropdownClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void OnToolbarOpenDropdownClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void OnToolbarCommitDropdownClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
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
        // Stop any active zoom lerp before starting focus/fit-all animation.
        StopZoomLerp();

        // Cancel any running animations first.
        CanvasScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CanvasScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        CanvasTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        CanvasTranslate.BeginAnimation(TranslateTransform.YProperty, null);

        var duration = new Duration(TimeSpan.FromMilliseconds(280));
        var ease     = new CubicEase { EasingMode = EasingMode.EaseInOut };

        double finalScale  = targetScale;
        double finalTransX = targetTransX;
        double finalTransY = targetTransY;

        // Scale animation — HoldEnd keeps value stable until Completed clears it.
        var scaleAnim = new DoubleAnimation(targetScale, duration)
            { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
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
            new DoubleAnimation(targetScale, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });

        // Translate animation
        var transXAnim = new DoubleAnimation(targetTransX, duration)
            { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        transXAnim.Completed += (_, _) =>
        {
            CanvasTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            CanvasTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            CanvasTranslate.X = finalTransX;
            CanvasTranslate.Y = finalTransY;
        };
        CanvasTranslate.BeginAnimation(TranslateTransform.XProperty, transXAnim);
        CanvasTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(targetTransY, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });

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

    private void HandleContinuation(TerminalCanvasItemViewModel tvm, Models.AiContinuationType type)
    {
        try
        {
            var continuationService = App.Services.GetService(typeof(Services.IAiContinuationService)) as Services.IAiContinuationService;
            if (continuationService is null) return;

            var sessionId = tvm.Terminal.Session?.Id;
            if (sessionId is null) return;

            var continuation = continuationService.BuildContinuation(sessionId, type);
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
