using CommandDeck.Controls;
using CommandDeck.Models;
using CommandDeck.ViewModels;

namespace CommandDeck.Views;

public partial class BranchSelectorOverlay : SearchOverlayControlBase
{
    private BranchSelectorViewModel? _vm;

    public BranchSelectorOverlay()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => _vm = DataContext as BranchSelectorViewModel;
    }

    private void OnBranchClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is GitBranchInfo branch)
        {
            if (_vm is not null) _vm.SelectedBranch = branch;
            _vm?.ConfirmCommand.Execute(null);
        }
    }
}
