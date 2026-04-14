using System.Threading;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Executes <see cref="ToolCall"/> instances dispatched by the assistant
/// and wraps exceptions into <see cref="ToolResult"/> error results.
/// </summary>
public interface IToolExecutionService
{
    /// <summary>
    /// Executes the given tool call and returns its result.
    /// Never throws — exceptions are captured as <see cref="ToolResult.IsError"/> results.
    /// </summary>
    Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct);
}
