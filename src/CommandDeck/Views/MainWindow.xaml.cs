using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CommandDeck.ViewModels;

namespace CommandDeck.Views;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;

    private nint _hwnd;

    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;

            // Apply title bar color from the current theme's BaseBg
            if (Application.Current.Resources["BaseBg"] is Color baseBg)
                ApplyTitleBarColors(baseBg);
        };

        // Keep title bar in sync whenever the theme changes
        App.ThemeApplied += OnThemeApplied;
        Closed += (_, _) => App.ThemeApplied -= OnThemeApplied;

        // Mouse side button (XButton1 = botão traseiro, XButton2 = botão frontal) abre o orb
        PreviewMouseDown += OnPreviewMouseDown;
    }

    private void OnThemeApplied(Color baseBg)
    {
        if (_hwnd == 0) return;
        ApplyTitleBarColors(baseBg);
    }

    private void ApplyTitleBarColors(Color bg)
    {
        // Use dark mode flag depending on the background luminance
        // Perceived luminance: 0.2126R + 0.7152G + 0.0722B
        double luminance = 0.2126 * bg.R + 0.7152 * bg.G + 0.0722 * bg.B;
        int isDark = luminance < 128 ? 1 : 0;
        DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref isDark, sizeof(int));

        // COLORREF = 0x00BBGGRR
        int colorRef = bg.R | (bg.G << 8) | (bg.B << 16);
        DwmSetWindowAttribute(_hwnd, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not (MouseButton.XButton1 or MouseButton.XButton2)) return;
        if (DataContext is MainViewModel { AiOrb: { } orb })
        {
            orb.ToggleRadialMenuCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private void OnShortcutsBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseShortcutsOverlayCommand.Execute(null);
    }

    private void OnBreadcrumbClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.CurrentProject is null) return;
        System.Windows.Clipboard.SetText(vm.CurrentProject.Path);
    }
}
