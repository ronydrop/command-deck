using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CommandDeck.Models;

/// <summary>
/// MCP server configuration persisted to %APPDATA%/CommandDeck/mcp-server.json.
/// Allows external tools (e.g. Claude Desktop) to discover the running server.
/// </summary>
public class McpServerConfig
{
    /// <summary>The port the HTTP listener is bound to.</summary>
    public int Port { get; set; }

    /// <summary>Bearer token required for all tool-call requests.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>ISO-8601 timestamp of when the server was started.</summary>
    public string StartedAt { get; set; } = string.Empty;
}

/// <summary>
/// JSON-RPC 2.0 request object.
/// </summary>
public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("method")] public string Method { get; set; } = string.Empty;
    [JsonPropertyName("params")] public JsonElement? Params { get; set; }
    [JsonPropertyName("id")] public JsonElement? Id { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 response object. Exactly one of <see cref="Result"/> or <see cref="Error"/>
/// will be non-null on a valid response.
/// </summary>
public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("result")] public object? Result { get; set; }
    [JsonPropertyName("error")] public JsonRpcError? Error { get; set; }
    [JsonPropertyName("id")] public JsonElement? Id { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 error object embedded in <see cref="JsonRpcResponse.Error"/>.
/// </summary>
public class JsonRpcError
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
}

/// <summary>
/// MCP tool descriptor returned by <c>tools/list</c>.
/// </summary>
public class McpTool
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;

    /// <summary>JSON Schema object describing the tool's input parameters.</summary>
    [JsonPropertyName("inputSchema")] public object InputSchema { get; set; } = new { type = "object", properties = new { } };
}

/// <summary>
/// Result payload returned for a <c>tools/call</c> invocation.
/// </summary>
public class McpToolResult
{
    [JsonPropertyName("content")] public List<McpContent> Content { get; set; } = new();
    [JsonPropertyName("isError")] public bool IsError { get; set; }
}

/// <summary>
/// A single content block inside <see cref="McpToolResult.Content"/>.
/// </summary>
public class McpContent
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
}
