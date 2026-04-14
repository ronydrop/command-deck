using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommandDeck.Models;
using CommandDeck.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CommandDeck.Helpers;

/// <summary>
/// Registers all built-in slash commands into <see cref="ISlashCommandService"/>.
/// Call <see cref="RegisterAll"/> once after the DI host is built.
/// </summary>
public static class SlashCommandRegistrar
{
    public static void RegisterAll(IServiceProvider services)
    {
        var svc      = services.GetRequiredService<ISlashCommandService>();
        var registry = services.GetRequiredService<IToolRegistry>();

        // ── /help (alias /?) ───────────────────────────────────────────────
        svc.Register(new SlashCommandDescriptor
        {
            Name        = "help",
            Aliases     = ["?"],
            Description = "Lista todos os comandos disponíveis.",
            Handler     = (ctx, _) =>
            {
                var lines = new StringBuilder("**Comandos disponíveis:**\n");
                foreach (var cmd in svc.Commands)
                {
                    var aliases = cmd.Aliases.Length > 0 ? $" (/{string.Join(", /", cmd.Aliases)})" : string.Empty;
                    lines.AppendLine($"- `/{cmd.Name}`{aliases} — {cmd.Description}");
                }
                return Task.FromResult(new SlashCommandResult { Handled = true, ResponseText = lines.ToString() });
            }
        });

        // ── /clear ─────────────────────────────────────────────────────────
        svc.Register(new SlashCommandDescriptor
        {
            Name        = "clear",
            Description = "Limpa o histórico visual do chat.",
            Handler     = (ctx, _) =>
            {
                ctx.ClearHistory();
                return Task.FromResult(new SlashCommandResult { Handled = true });
            }
        });

        // ── /model <name> ──────────────────────────────────────────────────
        svc.Register(new SlashCommandDescriptor
        {
            Name        = "model",
            Description = "Troca o modelo ativo. Uso: /model <nome>. Sem args: lista modelos disponíveis.",
            Handler     = (ctx, _) =>
            {
                if (string.IsNullOrWhiteSpace(ctx.Args))
                {
                    var list = string.Join("\n", ctx.AvailableModels.Select(m => $"- `{m}`"));
                    return Task.FromResult(new SlashCommandResult
                    {
                        Handled = true,
                        ResponseText = $"**Modelos disponíveis:**\n{list}"
                    });
                }

                var match = ctx.AvailableModels.FirstOrDefault(m =>
                    m.Equals(ctx.Args, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    var list = string.Join(", ", ctx.AvailableModels.Select(m => $"`{m}`"));
                    return Task.FromResult(new SlashCommandResult
                    {
                        Handled = true,
                        ResponseText = $"Modelo `{ctx.Args}` não encontrado. Disponíveis: {list}"
                    });
                }

                ctx.SetModel(match);
                return Task.FromResult(new SlashCommandResult
                {
                    Handled = true,
                    ResponseText = $"Modelo alterado para `{match}`."
                });
            }
        });

        // ── /agent <id-or-name> ────────────────────────────────────────────
        svc.Register(new SlashCommandDescriptor
        {
            Name        = "agent",
            Description = "Troca o agent mode ativo. Uso: /agent <id ou nome>. Sem args: lista agentes.",
            Handler     = (ctx, _) =>
            {
                if (string.IsNullOrWhiteSpace(ctx.Args))
                {
                    var list = string.Join("\n", ctx.AgentModes.Select(a => $"- `{a.Id}` — {a.Icon} {a.Name}"));
                    return Task.FromResult(new SlashCommandResult
                    {
                        Handled = true,
                        ResponseText = $"**Agent modes disponíveis:**\n{list}"
                    });
                }

                var match = ctx.AgentModes.FirstOrDefault(a =>
                    a.Id.Equals(ctx.Args, StringComparison.OrdinalIgnoreCase) ||
                    a.Name.Equals(ctx.Args, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    return Task.FromResult(new SlashCommandResult
                    {
                        Handled = true,
                        ResponseText = $"Agent mode `{ctx.Args}` não encontrado. Use `/agent` para listar."
                    });
                }

                ctx.SetAgent(match);
                return Task.FromResult(new SlashCommandResult
                {
                    Handled = true,
                    ResponseText = $"Agent mode alterado para {match.Icon} **{match.Name}**."
                });
            }
        });

        // ── /tools ─────────────────────────────────────────────────────────
        svc.Register(new SlashCommandDescriptor
        {
            Name        = "tools",
            Description = "Lista as tools disponíveis no registry.",
            Handler     = (ctx, _) =>
            {
                var all = ctx.ToolRegistry.All;
                if (all.Count == 0)
                {
                    return Task.FromResult(new SlashCommandResult
                    {
                        Handled = true,
                        ResponseText = "Nenhuma tool registrada."
                    });
                }
                var list = string.Join("\n", all.Select(t => $"- `{t.Name}` — {t.Description}"));
                return Task.FromResult(new SlashCommandResult
                {
                    Handled = true,
                    ResponseText = $"**Tools disponíveis ({all.Count}):**\n{list}"
                });
            }
        });

        // ── /provider <claude|openai|openrouter|ollama> ────────────────────
        svc.Register(new SlashCommandDescriptor
        {
            Name        = "provider",
            Description = "Troca o provider de IA. Uso: /provider <claude|openai|openrouter|ollama>.",
            Handler     = (ctx, _) =>
            {
                if (string.IsNullOrWhiteSpace(ctx.Args))
                {
                    return Task.FromResult(new SlashCommandResult
                    {
                        Handled = true,
                        ResponseText = "Uso: `/provider <claude|openai|openrouter|ollama>`"
                    });
                }
                ctx.SwitchProvider(ctx.Args);
                return Task.FromResult(new SlashCommandResult
                {
                    Handled = true,
                    ResponseText = $"Provider alterado para `{ctx.Args}`."
                });
            }
        });

        // ── /kanban list [coluna] ──────────────────────────────────────────
        svc.Register(new SlashCommandDescriptor
        {
            Name        = "kanban",
            Description = "Atalho Kanban. Uso: /kanban list [coluna] | /kanban add <título>.",
            Handler     = async (ctx, ct) =>
            {
                var parts = ctx.Args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return new SlashCommandResult
                    {
                        Handled = true,
                        ResponseText = "Uso: `/kanban list [coluna]` ou `/kanban add <título>`"
                    };

                var sub = parts[0].ToLowerInvariant();

                if (sub == "list")
                {
                    var column = parts.Length > 1 ? parts[1] : string.Empty;
                    var inputJson = string.IsNullOrWhiteSpace(column)
                        ? "{}"
                        : JsonSerializer.Serialize(new { column });

                    var call = new ToolCall
                    {
                        Id        = Guid.NewGuid().ToString("N"),
                        Name      = "kanban_list_cards",
                        InputJson = inputJson
                    };
                    var result = await ctx.ToolExec.ExecuteAsync(call, ct).ConfigureAwait(false);
                    return new SlashCommandResult
                    {
                        Handled = true,
                        ResponseText = result.IsError ? $"Erro: {result.Content}" : result.Content
                    };
                }

                if (sub == "add" && parts.Length > 1)
                {
                    var title = parts[1];
                    var call = new ToolCall
                    {
                        Id        = Guid.NewGuid().ToString("N"),
                        Name      = "kanban_create_card",
                        InputJson = JsonSerializer.Serialize(new { title })
                    };
                    var result = await ctx.ToolExec.ExecuteAsync(call, ct).ConfigureAwait(false);
                    return new SlashCommandResult
                    {
                        Handled = true,
                        ResponseText = result.IsError ? $"Erro: {result.Content}" : $"Card criado: **{title}**"
                    };
                }

                return new SlashCommandResult
                {
                    Handled = true,
                    ResponseText = "Uso: `/kanban list [coluna]` ou `/kanban add <título>`"
                };
            }
        });
    }
}
