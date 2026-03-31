using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DevWorkspaceHub.ViewModels;

namespace DevWorkspaceHub.Controls;

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
    private bool _scrollPending;

    public TerminalControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
            control.OutputArea.Document = document;
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
            OutputArea.Document = _viewModel.OutputDocument;

            // Throttled auto-scroll + cursor update coalesced into one Background dispatch
            _scrollHandler = (_, _) =>
            {
                if (_scrollPending) return;
                _scrollPending = true;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                {
                    _scrollPending = false;
                    OutputArea.ScrollToEnd();
                    UpdateCursorPosition();
                });
            };
            OutputArea.TextChanged += _scrollHandler;
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
    /// Positions the cursor overlay at the end of the last character in the document.
    /// </summary>
    private void UpdateCursorPosition()
    {
        try
        {
            var end = OutputArea.Document.ContentEnd;
            var rect = end.GetCharacterRect(LogicalDirection.Backward);
            if (rect == Rect.Empty) return;

            // Transform from RichTextBox coordinates to CursorCanvas coordinates
            var transform = OutputArea.TransformToVisual(CursorCanvas);
            var origin = transform.Transform(new Point(rect.Right, rect.Top));

            Canvas.SetLeft(CursorBlock, origin.X);
            Canvas.SetTop(CursorBlock, origin.Y);
            CursorBlock.Height = Math.Max(2, rect.Height);
        }
        catch { }
    }

    /// <summary>
    /// Recalculates terminal dimensions on resize.
    /// </summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_viewModel == null) return;

        var (charWidth, lineHeight) = MeasureCharDimensions();

        short columns = (short)Math.Max(40, (int)(ActualWidth / charWidth));
        short rows = (short)Math.Max(10, (int)(ActualHeight / lineHeight));

        _viewModel.ResizeTerminal(columns, rows);
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
