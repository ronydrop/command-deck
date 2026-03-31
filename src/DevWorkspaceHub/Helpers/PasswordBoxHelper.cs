using System.Windows;
using System.Windows.Controls;

namespace DevWorkspaceHub.Helpers;

/// <summary>
/// Attached property that enables two-way binding on PasswordBox.Password.
/// </summary>
public static class PasswordBoxHelper
{
    private static bool _isUpdating;

    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnBoundPasswordChanged));

    public static string GetBoundPassword(DependencyObject obj) =>
        (string)obj.GetValue(BoundPasswordProperty);

    public static void SetBoundPassword(DependencyObject obj, string value) =>
        obj.SetValue(BoundPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox pb) return;

        pb.PasswordChanged -= OnPasswordChanged;

        if (!_isUpdating)
            pb.Password = (string)e.NewValue;

        pb.PasswordChanged += OnPasswordChanged;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox pb) return;

        _isUpdating = true;
        SetBoundPassword(pb, pb.Password);
        _isUpdating = false;
    }
}
