using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace DevWorkspaceHub.Controls;

/// <summary>
/// Toast notification card with slide-in animation.
/// </summary>
public partial class NotificationToast : UserControl
{
    public NotificationToast()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Play slide-in animation on load
        if (TryFindResource("SlideIn") is Storyboard storyboard)
        {
            storyboard.Begin(this);
        }
    }
}
