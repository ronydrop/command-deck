using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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
        if (_viewModel != null)
        {
            OutputArea.Document = _viewModel.OutputDocument;

            // Auto-scroll when new content arrives
            OutputArea.TextChanged += (_, _) =>
            {
                OutputArea.ScrollToEnd();
            };
        }
    }

    /// <summary>
    /// Handles ALL key presses including special keys (Backspace, Delete, arrows, etc.).
    /// The hidden TextBox receives focus, so WPF routes keyboard events here.
    /// We intercept everything and send VT100 sequences to ConPTY.
    /// </summary>
    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel == null) return;

        string? sequence = TranslateKey(e.Key, Keyboard.Modifiers);
        if (sequence != null)
        {
            await _viewModel.SendKeyDataAsync(sequence);
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
            await _viewModel.SendKeyDataAsync(e.Text);
            e.Handled = true;
        }

        // Clear the hidden TextBox to prevent accumulation
        HiddenInput.Clear();
    }

    /// <summary>
    /// Click anywhere on the terminal → focus the hidden input so keys are captured.
    /// </summary>
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        HiddenInput.Focus();
        Keyboard.Focus(HiddenInput);
        e.Handled = true;
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        HiddenInput.Focus();
        Keyboard.Focus(HiddenInput);
        CursorBlock.Visibility = Visibility.Visible;
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        CursorBlock.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Recalculates terminal dimensions on resize.
    /// </summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_viewModel == null) return;

        // Cascadia Code 14pt: ~8.4px per char width, ~18px per line height
        double charWidth = 8.4;
        double lineHeight = 18.0;

        short columns = (short)Math.Max(40, (int)(ActualWidth / charWidth));
        short rows = (short)Math.Max(10, (int)(ActualHeight / lineHeight));

        _viewModel.ResizeTerminal(columns, rows);
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
