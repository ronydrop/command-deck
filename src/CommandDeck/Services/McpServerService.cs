using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Local MCP (Model Context Protocol) server hosted via <see cref="HttpListener"/>.
/// Listens on a random port in the 47000-47999 range on 127.0.0.1.
/// Exposes CommandDeck tools over JSON-RPC 2.0 to AI agents such as Claude Desktop.
/// </summary>
public sealed class McpServerService : IMcpServerService
{
    // ─── Dependencies ─────────────────────────────────────────────────────────
    private readonly ITerminalService _terminalService;
    private readonly IGitService _gitService;
    private readonly Lazy<IWorkspaceService> _workspaceService;
    private readonly IProjectService _projectService;

    // ─── Server state ─────────────────────────────────────────────────────────
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    private readonly SemaphoreSlim _configFileLock = new(1, 1);

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // ─── IMcpServerService ────────────────────────────────────────────────────
    public int Port { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public bool IsRunning { get; private set; }

    public event Action<string, string>? CardCompleted;
    public event Action<string, string>? CardUpdated;
    public event Action<string, string>? CardError;

    // ─── Constructor ──────────────────────────────────────────────────────────

    public McpServerService(
        ITerminalService terminalService,
        IGitService gitService,
        Lazy<IWorkspaceService> workspaceService,
        IProjectService projectService)
    {
        _terminalService = terminalService;
        _gitService = gitService;
        _workspaceService = workspaceService;
        _projectService = projectService;
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;

        Port = FindFreePort();
        Token = Guid.NewGuid().ToString("N");

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        IsRunning = true;

        await SaveConfigAsync();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);

        Debug.WriteLine($"[McpServer] Listening on http://127.0.0.1:{Port}/ — token={Token[..8]}...");
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        if (!IsRunning) return;

        IsRunning = false;
        _cts?.Cancel();

        try { _listener?.Stop(); } catch { }

        if (_listenTask != null)
            try { await _listenTask.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }

        _listener = null;
        await DeleteConfigAsync();

        Debug.WriteLine("[McpServer] Stopped.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
        _configFileLock.Dispose();
    }

    // ─── HTTP listen loop ─────────────────────────────────────────────────────

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && (_listener?.IsListening ?? false))
        {
            try
            {
                var context = await _listener!.GetContextAsync().WaitAsync(ct);
                _ = HandleRequestAsync(context);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException ex) when (ex.ErrorCode == 995) { break; } // ERROR_OPERATION_ABORTED
            catch (Exception ex)
            {
                Debug.WriteLine($"[McpServer] Listen loop error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.AddHeader("Access-Control-Allow-Origin", "*");

            if (context.Request.HttpMethod == "OPTIONS")
            {
                context.Response.AddHeader("Access-Control-Allow-Headers", "Authorization, Content-Type");
                context.Response.AddHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
                context.Response.StatusCode = 204;
                context.Response.Close();
                return;
            }

            if (context.Request.HttpMethod != "POST")
            {
                context.Response.StatusCode = 405;
                context.Response.Close();
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();

            JsonRpcRequest? request;
            try { request = JsonSerializer.Deserialize<JsonRpcRequest>(body, _json); }
            catch { request = null; }

            if (request is null)
            {
                await WriteResponseAsync(context, new JsonRpcResponse
                {
                    Error = new JsonRpcError { Code = -32700, Message = "Parse error" }
                });
                return;
            }

            var response = await DispatchAsync(request, context.Request);
            await WriteResponseAsync(context, response);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[McpServer] HandleRequest error: {ex}");
            try { context.Response.StatusCode = 500; context.Response.Close(); } catch { }
        }
    }

    private static async Task WriteResponseAsync(HttpListenerContext context, JsonRpcResponse response)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(response, _json);
        context.Response.ContentLength64 = json.Length;
        context.Response.StatusCode = 200;
        await context.Response.OutputStream.WriteAsync(json);
        context.Response.Close();
    }

    // ─── JSON-RPC dispatch ────────────────────────────────────────────────────

    private async Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest request, HttpListenerRequest httpRequest)
    {
        return request.Method switch
        {
            "initialize" => HandleInitialize(request),
            "tools/list" => HandleToolsList(request),
            "tools/call" => await HandleToolsCallAsync(request, httpRequest),
            _ => ErrorResponse(request.Id, -32601, $"Method not found: {request.Method}")
        };
    }

    // ─── initialize ──────────────────────────────────────────────────────────

    private static JsonRpcResponse HandleInitialize(JsonRpcRequest request) => new()
    {
        Id = request.Id,
        Result = new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { tools = new { } },
            serverInfo = new { name = "CommandDeck", version = "1.0.0" }
        }
    };

    // ─── tools/list ──────────────────────────────────────────────────────────

    private static JsonRpcResponse HandleToolsList(JsonRpcRequest request) => new()
    {
        Id = request.Id,
        Result = new
        {
            tools = BuildToolDescriptors()
        }
    };

    private static List<McpTool> BuildToolDescriptors() =>
    [
        new McpTool
        {
            Name = "terminal_list",
            Description = "Lists all active terminal sessions in CommandDeck, returning their IDs and titles.",
            InputSchema = new
            {
                type = "object",
                properties = new { },
                required = Array.Empty<string>()
            }
        },
        new McpTool
        {
            Name = "terminal_send_input",
            Description = "Sends text input to a specific terminal session identified by its session_id.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    session_id = new { type = "string", description = "The terminal session ID to write input to." },
                    input = new { type = "string", description = "The text to send to the terminal." }
                },
                required = new[] { "session_id", "input" }
            }
        },
        new McpTool
        {
            Name = "git_status",
            Description = "Returns the Git status of the active project: branch, ahead/behind, staged/modified/untracked file counts, and last commit info.",
            InputSchema = new
            {
                type = "object",
                properties = new { },
                required = Array.Empty<string>()
            }
        },
        new McpTool
        {
            Name = "workspace_info",
            Description = "Returns the current workspace name, active project name, and number of open terminal sessions.",
            InputSchema = new
            {
                type = "object",
                properties = new { },
                required = Array.Empty<string>()
            }
        },
        new McpTool
        {
            Name = "card_complete",
            Description = "Signals that an AI agent card has completed successfully. Fires the CardCompleted event in CommandDeck.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    card_id = new { type = "string", description = "The ID of the card that completed." },
                    summary = new { type = "string", description = "A short summary of what was accomplished." }
                },
                required = new[] { "card_id", "summary" }
            }
        },
        new McpTool
        {
            Name = "card_update",
            Description = "Sends a progress note for an in-flight AI agent card. Fires the CardUpdated event in CommandDeck.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    card_id = new { type = "string", description = "The ID of the card to update." },
                    note = new { type = "string", description = "The progress note to attach to the card." }
                },
                required = new[] { "card_id", "note" }
            }
        },
        new McpTool
        {
            Name = "card_error",
            Description = "Signals that an AI agent card has encountered an error. Fires the CardError event in CommandDeck.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    card_id = new { type = "string", description = "The ID of the card that errored." },
                    reason = new { type = "string", description = "A description of the error." }
                },
                required = new[] { "card_id", "reason" }
            }
        }
    ];

    // ─── tools/call ──────────────────────────────────────────────────────────

    private async Task<JsonRpcResponse> HandleToolsCallAsync(JsonRpcRequest request, HttpListenerRequest httpRequest)
    {
        // Bearer-token authentication
        var authHeader = httpRequest.Headers["Authorization"] ?? string.Empty;
        var expectedBearer = $"Bearer {Token}";
        if (!string.Equals(authHeader, expectedBearer, StringComparison.Ordinal))
            return ErrorResponse(request.Id, -32001, "Unauthorized: invalid or missing Bearer token.");

        // Extract tool name and arguments
        if (!request.Params.HasValue)
            return ErrorResponse(request.Id, -32602, "Invalid params: missing params object.");

        string? toolName = null;
        JsonElement? toolArgs = null;

        try
        {
            var p = request.Params.Value;
            if (p.TryGetProperty("name", out var nameProp))
                toolName = nameProp.GetString();
            if (p.TryGetProperty("arguments", out var argsProp))
                toolArgs = argsProp;
        }
        catch (Exception ex)
        {
            return ErrorResponse(request.Id, -32602, $"Invalid params: {ex.Message}");
        }

        if (string.IsNullOrEmpty(toolName))
            return ErrorResponse(request.Id, -32602, "Invalid params: 'name' is required.");

        McpToolResult result;
        try
        {
            result = toolName switch
            {
                "terminal_list" => ToolTerminalList(),
                "terminal_send_input" => await ToolTerminalSendInputAsync(toolArgs),
                "git_status" => await ToolGitStatusAsync(),
                "workspace_info" => ToolWorkspaceInfo(),
                "card_complete" => ToolCardComplete(toolArgs),
                "card_update" => ToolCardUpdate(toolArgs),
                "card_error" => ToolCardError(toolArgs),
                _ => ToolError($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[McpServer] Tool '{toolName}' threw: {ex}");
            result = ToolError($"Tool execution failed: {ex.Message}");
        }

        return new JsonRpcResponse { Id = request.Id, Result = result };
    }

    // ─── Tool: terminal_list ──────────────────────────────────────────────────

    private McpToolResult ToolTerminalList()
    {
        var sessions = _terminalService.GetSessions();
        var entries = new List<object>(sessions.Count);
        foreach (var s in sessions)
            entries.Add(new { id = s.Id, title = s.Title, status = s.Status.ToString() });

        var text = entries.Count == 0
            ? "No active terminal sessions."
            : JsonSerializer.Serialize(entries, _json);

        return ToolOk(text);
    }

    // ─── Tool: terminal_send_input ────────────────────────────────────────────

    private async Task<McpToolResult> ToolTerminalSendInputAsync(JsonElement? args)
    {
        var (sessionId, input) = ExtractTwoStrings(args, "session_id", "input");
        if (sessionId is null) return ToolError("Missing required parameter: session_id.");
        if (input is null) return ToolError("Missing required parameter: input.");

        var session = _terminalService.GetSession(sessionId);
        if (session is null) return ToolError($"Terminal session '{sessionId}' not found.");

        await _terminalService.WriteAsync(sessionId, input);
        return ToolOk($"Input sent to session '{sessionId}'.");
    }

    // ─── Tool: git_status ────────────────────────────────────────────────────

    private async Task<McpToolResult> ToolGitStatusAsync()
    {
        // Resolve repository path from the active workspace or first available project.
        string? repoPath = null;

        var workspace = _workspaceService.Value.CurrentWorkspace;
        if (workspace is not null)
        {
            // Try to find a project whose path matches the workspace name, or just use the first project.
            try
            {
                var projects = await _projectService.GetAllProjectsAsync();
                if (projects.Count > 0)
                    repoPath = projects[0].Path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[McpServer] git_status: project lookup failed: {ex.Message}");
            }
        }

        if (string.IsNullOrEmpty(repoPath))
            return ToolError("No project configured. Add a project in CommandDeck first.");

        var gitInfo = await _gitService.GetGitInfoAsync(repoPath);
        if (gitInfo is null)
            return ToolError($"Path '{repoPath}' is not a Git repository or Git is unavailable.");

        var summary = new
        {
            branch = gitInfo.BranchDisplay,
            status = gitInfo.RepoStatus.ToString(),
            staged = gitInfo.StagedFiles,
            modified = gitInfo.ModifiedFiles,
            untracked = gitInfo.UntrackedFiles,
            conflicted = gitInfo.ConflictedFiles,
            ahead = gitInfo.Ahead,
            behind = gitInfo.Behind,
            lastCommit = gitInfo.LastCommitMessage,
            lastCommitHash = gitInfo.LastCommitHash,
            summary = gitInfo.StatusSummary
        };

        return ToolOk(JsonSerializer.Serialize(summary, _json));
    }

    // ─── Tool: workspace_info ─────────────────────────────────────────────────

    private McpToolResult ToolWorkspaceInfo()
    {
        var workspace = _workspaceService.Value.CurrentWorkspace;
        var sessions = _terminalService.GetSessions();

        // Resolve the current project name synchronously via a brief blocking call.
        // GetAllProjectsAsync is a fast in-memory/JSON read; the 2 s timeout is a safety net.
        string projectName = "(none)";
        try
        {
            var projectsTask = _projectService.GetAllProjectsAsync();
            if (projectsTask.Wait(TimeSpan.FromSeconds(2)) && projectsTask.Result.Count > 0)
                projectName = projectsTask.Result[0].Name;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[McpServer] workspace_info: project lookup failed: {ex.Message}");
        }

        var info = new
        {
            workspaceName = workspace?.Name ?? "(none)",
            workspaceId = workspace?.Id ?? string.Empty,
            projectName,
            activeTerminals = sessions.Count
        };

        return ToolOk(JsonSerializer.Serialize(info, _json));
    }

    // ─── Tool: card_complete ──────────────────────────────────────────────────

    private McpToolResult ToolCardComplete(JsonElement? args)
    {
        var (cardId, summary) = ExtractTwoStrings(args, "card_id", "summary");
        if (cardId is null) return ToolError("Missing required parameter: card_id.");
        if (summary is null) return ToolError("Missing required parameter: summary.");

        CardCompleted?.Invoke(cardId, summary);
        return ToolOk($"Card '{cardId}' marked as complete.");
    }

    // ─── Tool: card_update ────────────────────────────────────────────────────

    private McpToolResult ToolCardUpdate(JsonElement? args)
    {
        var (cardId, note) = ExtractTwoStrings(args, "card_id", "note");
        if (cardId is null) return ToolError("Missing required parameter: card_id.");
        if (note is null) return ToolError("Missing required parameter: note.");

        CardUpdated?.Invoke(cardId, note);
        return ToolOk($"Card '{cardId}' updated.");
    }

    // ─── Tool: card_error ─────────────────────────────────────────────────────

    private McpToolResult ToolCardError(JsonElement? args)
    {
        var (cardId, reason) = ExtractTwoStrings(args, "card_id", "reason");
        if (cardId is null) return ToolError("Missing required parameter: card_id.");
        if (reason is null) return ToolError("Missing required parameter: reason.");

        CardError?.Invoke(cardId, reason);
        return ToolOk($"Card '{cardId}' error reported.");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static McpToolResult ToolOk(string text) => new()
    {
        Content = [new McpContent { Type = "text", Text = text }],
        IsError = false
    };

    private static McpToolResult ToolError(string message) => new()
    {
        Content = [new McpContent { Type = "text", Text = message }],
        IsError = true
    };

    private static JsonRpcResponse ErrorResponse(JsonElement? id, int code, string message) => new()
    {
        Id = id,
        Error = new JsonRpcError { Code = code, Message = message }
    };

    /// <summary>Extracts two named string properties from a <see cref="JsonElement"/>.</summary>
    private static (string? first, string? second) ExtractTwoStrings(
        JsonElement? args, string firstName, string secondName)
    {
        if (!args.HasValue) return (null, null);
        var el = args.Value;

        string? first = null, second = null;
        if (el.TryGetProperty(firstName, out var fp)) first = fp.GetString();
        if (el.TryGetProperty(secondName, out var sp)) second = sp.GetString();
        return (first, second);
    }

    // ─── Port discovery ───────────────────────────────────────────────────────

    /// <summary>
    /// Tries ports 47000–47099, returning the first one not already bound.
    /// Throws <see cref="InvalidOperationException"/> if all 100 ports are in use.
    /// </summary>
    private static int FindFreePort()
    {
        const int start = 47000;
        const int maxTries = 100;

        for (var i = 0; i < maxTries; i++)
        {
            var port = start + i;
            try
            {
                var probe = new TcpListener(IPAddress.Loopback, port);
                probe.Start();
                probe.Stop();
                return port;
            }
            catch (SocketException) { /* port in use — try next */ }
        }

        throw new InvalidOperationException(
            $"No free port found in range {start}–{start + maxTries - 1}.");
    }

    // ─── Config file persistence ──────────────────────────────────────────────

    private static string ConfigFilePath =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CommandDeck", "mcp-server.json");

    private async Task SaveConfigAsync()
    {
        await _configFileLock.WaitAsync();
        try
        {
            var dir = System.IO.Path.GetDirectoryName(ConfigFilePath)!;
            Directory.CreateDirectory(dir);

            var config = new McpServerConfig
            {
                Port = Port,
                Token = Token,
                StartedAt = DateTime.UtcNow.ToString("O")
            };

            var json = JsonSerializer.SerializeToUtf8Bytes(config, _json);
            await File.WriteAllBytesAsync(ConfigFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[McpServer] SaveConfig failed: {ex.Message}");
        }
        finally
        {
            _configFileLock.Release();
        }
    }

    private async Task DeleteConfigAsync()
    {
        await _configFileLock.WaitAsync();
        try
        {
            if (File.Exists(ConfigFilePath))
                File.Delete(ConfigFilePath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[McpServer] DeleteConfig failed: {ex.Message}");
        }
        finally
        {
            _configFileLock.Release();
        }
    }
}
