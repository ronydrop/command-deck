using System.Windows.Controls;

namespace CommandDeck.Controls;

/// <summary>
/// Code-behind for the system resource monitor widget.
/// All state (CPU usage, memory) is bound from <see cref="ViewModels.WidgetCanvasItemViewModel"/>
/// via XAML bindings — no imperative logic required here.
/// </summary>
public partial class SystemMonitorWidgetControl : UserControl
{
    public SystemMonitorWidgetControl()
    {
        InitializeComponent();
    }
}
