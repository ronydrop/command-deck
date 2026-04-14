using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommandDeck.Helpers;
using CommandDeck.Services;
using CommandDeck.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CommandDeck.Controls;

/// <summary>
/// Presents the 3×3 Bento grid layout. Slots 0..7 are created in code-behind;
/// the center cell (row=1, col=1) is reserved for the <see cref="BlockCatalogPanel"/>.
/// Items are placed by dragging catalog entries from that panel onto the drop slots.
/// </summary>
public partial class BentoLayoutPresenter : UserControl
{
    private readonly Border[] _slots = new Border[BentoSlotMap.SlotCount];
    private readonly System.Collections.Generic.Dictionary<int, Point> _slotDragStarts = new();
    private bool _occupiedDragging;

    public BentoLayoutPresenter()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        CreateSlots();
    }

    private void CreateSlots()
    {
        var grid = (Grid)Content;

        for (int i = 0; i < BentoSlotMap.SlotCount; i++)
        {
            var (row, col) = BentoSlotMap.SlotToGrid(i);
            int slotIndex = i;

            var slot = new Border
            {
                Margin = new Thickness(8),
                CornerRadius = new CornerRadius(10),
                AllowDrop = true,
                Tag = slotIndex
            };

            // Dashed empty-slot border via BorderBrush
            slot.SetResourceReference(Border.BorderBrushProperty, "Surface2Brush");
            slot.BorderThickness = new Thickness(2);

            // Placeholder text
            var placeholder = CreatePlaceholder();
            slot.Child = placeholder;

            slot.DragEnter += OnSlotDragEnter;
            slot.DragOver  += OnSlotDragOver;
            slot.DragLeave += OnSlotDragLeave;
            slot.Drop      += OnSlotDrop;

            Grid.SetRow(slot, row);
            Grid.SetColumn(slot, col);
            grid.Children.Add(slot);
            _slots[i] = slot;
        }
    }

    private static TextBlock CreatePlaceholder()
    {
        var tb = new TextBlock
        {
            Text = "Arraste blocos aqui",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.3,
            FontSize = 12
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "SubtextBrush");
        return tb;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is TerminalCanvasViewModel oldVm)
            oldVm.Items.CollectionChanged -= OnItemsChanged;

        if (e.NewValue is TerminalCanvasViewModel vm)
        {
            vm.Items.CollectionChanged += OnItemsChanged;

            // Wire the catalog service into the embedded BlockCatalogPanel
            try
            {
                CatalogPanel.CatalogService = App.Services.GetService<IWidgetCatalogService>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BentoLayoutPresenter] CatalogService resolution failed: {ex.Message}");
            }

            RefreshSlots(vm);
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is TerminalCanvasViewModel vm)
            RefreshSlots(vm);
    }

    private void RefreshSlots(TerminalCanvasViewModel vm)
    {
        for (int i = 0; i < BentoSlotMap.SlotCount; i++)
        {
            var item = vm.Items.FirstOrDefault(x => x.BentoSlotIndex == i);
            var slot = _slots[i];

            // Unhook previous occupied-slot drag handler before reassigning Tag
            slot.PreviewMouseLeftButtonDown -= OnOccupiedSlotMouseDown;
            slot.PreviewMouseMove -= OnOccupiedSlotMouseMove;

            if (item is null)
            {
                slot.Child = CreatePlaceholder();
                slot.Background = Brushes.Transparent;
                slot.Tag = i; // plain int tag for empty slot
            }
            else
            {
                var card = new CanvasCardControl
                {
                    DataContext = item,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                slot.Child = card;
                slot.Background = Brushes.Transparent;

                // Store (slotIndex, itemId) tuple as Tag so drag handlers can identify the item
                slot.Tag = (slotIndex: i, itemId: item.Id);
                slot.PreviewMouseLeftButtonDown += OnOccupiedSlotMouseDown;
                slot.PreviewMouseMove += OnOccupiedSlotMouseMove;
            }
        }
    }

    // ─── Occupied-slot drag ──────────────────────────────────────────────────

    private void OnOccupiedSlotMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border) return;
        if (border.Tag is not (int slotIdx, string _)) return;
        _slotDragStarts[slotIdx] = e.GetPosition(null);
    }

    private void OnOccupiedSlotMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _occupiedDragging) return;
        if (sender is not Border border) return;
        if (border.Tag is not (int slotIdx, string itemId)) return;

        if (!_slotDragStarts.TryGetValue(slotIdx, out var start)) return;

        var pos = e.GetPosition(null);
        var diff = start - pos;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _occupiedDragging = true;
        var data = new DataObject("CommandDeck.BentoItemId", itemId);
        DragDrop.DoDragDrop(border, data, DragDropEffects.Move);
        _occupiedDragging = false;
        _slotDragStarts.Remove(slotIdx);
    }

    // ─── Drop target handlers ────────────────────────────────────────────────

    private void OnSlotDragEnter(object sender, DragEventArgs e)
    {
        if (sender is Border b) b.SetResourceReference(Border.BackgroundProperty, "Surface0Brush");
    }

    private void OnSlotDragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border b) b.Background = Brushes.Transparent;
    }

    private void OnSlotDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("CommandDeck.CatalogKey") ||
            e.Data.GetDataPresent("CommandDeck.BentoItemId"))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnSlotDrop(object sender, DragEventArgs e)
    {
        if (sender is not Border border) return;

        // Resolve target slot — tag is either plain int (empty) or (int, string) (occupied)
        int targetSlot = border.Tag switch
        {
            (int idx, string _) => idx,
            int idx             => idx,
            _                   => -1
        };
        if (targetSlot < 0) return;

        if (DataContext is not TerminalCanvasViewModel vm) return;

        IWorkspaceService? workspaceService;
        try
        {
            workspaceService = App.Services.GetService<IWorkspaceService>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BentoLayoutPresenter] WorkspaceService resolution failed: {ex.Message}");
            return;
        }
        if (workspaceService is null) return;

        if (e.Data.GetDataPresent("CommandDeck.CatalogKey"))
        {
            var key = (string)e.Data.GetData("CommandDeck.CatalogKey");

            // Ignore drop if slot is already occupied
            if (vm.Items.Any(i => i.BentoSlotIndex == targetSlot))
            {
                e.Effects = DragDropEffects.None;
            }
            else
            {
                workspaceService.AssignCatalogKeyToBentoSlot(key, targetSlot);
            }
        }
        else if (e.Data.GetDataPresent("CommandDeck.BentoItemId"))
        {
            var itemId = (string)e.Data.GetData("CommandDeck.BentoItemId");
            workspaceService.MoveBentoItem(itemId, targetSlot);
        }

        border.Background = Brushes.Transparent;
        e.Handled = true;
    }
}
