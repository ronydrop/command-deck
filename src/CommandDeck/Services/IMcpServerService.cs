namespace CommandDeck.Services;

/// <summary>
/// Hosts a local JSON-RPC 2.0 / MCP HTTP server that exposes CommandDeck tools
/// to AI agents (e.g. Claude Desktop, Cursor). The server binds to 127.0.0.1
/// on a randomly chosen port in the 47000–47999 range and requires Bearer-token
/// authentication on every tool-call request.
/// </summary>
public interface IMcpServerService : IDisposable
{
    /// <summary>Gets the port the HTTP listener is bound to (valid after <see cref="StartAsync"/>).</summary>
    int Port { get; }

    /// <summary>Gets the random Bearer token clients must supply (valid after <see cref="StartAsync"/>).</summary>
    string Token { get; }

    /// <summary>Gets whether the server is currently listening for requests.</summary>
    bool IsRunning { get; }

    /// <summary>Starts the HTTP listener on a free port and persists the server config to disk.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Stops the HTTP listener and deletes the on-disk server config file.</summary>
    Task StopAsync();

    // ─── Card lifecycle events ────────────────────────────────────────────────

    /// <summary>
    /// Fired when an AI agent calls the <c>card_complete</c> tool.
    /// Arguments: (cardId, summary).
    /// </summary>
    event Action<string, string>? CardCompleted;

    /// <summary>
    /// Fired when an AI agent calls the <c>card_update</c> tool.
    /// Arguments: (cardId, note).
    /// </summary>
    event Action<string, string>? CardUpdated;

    /// <summary>
    /// Fired when an AI agent calls the <c>card_error</c> tool.
    /// Arguments: (cardId, reason).
    /// </summary>
    event Action<string, string>? CardError;
}
