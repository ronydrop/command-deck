using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using DevWorkspaceHub.ViewModels;

namespace DevWorkspaceHub.Controls;

public partial class ImageWidgetControl : UserControl
{
    public ImageWidgetControl()
    {
        InitializeComponent();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;

        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not WidgetCanvasItemViewModel vm) return;

        // Drop file from explorer
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            var imgFile = files?.FirstOrDefault(f => IsImageFile(f));
            if (imgFile is not null)
            {
                vm.ImagePath = imgFile;
                e.Handled = true;
                return;
            }
        }

        // Drop bitmap data
        if (e.Data.GetDataPresent(DataFormats.Bitmap))
        {
            if (e.Data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            {
                vm.SetImageFromBitmap(bitmap);
                e.Handled = true;
            }
        }
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".ico" or ".tiff" or ".tif";
    }
}
