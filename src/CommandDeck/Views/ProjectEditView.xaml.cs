using System.Windows.Controls;
using System.Windows.Input;
using CommandDeck.ViewModels;

namespace CommandDeck.Views;

public partial class ProjectEditView : UserControl
{
    public ProjectEditView()
    {
        InitializeComponent();
    }

    private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement element &&
            element.DataContext is string color &&
            DataContext is ProjectEditViewModel vm)
        {
            vm.Color = color;
        }
    }
}
