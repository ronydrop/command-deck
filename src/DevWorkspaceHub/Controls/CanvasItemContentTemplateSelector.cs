using System.Windows;
using System.Windows.Controls;
using DevWorkspaceHub.Models;
using DevWorkspaceHub.ViewModels;

namespace DevWorkspaceHub.Controls;

/// <summary>
/// DataTemplateSelector that chooses the correct content template
/// based on the concrete <see cref="CanvasItemViewModel"/> subtype.
/// Templates are set as properties in XAML on the CanvasCardControl resource.
/// </summary>
public class CanvasItemContentTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TerminalTemplate { get; set; }
    public DataTemplate? GitWidgetTemplate { get; set; }
    public DataTemplate? ProcessWidgetTemplate { get; set; }
    public DataTemplate? ShortcutWidgetTemplate { get; set; }
    public DataTemplate? NoteWidgetTemplate { get; set; }
    public DataTemplate? ImageWidgetTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            TerminalCanvasItemViewModel => TerminalTemplate,
            WidgetCanvasItemViewModel w => w.WidgetType switch
            {
                WidgetType.Git => GitWidgetTemplate,
                WidgetType.Process => ProcessWidgetTemplate,
                WidgetType.Note => NoteWidgetTemplate,
                WidgetType.Image => ImageWidgetTemplate,
                _ => ShortcutWidgetTemplate
            },
            _ => base.SelectTemplate(item, container)
        };
    }
}
