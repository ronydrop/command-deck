using CommandDeck.ViewModels;

namespace CommandDeck.Helpers;

/// <summary>
/// Routes AI quick-action requests to an existing chat tile on the canvas,
/// or creates a new one if none is available.
/// Replaces the old AssistantPanelView as the central point of AI chat interaction.
/// </summary>
public class ChatTileRouter
{
    private readonly Lazy<TerminalCanvasViewModel> _canvasVm;

    public ChatTileRouter(Lazy<TerminalCanvasViewModel> canvasVm)
    {
        _canvasVm = canvasVm;
    }

    /// <summary>
    /// Finds an existing chat tile or creates a new one, then injects a prompt.
    /// If <paramref name="autoSend"/> is true, the message is sent automatically.
    /// </summary>
    public async Task RouteMessageAsync(string prompt, bool autoSend = false)
    {
        var canvas = _canvasVm.Value;

        // Find the most recently focused chat tile
        var chatTile = canvas.Items
            .OfType<ChatCanvasItemViewModel>()
            .OrderByDescending(t => t.ZIndex)
            .FirstOrDefault();

        if (chatTile is null)
        {
            // No chat tile on canvas — create one
            canvas.AddChatWidget();
            await Task.Delay(200); // Allow the tile to initialize

            chatTile = canvas.Items
                .OfType<ChatCanvasItemViewModel>()
                .OrderByDescending(t => t.ZIndex)
                .FirstOrDefault();
        }

        if (chatTile is null) return;

        // Bring to front and inject prompt
        canvas.BringToFront(chatTile);
        await chatTile.InjectPromptAsync(prompt, autoSend);
    }

    /// <summary>
    /// Routes a pre-built user message to the active chat tile.
    /// Creates a new tile if none exists.
    /// </summary>
    public async Task RouteUserMessageAsync(string message)
        => await RouteMessageAsync(message, autoSend: true);
}
