using System.Windows;
using System.Windows.Controls;

namespace CommandDeck.Helpers;

/// <summary>
/// Attached property that enables two-way binding on PasswordBox.Password.
/// </summary>
public static class PasswordBoxHelper
{
    [ThreadStatic]
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
        ((FrameworkElement)obj).SetCurrentValue(BoundPasswordProperty, value);

    private static readonly DependencyPropertyKey HasPasswordPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly(
            "HasPassword",
            typeof(bool),
            typeof(PasswordBoxHelper),
            new PropertyMetadata(false));

    public static readonly DependencyProperty HasPasswordProperty =
        HasPasswordPropertyKey.DependencyProperty;

    public static bool GetHasPassword(DependencyObject obj) =>
        (bool)obj.GetValue(HasPasswordProperty);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox pb) return;

        pb.PasswordChanged -= OnPasswordChanged;

        if (!_isUpdating)
            pb.Password = (string)e.NewValue;

        pb.PasswordChanged += OnPasswordChanged;
        d.SetValue(HasPasswordPropertyKey, !string.IsNullOrEmpty((string)e.NewValue));
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox pb) return;

        _isUpdating = true;
        SetBoundPassword(pb, pb.Password);
        _isUpdating = false;
        pb.SetValue(HasPasswordPropertyKey, pb.Password.Length > 0);
    }
}
