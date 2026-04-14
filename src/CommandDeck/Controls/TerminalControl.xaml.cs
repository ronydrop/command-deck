using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

/// <summary>
/// Custom WPF control for rendering terminal output and capturing keyboard input.
/// Uses a hidden TextBox to intercept ALL keystrokes (including Backspace, Delete, arrows)
/// and routes them as VT100 sequences to the ConPTY session.
/// The RichTextBox is purely for display (read-only, no focus, no hit-test).
/// </summary>
public partial class TerminalControl : UserControl
{
    private TerminalViewModel? _viewModel;
    private TextChangedEventHandler? _scrollHandler;

    /// <summary>
    /// ConPTY resize is debounced: each resize makes WSL/bash react (SIGWINCH) and redraw the prompt.
    /// WPF fires many SizeChanged events during a drag; without debouncing the terminal floods with prompts.
    /// </summary>
    private DispatcherTimer? _resizeDebounceTimer;

    /// <summary>Last (columns, rows) passed to <see cref="TerminalViewModel.StartSessionAsync"/> or <see cref="TerminalViewModel.ResizeTerminal"/>.</summary>
    private int _lastAppliedCols = -1;
    private int _lastAppliedRows = -1;

    private const int ResizeDebounceMs = 75;

    public TerminalControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged   += OnIsVisibleChanged;
    }

    // ─── Dependency Property: FlowDocument binding ──────────────────────────

    public static readonly DependencyProperty TerminalDocumentProperty =
        DependencyProperty.Register(
            nameof(TerminalDocument),
            typeof(FlowDocument),
            typeof(TerminalControl),
            new PropertyMetadata(null, OnTerminalDocumentChanged));

    public FlowDocument? TerminalDocument
    {
        get => (FlowDocument?)GetValue(TerminalDocumentProperty);
        set => SetValue(TerminalDocumentProperty, value);
    }

    private static void OnTerminalDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TerminalControl control && e.NewValue is FlowDocument document)
        {
            control.TryAssignDocument(document);
        }
    }

    // ─── Event Handlers ─────────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _viewModel = DataContext as TerminalViewModel;

        // Remove previous handler to avoid accumulation of subscriptions
        if (_scrollHandler != null)
        {
            OutputArea.TextChanged -= _scrollHandler;
            _scrollHandler = null;
        }

        if (_viewModel != null)
        {
            // A FlowDocument can only belong to ONE RichTextBox at a time.
            // When TerminalCanvasView and TabbedTerminalView are both in the visual tree
            // (one Collapsed, one Visible), they share the same TerminalViewModel and thus
            // the same FlowDocument. TryAssignDocument handles the ownership conflict
            // gracefully; IsVisibleChanged retries when this control becomes visible.
            TryAssignDocument(_viewModel.OutputDocument);

            // Auto-scroll at Render priority so the document layout is always up-to-date
            _scrollHandler = (_, _) =>
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
                {
                    var sv = GetScrollViewer(OutputArea);
                    if (sv != null && sv.ExtentHeight > sv.ViewportHeight)
                        OutputArea.ScrollToEnd();
                    UpdateCursorPosition();
                });
            };
            OutputArea.TextChanged += _scrollHandler;

            // If the control is already loaded but the session was not started
            // (OnLoaded fired before DataContext was set), start it now.
            if (IsLoaded && ActualWidth > 0 && ActualHeight > 0)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, async () =>
                {
                    var (cw, lh) = MeasureCharDimensions();
                    var (cols, rows) = CalculateTerminalSize(cw, lh);
                    await _viewModel.StartSessionAsync(cols, rows);
                    RecordAppliedSize(cols, rows);
                });
            }
        }
    }

    /// <summary>
    /// Tries to assign <paramref name="document"/> to <see cref="OutputArea"/>.
    /// If the document is already owned by another <see cref="RichTextBox"/>
    /// (which happens when Canvas and TabbedTerminal views are both in the visual tree
    /// but share the same <see cref="TerminalViewModel"/>), the assignment is silently
    /// skipped. <see cref="OnIsVisibleChanged"/> will retry when this control becomes visible.
    /// </summary>
    private void TryAssignDocument(FlowDocument document)
    {
        try
        {
            OutputArea.Document = document;
        }
        catch (ArgumentException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[TerminalControl] Document ownership conflict — will retry on IsVisible: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles visibility transitions.
    /// When this control becomes <b>visible</b>: retries claiming the FlowDocument
    /// at <see cref="DispatcherPriority.Background"/> so the sibling view's controls
    /// have already processed their own visibility-lost event and released ownership.
    /// When this control becomes <b>invisible</b>: immediately releases the document
    /// so the other view's controls can claim it.
    /// </summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            // Became visible — schedule a background-priority retry so WPF finishes
            // collapsing the other view (and its RichTextBoxes release document ownership)
            // before we attempt to claim the document.
            if (_viewModel != null)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                {
                    if (_viewModel != null && IsVisible)
                        TryAssignDocument(_viewModel.OutputDocument);
                });
            }
        }
        else
        {
            // Became invisible — release the document so the other view can own it.
            if (_viewModel != null && OutputArea.Document == _viewModel.OutputDocument)
                OutputArea.Document = new FlowDocument();
        }
    }

    /// <summary>
    /// Handles ALL key presses including special keys (Backspace, Delete, arrows, etc.).
    /// The hidden TextBox receives focus, so WPF routes keyboard events here.
    /// We intercept everything and send VT100 sequences to ConPTY.
    /// IMPORTANT: e.Handled must be set synchronously (before any await) so WPF
    /// does not process the key further (arrow navigation, Backspace deletion, etc.).
    /// </summary>
    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel == null) return;

        // Always clear the hidden TextBox to prevent character accumulation
        // that could interfere with subsequent key processing.
        HiddenInput.Clear();

        string? sequence = TranslateKey(e.Key, Keyboard.Modifiers);
        if (sequence != null)
        {
            // Mark handled BEFORE the await so WPF doesn't process the key
            // further (e.g., arrow keys moving focus, Backspace deleting in TextBox).
            // After an await, e.Handled = true would run asynchronously — too late.
            e.Handled = true;
            try
            {
                await _viewModel.SendKeyDataAsync(sequence);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[KeyDown] {ex}");
            }
            return;
        }

        // For keys not mapped by TranslateKey (printable chars handled by
        // OnPreviewTextInput), still mark navigation-stealing keys as handled
        // so WPF does not move focus away from the terminal.
        if (e.Key is Key.Tab or Key.Up or Key.Down or Key.Left or Key.Right
            or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles printable character input (letters, numbers, symbols).
    /// </summary>
    private async void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_viewModel == null) return;

        if (!string.IsNullOrEmpty(e.Text))
        {
            e.Handled = true;
            // Clear immediately (synchronously) so the TextBox never accumulates chars
            HiddenInput.Clear();
            try
            {
                await _viewModel.SendKeyDataAsync(e.Text);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TextInput] {ex}");
            }
        }
    }

    /// <summary>
    /// Click anywhere on the terminal → focus the hidden input so keys are captured.
    /// </summary>
    /// <summary>
    /// Directs keyboard input to this terminal. Call from parent containers
    /// whenever the card area is clicked so focus always reaches HiddenInput.
    /// </summary>
    public void FocusInput()
    {
        // Reclaim Win32 focus from any HwndHost (e.g., WebView2) before setting WPF focus.
        Window.GetWindow(this)?.Focus();
        HiddenInput.Focus();
        Keyboard.Focus(HiddenInput);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        FocusInput();
        e.Handled = true;
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        // GotFocus is a bubbling event — it fires when HiddenInput receives focus too.
        // Only redirect focus if HiddenInput does not already have keyboard focus,
        // to avoid re-entrant FocusInput() calls.
        if (!HiddenInput.IsKeyboardFocused)
            FocusInput();

        CursorBlock.Visibility = Visibility.Visible;
        UpdateCursorPosition();
    }

    // Redirect any keyboard focus that lands on the UserControl itself
    // (or a child other than HiddenInput) back to HiddenInput.
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (e.NewFocus != HiddenInput)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, (Action)FocusInput);
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        // LostFocus bubbles — only hide cursor if focus truly left this control.
        // Check whether the newly focused element is still within this UserControl.
        if (Keyboard.FocusedElement is DependencyObject focused && IsVisualDescendant(focused))
            return;

        CursorBlock.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Returns true if <paramref name="element"/> is a visual descendant of this control.
    /// </summary>
    private bool IsVisualDescendant(DependencyObject element)
    {
        DependencyObject? current = element;
        while (current != null)
        {
            if (current == this) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    /// <summary>
    /// Finds the internal ScrollViewer of a RichTextBox using the visual tree.
    /// </summary>
    private static ScrollViewer? GetScrollViewer(DependencyObject obj)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is ScrollViewer sv) return sv;
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    /// <summary>
    /// Positions the cursor overlay based on the parser's cursor column.
    /// Computes X from the document's left content edge + cursorCol * charWidth,
    /// and Y from the last line in the document.
    /// </summary>
    private void UpdateCursorPosition()
    {
        try
        {
            var end = OutputArea.Document.ContentEnd;
            var rect = end.GetCharacterRect(LogicalDirection.Backward);
            if (rect == Rect.Empty) return;

            var transform = OutputArea.TransformToVisual(CursorCanvas);
            var (charWidth, _) = MeasureCharDimensions();
            double lineHeight = Math.Max(2, rect.Height);

            if (_viewModel != null)
            {
                int cursorCol = _viewModel.CursorColumn;

                // X: compute from the fixed left content edge.
                // Use ContentStart to find where column 0 begins (includes padding + PagePadding).
                var startRect = OutputArea.Document.ContentStart
                    .GetCharacterRect(LogicalDirection.Forward);
                double baselineX = startRect != Rect.Empty
                    ? startRect.Left
                    : OutputArea.Padding.Left + OutputArea.Document.PagePadding.Left;

                double cursorX = baselineX + cursorCol * charWidth;

                // Y: use the end of the document (last line).
                // Check Forward direction to handle empty lines after \n correctly.
                var forwardRect = end.GetCharacterRect(LogicalDirection.Forward);
                double cursorY = (forwardRect != Rect.Empty && forwardRect.Top > rect.Top)
                    ? forwardRect.Top
                    : rect.Top;

                var origin = transform.Transform(new Point(cursorX, cursorY));
                Canvas.SetLeft(CursorBlock, origin.X);
                Canvas.SetTop(CursorBlock, origin.Y);
                CursorBlock.Height = lineHeight;
                return;
            }

            // Fallback: position at document end
            var fallback = transform.Transform(new Point(rect.Right, rect.Top));
            Canvas.SetLeft(CursorBlock, fallback.X);
            Canvas.SetTop(CursorBlock, fallback.Y);
            CursorBlock.Height = lineHeight;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TerminalControl] Cursor position update failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Called once when the control is first rendered. Creates the ConPTY session with
    /// the actual measured dimensions so the shell never receives a mismatched size.
    /// </summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null && ActualWidth > 0 && ActualHeight > 0)
        {
            var (charWidth, lineHeight) = MeasureCharDimensions();
            var (cols, rows) = CalculateTerminalSize(charWidth, lineHeight);
            await _viewModel.StartSessionAsync(cols, rows);
            RecordAppliedSize(cols, rows);
        }

        // Auto-focus after layout stabilizes so keystrokes reach HiddenInput
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, (Action)FocusInput);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _resizeDebounceTimer?.Stop();

        // Release the FlowDocument when the control leaves the visual tree.
        // This lets other TerminalControl instances (e.g. canvas vs. tabbed view)
        // claim ownership without hitting an ownership conflict.
        if (_viewModel != null && OutputArea.Document == _viewModel.OutputDocument)
            OutputArea.Document = new FlowDocument();
    }

    private void RecordAppliedSize(short columns, short rows)
    {
        _lastAppliedCols = columns;
        _lastAppliedRows = rows;
    }

    /// <summary>
    /// Coalesces rapid SizeChanged events into a single ConPTY resize after the user stops dragging.
    /// </summary>
    private void ScheduleDebouncedResize()
    {
        if (_resizeDebounceTimer == null)
        {
            _resizeDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ResizeDebounceMs)
            };
            _resizeDebounceTimer.Tick += (_, _) =>
            {
                _resizeDebounceTimer.Stop();
                ApplyDebouncedResize();
            };
        }

        _resizeDebounceTimer.Stop();
        _resizeDebounceTimer.Start();
    }

    private void ApplyDebouncedResize()
    {
        if (_viewModel == null || _viewModel.Session == null) return;

        var (cw, lh) = MeasureCharDimensions();
        var (columns, rows) = CalculateTerminalSize(cw, lh);

        if (columns == _lastAppliedCols && rows == _lastAppliedRows)
            return;

        RecordAppliedSize(columns, rows);
        _viewModel.ResizeTerminal(columns, rows);
    }

    /// <summary>
    /// Recalculates terminal dimensions on resize.
    /// Also serves as a fallback session start when OnLoaded fired with zero dimensions.
    /// </summary>
    private async void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_viewModel == null) return;

        var (charWidth, lineHeight) = MeasureCharDimensions();
        var (columns, rows) = CalculateTerminalSize(charWidth, lineHeight);

        // Fallback: if OnLoaded fired before the control had non-zero dimensions,
        // the session was never started. Try now that we have a valid size.
        if (!_viewModel.IsSessionStarted && ActualWidth > 0 && ActualHeight > 0)
        {
            await _viewModel.StartSessionAsync(columns, rows);
            RecordAppliedSize(columns, rows);
            return;
        }

        if (_viewModel.Session == null) return;

        // Same grid size as last apply — skip (duplicate layout events).
        if (columns == _lastAppliedCols && rows == _lastAppliedRows)
            return;

        ScheduleDebouncedResize();
    }

    /// <summary>
    /// Measures character width and line height from the OutputArea's current font.
    /// Falls back to defaults if measurement fails (e.g., font not yet loaded).
    /// </summary>
    private (double charWidth, double lineHeight) MeasureCharDimensions()
    {
        try
        {
            var typeface = new Typeface(
                OutputArea.FontFamily,
                OutputArea.FontStyle,
                OutputArea.FontWeight,
                OutputArea.FontStretch);

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            var ft = new FormattedText(
                "W",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                OutputArea.FontSize,
                Brushes.Black,
                pixelsPerDip);

            return (ft.Width, ft.Height);
        }
        catch
        {
            return (8.4, 18.0);
        }
    }

    /// <summary>
    /// Converts pixel dimensions into terminal columns/rows, accounting for
    /// the RichTextBox padding and FlowDocument page padding so ConPTY receives
    /// the correct size rather than a slightly inflated one.
    /// </summary>
    private (short columns, short rows) CalculateTerminalSize(double charWidth, double lineHeight)
    {
        // Horizontal padding: OutputArea.Padding (left+right) + FlowDocument.PagePadding (left+right)
        double hPad = OutputArea.Padding.Left  + OutputArea.Padding.Right
                    + OutputArea.Document.PagePadding.Left + OutputArea.Document.PagePadding.Right;
        // Vertical padding: OutputArea.Padding (top+bottom) + FlowDocument.PagePadding (top+bottom)
        double vPad = OutputArea.Padding.Top   + OutputArea.Padding.Bottom
                    + OutputArea.Document.PagePadding.Top  + OutputArea.Document.PagePadding.Bottom;

        // Reserve scrollbar width so ConPTY columns stay stable whether or not the scrollbar is visible
        double usableWidth  = Math.Max(0, ActualWidth  - hPad - SystemParameters.VerticalScrollBarWidth);
        double usableHeight = Math.Max(0, ActualHeight - vPad);

        short columns = (short)Math.Max(40, (int)(usableWidth  / charWidth));
        short rows    = (short)Math.Max(10, (int)(usableHeight / lineHeight));
        return (columns, rows);
    }

    // ─── Key Translation ────────────────────────────────────────────────────

    /// <summary>
    /// Translates WPF Key events to VT100/ANSI escape sequences.
    /// Covers ALL keys: control chars, special keys, function keys, and Ctrl combos.
    /// </summary>
    private static string? TranslateKey(Key key, ModifierKeys modifiers)
    {
        // Handle Ctrl+key combinations first
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            return key switch
            {
                Key.A => "\x01",
                Key.B => "\x02",
                Key.C => "\x03",  // SIGINT
                Key.D => "\x04",  // EOF
                Key.E => "\x05",
                Key.F => "\x06",
                Key.G => "\x07",
                Key.H => "\x08",  // Backspace
                Key.I => "\x09",  // Tab
                Key.J => "\x0A",  // Newline
                Key.K => "\x0B",
                Key.L => "\x0C",  // Form feed (clear screen)
                Key.M => "\x0D",  // Enter
                Key.N => "\x0E",
                Key.O => "\x0F",
                Key.P => "\x10",
                Key.Q => "\x11",
                Key.R => "\x12",
                Key.S => "\x13",
                Key.T => "\x14",
                Key.U => "\x15",
                Key.V => "\x16",  // Paste in some terminals
                Key.W => "\x17",
                Key.X => "\x18",
                Key.Y => "\x19",
                Key.Z => "\x1A",  // SIGTSTP
                _ => null
            };
        }

        // Special keys → VT100 escape sequences
        return key switch
        {
            Key.Enter => "\r",
            Key.Back => "\x7F",        // DEL (backspace in terminal)
            Key.Tab => "\t",
            Key.Escape => "\x1B",
            Key.Space => " ",

            // Arrow keys
            Key.Up => "\x1B[A",
            Key.Down => "\x1B[B",
            Key.Right => "\x1B[C",
            Key.Left => "\x1B[D",

            // Navigation keys
            Key.Home => "\x1B[H",
            Key.End => "\x1B[F",
            Key.PageUp => "\x1B[5~",
            Key.PageDown => "\x1B[6~",
            Key.Insert => "\x1B[2~",
            Key.Delete => "\x1B[3~",

            // Function keys
            Key.F1 => "\x1BOP",
            Key.F2 => "\x1BOQ",
            Key.F3 => "\x1BOR",
            Key.F4 => "\x1BOS",
            Key.F5 => "\x1B[15~",
            Key.F6 => "\x1B[17~",
            Key.F7 => "\x1B[18~",
            Key.F8 => "\x1B[19~",
            Key.F9 => "\x1B[20~",
            Key.F10 => "\x1B[21~",
            Key.F11 => "\x1B[23~",
            Key.F12 => "\x1B[24~",

            _ => null  // Printable chars handled by PreviewTextInput
        };
    }
}
