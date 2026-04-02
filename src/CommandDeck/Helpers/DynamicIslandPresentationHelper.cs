using System.Linq;
using CommandDeck.Models;

namespace CommandDeck.Helpers;

/// <summary>
/// Builds consistent presentation copy and semantic metadata for the Dynamic Island.
/// Keeps UI wording rules out of the view layer so the pill and expanded layouts stay aligned.
/// </summary>
public static class DynamicIslandPresentationHelper
{
    public static DynamicIslandEventKind GetEventKind(AiAgentState state) => state switch
    {
        AiAgentState.WaitingUser => DynamicIslandEventKind.Approval,
        AiAgentState.WaitingInput => DynamicIslandEventKind.Question,
        AiAgentState.Error => DynamicIslandEventKind.Error,
        AiAgentState.Completed => DynamicIslandEventKind.Completed,
        AiAgentState.Executing => DynamicIslandEventKind.Execution,
        AiAgentState.Thinking => DynamicIslandEventKind.Activity,
        _ => DynamicIslandEventKind.Activity
    };

    public static DynamicIslandVisualTone GetVisualTone(AiAgentState state, NotificationType severity) => state switch
    {
        AiAgentState.WaitingUser => DynamicIslandVisualTone.Warning,
        AiAgentState.WaitingInput => DynamicIslandVisualTone.Accent,
        AiAgentState.Error => DynamicIslandVisualTone.Danger,
        AiAgentState.Completed => DynamicIslandVisualTone.Success,
        AiAgentState.Executing => DynamicIslandVisualTone.Accent,
        AiAgentState.Thinking => DynamicIslandVisualTone.Neutral,
        _ => severity switch
        {
            NotificationType.Error => DynamicIslandVisualTone.Danger,
            NotificationType.Warning => DynamicIslandVisualTone.Warning,
            NotificationType.Success => DynamicIslandVisualTone.Success,
            _ => DynamicIslandVisualTone.Neutral
        }
    };

    public static string BuildHeadline(string agentLabel, AiAgentState state, string label)
    {
        agentLabel = string.IsNullOrWhiteSpace(agentLabel) ? "AI" : agentLabel.Trim();
        label = Normalize(label);

        return state switch
        {
            AiAgentState.Thinking => $"{agentLabel} pensando",
            AiAgentState.Executing => $"{agentLabel} executando",
            AiAgentState.WaitingUser => $"{agentLabel} aguarda aprovação",
            AiAgentState.WaitingInput => $"{agentLabel} fez uma pergunta",
            AiAgentState.Completed => $"{agentLabel} concluiu",
            AiAgentState.Error => $"{agentLabel} encontrou um erro",
            _ => string.IsNullOrWhiteSpace(label) ? agentLabel : $"{agentLabel} ativo"
        };
    }

    public static string BuildPrimarySnippet(
        AiAgentState state,
        string label,
        string? actionDetail,
        IReadOnlyList<AiAgentChoiceOption> choices)
    {
        label = Normalize(label);
        actionDetail = Normalize(actionDetail);

        return state switch
        {
            AiAgentState.WaitingUser => !string.IsNullOrWhiteSpace(actionDetail)
                ? actionDetail!
                : label,
            AiAgentState.WaitingInput => !string.IsNullOrWhiteSpace(label)
                ? label
                : "Selecione uma opção para continuar.",
            AiAgentState.Executing => !string.IsNullOrWhiteSpace(actionDetail)
                ? actionDetail!
                : label,
            AiAgentState.Completed => !string.IsNullOrWhiteSpace(actionDetail)
                ? actionDetail!
                : (!string.IsNullOrWhiteSpace(label) ? label : "Execução concluída."),
            AiAgentState.Error => !string.IsNullOrWhiteSpace(actionDetail)
                ? actionDetail!
                : (!string.IsNullOrWhiteSpace(label) ? label : "Erro detectado na sessão."),
            _ => !string.IsNullOrWhiteSpace(label)
                ? label
                : (choices.Count > 0 ? $"Opções disponíveis: {choices.Count}" : string.Empty)
        };
    }

    public static string BuildSecondarySnippet(
        AiAgentState state,
        string label,
        string? actionDetail,
        IReadOnlyList<AiAgentChoiceOption> choices)
    {
        label = Normalize(label);
        actionDetail = Normalize(actionDetail);

        return state switch
        {
            AiAgentState.WaitingUser => "Revise e responda sem trocar de contexto.",
            AiAgentState.WaitingInput when choices.Count > 0 => $"Respostas rápidas: {string.Join(" • ", choices.Select(c => Normalize(c.Label)).Where(s => !string.IsNullOrWhiteSpace(s)).Take(3))}",
            AiAgentState.WaitingInput => "A sessão está aguardando sua resposta.",
            AiAgentState.Executing when !string.IsNullOrWhiteSpace(label) && !string.Equals(label, actionDetail, StringComparison.OrdinalIgnoreCase) => label,
            AiAgentState.Completed => "Clique para abrir a sessão e revisar o resultado.",
            AiAgentState.Error => "Abra a sessão para corrigir ou reenviar a ação.",
            AiAgentState.Thinking => "Monitorando a próxima etapa do agente.",
            _ => string.Empty
        };
    }

    public static string BuildCompactBadge(DynamicIslandEventKind kind) => kind switch
    {
        DynamicIslandEventKind.Approval => "Approve",
        DynamicIslandEventKind.Question => "Ask",
        DynamicIslandEventKind.Execution => "Live",
        DynamicIslandEventKind.Completed => "Done",
        DynamicIslandEventKind.Error => "Alert",
        DynamicIslandEventKind.Notification => "Notify",
        _ => "Live"
    };

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var compact = string.Join(" ", value
            .Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return compact.Length > 220 ? compact[..217] + "..." : compact;
    }
}
