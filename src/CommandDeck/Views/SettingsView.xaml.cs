using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using CommandDeck.ViewModels;

namespace CommandDeck.Views;

public partial class SettingsView : UserControl
{
    private string? _editingShortcut;

    private static readonly Dictionary<string, string> ShortcutDefaults = new()
    {
        ["NewTerminalShortcut"]      = "Ctrl+Shift+T",
        ["CloseTerminalShortcut"]    = "Ctrl+Shift+W",
        ["NextTerminalShortcut"]     = "Ctrl+Tab",
        ["PreviousTerminalShortcut"] = "Ctrl+Shift+Tab",
        ["ToggleSidebarShortcut"]    = "Ctrl+B",
        ["FocusTerminalShortcut"]    = "Ctrl+`",
    };

    private static readonly Dictionary<string, string> ShortcutLabels = new()
    {
        ["NewTerminalShortcut"]      = "Novo Terminal",
        ["CloseTerminalShortcut"]    = "Fechar Terminal",
        ["NextTerminalShortcut"]     = "Próximo Terminal",
        ["PreviousTerminalShortcut"] = "Terminal Anterior",
        ["ToggleSidebarShortcut"]    = "Alternar Sidebar",
        ["FocusTerminalShortcut"]    = "Focar Terminal",
    };

    public SettingsView() { InitializeComponent(); }

    private void OnLinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    // ─── Shortcut editing ────────────────────────────────────────────────────

    private void OnEditShortcut(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string shortcutName) return;

        _editingShortcut = shortcutName;
        CaptureActionLabel.Text = ShortcutLabels.GetValueOrDefault(shortcutName, shortcutName);
        CaptureKeysText.Text = "Aguardando teclas...";
        CaptureKeyDisplay.Background = (Brush)FindResource("Surface0Brush");
        CaptureSuccessBadge.Visibility = Visibility.Collapsed;
        ShortcutCaptureOverlay.Visibility = Visibility.Visible;
        ShortcutCaptureOverlay.Focus();
    }

    private void OnCancelCapture(object sender, RoutedEventArgs e)
    {
        ShortcutCaptureOverlay.Visibility = Visibility.Collapsed;
        _editingShortcut = null;
    }

    private void OnCapturePreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            ShortcutCaptureOverlay.Visibility = Visibility.Collapsed;
            _editingShortcut = null;
            return;
        }

        // Modifier-only: show partial combo
        if (IsModifierKey(key))
        {
            var partial = BuildModifiers();
            CaptureKeysText.Text = string.IsNullOrEmpty(partial) ? "Aguardando teclas..." : partial + "+...";
            return;
        }

        var combo = BuildKeyCombo(key);
        CaptureKeysText.Text = combo;
        _ = ApplyAndCloseAsync(combo);
    }

    private async Task ApplyAndCloseAsync(string combo)
    {
        if (_editingShortcut == null || DataContext is not SettingsViewModel vm) return;

        SetShortcut(vm, _editingShortcut, combo);

        // Success feedback
        CaptureKeyDisplay.Background = (Brush)FindResource("AccentGreenBrush");
        CaptureSuccessBadge.Visibility = Visibility.Visible;

        await Task.Delay(900);

        ShortcutCaptureOverlay.Visibility = Visibility.Collapsed;
        _editingShortcut = null;
    }

    private void OnResetShortcut(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string shortcutName) return;
        if (DataContext is not SettingsViewModel vm) return;
        if (!ShortcutDefaults.TryGetValue(shortcutName, out var def)) return;
        SetShortcut(vm, shortcutName, def);
    }

    private static void SetShortcut(SettingsViewModel vm, string name, string value)
    {
        switch (name)
        {
            case "NewTerminalShortcut":      vm.NewTerminalShortcut = value;      break;
            case "CloseTerminalShortcut":    vm.CloseTerminalShortcut = value;    break;
            case "NextTerminalShortcut":     vm.NextTerminalShortcut = value;     break;
            case "PreviousTerminalShortcut": vm.PreviousTerminalShortcut = value; break;
            case "ToggleSidebarShortcut":    vm.ToggleSidebarShortcut = value;    break;
            case "FocusTerminalShortcut":    vm.FocusTerminalShortcut = value;    break;
        }
    }

    // ─── Key helpers ─────────────────────────────────────────────────────────

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;

    private static string BuildModifiers()
    {
        var parts = new List<string>(3);
        if (Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl))  parts.Add("Ctrl");
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) parts.Add("Shift");
        if (Keyboard.IsKeyDown(Key.LeftAlt)   || Keyboard.IsKeyDown(Key.RightAlt))   parts.Add("Alt");
        return string.Join("+", parts);
    }

    private static string BuildKeyCombo(Key key)
    {
        var parts = new List<string>(4);
        if (Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl))  parts.Add("Ctrl");
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) parts.Add("Shift");
        if (Keyboard.IsKeyDown(Key.LeftAlt)   || Keyboard.IsKeyDown(Key.RightAlt))   parts.Add("Alt");
        parts.Add(FormatKey(key));
        return string.Join("+", parts);
    }

    private static string FormatKey(Key key) => key switch
    {
        Key.OemTilde        => "`",
        Key.OemBackslash    => "\\",
        Key.OemQuestion     => "/",
        Key.OemPeriod       => ".",
        Key.OemComma        => ",",
        Key.OemMinus        => "-",
        Key.OemPlus         => "=",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets=> "]",
        Key.OemQuotes       => "'",
        Key.OemSemicolon    => ";",
        Key.Tab             => "Tab",
        Key.Space           => "Space",
        Key.Return          => "Enter",
        Key.Back            => "Backspace",
        Key.Delete          => "Delete",
        Key.Insert          => "Insert",
        Key.Home            => "Home",
        Key.End             => "End",
        Key.PageUp          => "PageUp",
        Key.PageDown        => "PageDown",
        Key.Up              => "Up",
        Key.Down            => "Down",
        Key.Left            => "Left",
        Key.Right           => "Right",
        Key.F1  => "F1",  Key.F2  => "F2",  Key.F3  => "F3",  Key.F4  => "F4",
        Key.F5  => "F5",  Key.F6  => "F6",  Key.F7  => "F7",  Key.F8  => "F8",
        Key.F9  => "F9",  Key.F10 => "F10", Key.F11 => "F11", Key.F12 => "F12",
        _ => key.ToString()
    };
}
