using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Dispatches <see cref="ToolCall"/> instances to registered handlers via <see cref="IToolRegistry"/>.
/// Catches all exceptions so the assistant loop never crashes on a bad tool execution.
/// </summary>
public sealed class ToolExecutionService : IToolExecutionService
{
    private readonly IToolRegistry _registry;

    public ToolExecutionService(IToolRegistry registry)
    {
        _registry = registry;
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[ToolExec] Executando tool '{call.Name}' (id={call.Id})");

            JsonElement input;
            try
            {
                input = string.IsNullOrWhiteSpace(call.InputJson)
                    ? JsonDocument.Parse("{}").RootElement
                    : JsonDocument.Parse(call.InputJson).RootElement;
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolExec] InputJson inválido para tool '{call.Name}': {ex.Message}");
                return new ToolResult { ToolCallId = call.Id, Content = $"Input JSON inválido: {ex.Message}", IsError = true };
            }

            var result = await _registry.ExecuteAsync(call.Name, input, ct).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[ToolExec] Tool '{call.Name}' concluída ({result.Length} chars)");
            return new ToolResult { ToolCallId = call.Id, Content = result };
        }
        catch (OperationCanceledException)
        {
            return new ToolResult { ToolCallId = call.Id, Content = "Operação cancelada.", IsError = true };
        }
        catch (KeyNotFoundException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ToolExec] Tool '{call.Name}' não encontrada no registry: {ex.Message}");
            return new ToolResult { ToolCallId = call.Id, Content = ex.Message, IsError = true };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ToolExec] Erro ao executar tool '{call.Name}': {ex}");
            return new ToolResult { ToolCallId = call.Id, Content = $"Erro interno: {ex.Message}", IsError = true };
        }
    }
}
