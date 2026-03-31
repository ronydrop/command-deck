using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommandDeck.ViewModels;

namespace CommandDeck.Controls;

public partial class NoteWidgetControl : UserControl
{
    public NoteWidgetControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is WidgetCanvasItemViewModel vm)
            ApplyColor(vm.NoteColor);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        ColorPickerPopup.IsOpen = !ColorPickerPopup.IsOpen;
    }

    private void OnColorPick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string color
            && DataContext is WidgetCanvasItemViewModel vm)
        {
            vm.NoteColor = color;
            ApplyColor(color);
            ColorPickerPopup.IsOpen = false;
        }
    }

    private void ApplyColor(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            StripBrush.Color = color;
            IconBgBrush.Color = color;
            WidgetBgBrush.Color = color;
        }
        catch { /* ignore invalid color */ }
    }
}
