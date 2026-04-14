using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommandDeck.Models;
using CommandDeck.Services;

namespace CommandDeck.Controls;

/// <summary>
/// Panel displaying all enabled catalog widget entries as draggable blocks.
/// Drag a block onto a <see cref="BentoLayoutPresenter"/> slot to place a widget there.
/// </summary>
public partial class BlockCatalogPanel : UserControl
{
    private Point _dragStartPoint;
    private bool _isDragging;

    public static readonly DependencyProperty CatalogServiceProperty =
        DependencyProperty.Register(
            nameof(CatalogService),
            typeof(IWidgetCatalogService),
            typeof(BlockCatalogPanel),
            new PropertyMetadata(null, OnCatalogServiceChanged));

    public IWidgetCatalogService? CatalogService
    {
        get => (IWidgetCatalogService?)GetValue(CatalogServiceProperty);
        set => SetValue(CatalogServiceProperty, value);
    }

    private static void OnCatalogServiceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BlockCatalogPanel panel && e.NewValue is IWidgetCatalogService catalog)
            panel.CatalogItems.ItemsSource = catalog.Enabled;
    }

    public BlockCatalogPanel()
    {
        InitializeComponent();
    }

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging) return;

        var pos = e.GetPosition(null);
        var diff = _dragStartPoint - pos;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (sender is not Border border || border.Tag is not WidgetCatalogEntry entry) return;

        _isDragging = true;
        var data = new DataObject("CommandDeck.CatalogKey", entry.Key);
        DragDrop.DoDragDrop(border, data, DragDropEffects.Copy);
        _isDragging = false;
    }
}
