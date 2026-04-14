using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CommandDeck.Controls;

/// <summary>
/// Simple input dialog for renaming a canvas tile.
/// </summary>
public class TileLabelDialog : Window
{
    private readonly TextBox _input;
    public string? NewLabel { get; private set; }

    public TileLabelDialog(string currentLabel)
    {
        Title = "Renomear Tile";
        Width = 320;
        Height = 130;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 46));
        Foreground = new SolidColorBrush(Colors.White);
        WindowStyle = WindowStyle.ToolWindow;

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Nome do tile (deixe vazio para usar o padrão):",
            Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(label, 0);

        _input = new TextBox
        {
            Text = currentLabel,
            Height = 28,
            Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            Foreground = new SolidColorBrush(Colors.White),
            CaretBrush = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromRgb(88, 91, 112)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 13
        };
        Grid.SetRow(_input, 1);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(btnPanel, 2);

        var btnOk = new Button
        {
            Content = "OK",
            Width = 70,
            Height = 26,
            Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(137, 180, 250)),
            Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
            BorderThickness = new Thickness(0)
        };
        btnOk.Click += (_, _) => { NewLabel = _input.Text; DialogResult = true; };

        var btnCancel = new Button
        {
            Content = "Cancelar",
            Width = 70,
            Height = 26,
            Background = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0)
        };
        btnCancel.Click += (_, _) => { DialogResult = false; };

        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);

        grid.Children.Add(label);
        grid.Children.Add(_input);
        grid.Children.Add(btnPanel);

        Content = grid;

        Loaded += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };

        _input.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { NewLabel = _input.Text; DialogResult = true; }
            if (e.Key == System.Windows.Input.Key.Escape) DialogResult = false;
        };
    }
}
