# CommandDeck — Browser AI Context: Recomendações Técnicas Detalhadas

**Versão:** 1.0
**Data:** 31/03/2026
**Autor:** AI Context Integration Engineer + Security Engineer + UX/Product Designer

---

## PARTE 1: AI CONTEXT INTEGRATION

### 1.1 Estrutura do ElementContext Payload

O `ElementCaptureData` existente captura dados brutos do DOM. Precisamos de uma camada intermediária
`ElementContext` que enriquece esses dados para consumo pela IA.

```csharp
// Models/Browser/ElementContext.cs
public sealed class ElementContext
{
    // ─── Identidade ──────────────────────────────────────────────
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;

    // ─── Dados brutos do DOM (vindos do JS) ──────────────────────
    public ElementCaptureData RawCapture { get; init; } = new();

    // ─── Metadados enriquecidos (calculados no C#) ───────────────
    public string PageUrl { get; init; } = string.Empty;
    public string PageTitle { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public string? ProjectPath { get; init; }

    // ─── Code Mapping (elemento → código fonte) ──────────────────
    public CodeMappingResult? CodeMapping { get; init; }

    // ─── Screenshot do elemento (PNG base64, opcional) ───────────
    public string? ScreenshotBase64 { get; init; }

    // ─── Console errors capturados na página ─────────────────────
    public List<string> ConsoleErrors { get; init; } = new();

    // ─── Sanitização aplicada ────────────────────────────────────
    public bool WasSanitized { get; init; }
    public List<string> SanitizationActions { get; init; } = new();
}

public sealed class CodeMappingResult
{
    public string? FilePath { get; init; }
    public int? LineNumber { get; init; }
    public string? ComponentName { get; init; }
    public double Confidence { get; init; } // 0.0 - 1.0
    public CodeMappingStrategy Strategy { get; init; }
    public List<CodeMappingCandidate> Candidates { get; init; } = new();
}

public enum CodeMappingStrategy
{
    ReactFiber,
    DataTestId,
    ClassNameHeuristic,
    FileSearch,
    None
}

public sealed class CodeMappingCandidate
{
    public string FilePath { get; init; } = string.Empty;
    public int? LineNumber { get; init; }
    public double Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;
}
```

### 1.2 Formato Texto (Terminal Agents) vs Formato Estruturado (Assistant Panel)

O mesmo `ElementContext` deve ser formatado de duas formas diferentes dependendo do destino:

**FORMATO TEXTO para Terminal Agents (Claude Code, Codex, Hermes):**

```csharp
// Services/Browser/ElementContextFormatter.cs
public static class ElementContextFormatter
{
    /// <summary>
    /// Formato plain-text para colar em terminais ConPTY.
    /// Sem markdown, sem JSON, sem caracteres especiais que quebrem o terminal.
    /// Delimitadores visuais com ASCII art simples.
    /// </summary>
    public static string FormatForTerminal(ElementContext ctx, AiPromptIntent intent)
    {
        var sb = new StringBuilder();

        // Header com intent
        sb.AppendLine($"== ELEMENT CONTEXT [{IntentToLabel(intent)}] ==");
        sb.AppendLine();

        // Identificação
        sb.AppendLine($"Tag: <{ctx.RawCapture.TagName}>");
        if (!string.IsNullOrEmpty(ctx.RawCapture.Id))
            sb.AppendLine($"ID: #{ctx.RawCapture.Id}");
        if (!string.IsNullOrEmpty(ctx.RawCapture.ClassName))
            sb.AppendLine($"Classes: .{ctx.RawCapture.ClassName.Replace(" ", " .")}");
        sb.AppendLine($"Selector: {ctx.RawCapture.CssSelector}");
        sb.AppendLine($"Page: {ctx.PageUrl}");
        sb.AppendLine();

        // HTML (truncado para não poluir o terminal)
        var html = ctx.RawCapture.OuterHtml ?? ctx.RawCapture.InnerHtml ?? "";
        if (html.Length > 2000) html = html[..2000] + "\n... [truncated]";
        sb.AppendLine("HTML:");
        sb.AppendLine(html);
        sb.AppendLine();

        // Framework info
        if (ctx.RawCapture.FrameworkInfo?.ComponentName != null)
        {
            sb.AppendLine($"Component: {ctx.RawCapture.FrameworkInfo.ComponentName}");
            sb.AppendLine($"Framework: {ctx.RawCapture.FrameworkInfo.Framework}");
        }

        // Code mapping
        if (ctx.CodeMapping != null && ctx.CodeMapping.Confidence > 0.3)
        {
            sb.AppendLine();
            sb.AppendLine($"Probable source file: {ctx.CodeMapping.FilePath}");
            if (ctx.CodeMapping.LineNumber.HasValue)
                sb.AppendLine($"Line: ~{ctx.CodeMapping.LineNumber}");
            sb.AppendLine($"Confidence: {ctx.CodeMapping.Confidence:P0}");
        }

        // Accessibility
        if (ctx.RawCapture.Accessibility != null)
        {
            var a11y = ctx.RawCapture.Accessibility;
            sb.AppendLine();
            sb.AppendLine("Accessibility:");
            if (!string.IsNullOrEmpty(a11y.Role)) sb.AppendLine($"  Role: {a11y.Role}");
            if (!string.IsNullOrEmpty(a11y.AriaLabel)) sb.AppendLine($"  aria-label: {a11y.AriaLabel}");
        }

        // Console errors
        if (ctx.ConsoleErrors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Console Errors:");
            foreach (var err in ctx.ConsoleErrors.Take(5))
                sb.AppendLine($"  ! {err}");
        }

        // Intent-specific instructions
        sb.AppendLine();
        sb.AppendLine(GetIntentInstruction(intent));

        sb.AppendLine("== END ELEMENT CONTEXT ==");
        return sb.ToString();
    }

    private static string IntentToLabel(AiPromptIntent intent) => intent switch
    {
        AiPromptIntent.AnalyzeElement => "ANALYZE",
        AiPromptIntent.FixElementBug => "FIX BUG",
        AiPromptIntent.ImproveElementUX => "IMPROVE UX",
        AiPromptIntent.LocateElementCode => "LOCATE CODE",
        _ => "CONTEXT"
    };

    private static string GetIntentInstruction(AiPromptIntent intent) => intent switch
    {
        AiPromptIntent.AnalyzeElement =>
            "Analyze this HTML element. Explain its purpose, structure, and any issues you notice.",
        AiPromptIntent.FixElementBug =>
            "This element has a bug. Identify the issue and provide the corrected code.",
        AiPromptIntent.ImproveElementUX =>
            "Improve the UX of this element. Suggest better accessibility, layout, and interaction patterns.",
        AiPromptIntent.LocateElementCode =>
            "Find the source code file that defines this component. Use the class names, component name, and data attributes as clues.",
        _ => "Use this element context to assist the developer."
    };
}
```

**FORMATO ESTRUTURADO para Assistant Panel:**

```csharp
/// <summary>
/// Formato rich para o chat panel. Pode incluir seções colapsáveis,
/// syntax highlighting no XAML, e metadata para renderização especial.
/// </summary>
public static ChatMessage FormatForAssistant(ElementContext ctx, AiPromptIntent intent)
{
    var content = new StringBuilder();

    content.AppendLine($"🔍 **Elemento Capturado** — `<{ctx.RawCapture.TagName}>`");
    content.AppendLine();

    if (!string.IsNullOrEmpty(ctx.RawCapture.Id))
        content.AppendLine($"**ID:** `#{ctx.RawCapture.Id}`");
    if (!string.IsNullOrEmpty(ctx.RawCapture.ClassName))
        content.AppendLine($"**Classes:** `{ctx.RawCapture.ClassName}`");
    content.AppendLine($"**Selector:** `{ctx.RawCapture.CssSelector}`");
    content.AppendLine($"**Página:** {ctx.PageUrl}");

    if (ctx.RawCapture.FrameworkInfo?.ComponentName != null)
        content.AppendLine($"**Componente:** `{ctx.RawCapture.FrameworkInfo.ComponentName}` ({ctx.RawCapture.FrameworkInfo.Framework})");

    content.AppendLine();
    content.AppendLine("```html");
    var html = ctx.RawCapture.OuterHtml ?? "";
    if (html.Length > 4000) html = html[..4000] + "\n<!-- truncated -->";
    content.AppendLine(html);
    content.AppendLine("```");

    if (ctx.CodeMapping != null && ctx.CodeMapping.Confidence > 0.3)
    {
        content.AppendLine();
        content.AppendLine($"📁 **Arquivo provável:** `{ctx.CodeMapping.FilePath}`");
        if (ctx.CodeMapping.LineNumber.HasValue)
            content.AppendLine($"**Linha:** ~{ctx.CodeMapping.LineNumber}");
        content.AppendLine($"**Confiança:** {ctx.CodeMapping.Confidence:P0}");
    }

    // Criar ChatMessage com role especial para renderização diferenciada
    return new ChatMessage
    {
        Role = "element-context",  // Role especial (ver seção 1.7)
        Content = content.ToString(),
        Timestamp = DateTime.Now,
        // Metadata extra para renderização no XAML
    };
}
```

**Regras de escaping para terminal:**
- Substituir `\r\n` por `\n` (ConPTY já lida com line endings)
- Escapar sequências ANSI que possam estar no HTML capturado
- Limitar linhas a 120 caracteres (wrap visual, não truncar)
- Nunca enviar caracteres de controle (0x00-0x1F exceto \n e \t)
- Prefixar com newline para não colar no meio de um prompt existente

### 1.3 AiContextRouter — Interface e Routing Logic

```csharp
// Services/Browser/IAiContextRouter.cs
public interface IAiContextRouter
{
    /// <summary>
    /// Routes an ElementContext to the appropriate agent destination.
    /// </summary>
    Task<bool> RouteToAgentAsync(
        ElementContext context,
        AgentTarget target,
        AiPromptIntent intent = AiPromptIntent.AnalyzeElement);

    /// <summary>
    /// Returns available agent targets for the context picker UI.
    /// </summary>
    IReadOnlyList<AgentTarget> GetAvailableTargets();

    /// <summary>
    /// Event raised when context is successfully routed.
    /// </summary>
    event Action<ElementContext, AgentTarget>? ContextRouted;
}

// Services/Browser/AiContextRouter.cs
public sealed class AiContextRouter : IAiContextRouter
{
    private readonly IAgentSelectorService _agentSelector;
    private readonly IAiContextService _aiContext;
    private readonly IAssistantService _assistant;
    private readonly ITerminalService _terminalService;
    private readonly IAiTerminalService _aiTerminalService;
    private readonly ITerminalSessionService _sessionService;
    private readonly INotificationService _notification;
    private readonly IPersistenceService _persistence;

    public event Action<ElementContext, AgentTarget>? ContextRouted;

    public AiContextRouter(
        IAgentSelectorService agentSelector,
        IAiContextService aiContext,
        IAssistantService assistant,
        ITerminalService terminalService,
        IAiTerminalService aiTerminalService,
        ITerminalSessionService sessionService,
        INotificationService notification,
        IPersistenceService persistence)
    {
        _agentSelector = agentSelector;
        _aiContext = aiContext;
        _assistant = assistant;
        _terminalService = terminalService;
        _aiTerminalService = aiTerminalService;
        _sessionService = sessionService;
        _notification = notification;
        _persistence = persistence;
    }

    public async Task<bool> RouteToAgentAsync(
        ElementContext context,
        AgentTarget target,
        AiPromptIntent intent = AiPromptIntent.AnalyzeElement)
    {
        try
        {
            bool success = target.Type switch
            {
                AgentTargetType.Assistant => await RouteToAssistantAsync(context, intent),
                AgentTargetType.Terminal => await RouteToTerminalAsync(context, target, intent),
                _ => false
            };

            if (success)
            {
                // Persist to history
                await PersistSelectionAsync(context, target, intent);

                // Fire event for UI updates
                ContextRouted?.Invoke(context, target);

                _notification.Notify(
                    $"Contexto enviado para {target.DisplayName}",
                    NotificationType.Success,
                    NotificationSource.AI);
            }

            return success;
        }
        catch (Exception ex)
        {
            _notification.Notify(
                "Erro ao enviar contexto",
                NotificationType.Error,
                NotificationSource.AI,
                message: ex.Message);
            return false;
        }
    }

    public IReadOnlyList<AgentTarget> GetAvailableTargets()
    {
        var targets = new List<AgentTarget>();

        // 1. Assistant Panel (sempre disponível)
        targets.Add(new AgentTarget
        {
            Type = AgentTargetType.Assistant,
            DisplayName = "Assistant Panel",
            AgentType = "assistant"
        });

        // 2. Agent ativo atual
        var active = _agentSelector.ActiveAgent;
        if (active != null)
        {
            // Buscar sessão terminal do agent ativo
            var aiSessions = _aiTerminalService.GetActiveAiSessions();
            var matchingSession = aiSessions.FirstOrDefault(
                s => s.AiSessionType == active.SessionType);

            targets.Add(new AgentTarget
            {
                Type = AgentTargetType.Terminal,
                DisplayName = $"{active.Name} (Ativo)",
                AgentType = active.Id,
                TerminalSessionId = matchingSession?.Id
            });
        }

        // 3. Todos os AI terminals ativos
        foreach (var session in _aiTerminalService.GetActiveAiSessions())
        {
            if (targets.Any(t => t.TerminalSessionId == session.Id))
                continue;

            targets.Add(new AgentTarget
            {
                Type = AgentTargetType.Terminal,
                DisplayName = $"{session.AiSessionType} — {session.Id[..8]}",
                AgentType = session.AiSessionType.ToString(),
                TerminalSessionId = session.Id
            });
        }

        return targets;
    }

    private async Task<bool> RouteToAssistantAsync(
        ElementContext context, AiPromptIntent intent)
    {
        // Formato rico para o chat panel
        var message = ElementContextFormatter.FormatForAssistant(context, intent);

        // Dispara via messenger/event para o AssistantPanelViewModel
        // (ver seção 1.7 para o mecanismo exato)
        WeakReferenceMessenger.Default.Send(
            new ElementContextMessage(context, intent, message));

        return true;
    }

    private async Task<bool> RouteToTerminalAsync(
        ElementContext context, AgentTarget target, AiPromptIntent intent)
    {
        string? sessionId = target.TerminalSessionId;

        // Se não tem sessão, criar uma nova para o agent
        if (string.IsNullOrEmpty(sessionId))
        {
            var agent = _agentSelector.Agents.FirstOrDefault(
                a => a.Id == target.AgentType);
            if (agent == null) return false;

            var session = await _terminalService.CreateSessionAsync(ShellType.PowerShell);
            sessionId = session.Id;
            await _aiTerminalService.InjectAiCommandAsync(
                sessionId, agent.SessionType, agent.ModelOrAlias);
        }

        // Formato texto para o terminal
        var text = ElementContextFormatter.FormatForTerminal(context, intent);

        // Injetar no terminal com prefixo newline
        await _terminalService.WriteAsync(sessionId, "\n" + text + "\n");

        return true;
    }

    private async Task PersistSelectionAsync(
        ElementContext context, AgentTarget target, AiPromptIntent intent)
    {
        // Ver seção 1.8 para schema SQLite
        await _persistence.SaveSettingAsync(
            $"element_selection_{context.Id}",
            new ElementSelectionRecord
            {
                ContextId = context.Id,
                TagName = context.RawCapture.TagName,
                CssSelector = context.RawCapture.CssSelector,
                PageUrl = context.PageUrl,
                TargetAgent = target.DisplayName,
                Intent = intent.ToString(),
                CapturedAt = context.CapturedAt
            });
    }
}
```

### 1.4 Integração com IAiContextService Existente

O `IAiContextService` existente foca em contexto de **terminal**. Para suportar contexto de
**browser/element**, devemos estender a interface sem quebrar o contrato existente:

```csharp
// Extensão da interface existente
public interface IAiContextService
{
    // ─── Métodos existentes (não alterar) ─────────────────────────
    Task<AiTerminalContext?> GetActiveTerminalContextAsync();
    Task<AiTerminalContext?> GetTerminalContextAsync(string sessionId);
    Task<string> BuildPromptAsync(AiPromptIntent intent, AiTerminalContext? context = null, int outputLines = 40);
    string GetRecentOutput(string sessionId, int lines = 40);

    // ─── Novos métodos para Element Context ──────────────────────
    Task<string> BuildElementPromptAsync(
        AiPromptIntent intent,
        ElementContext elementContext,
        AiTerminalContext? terminalContext = null);

    ElementContext? LastCapturedElement { get; }
}
```

**Implementação dos novos métodos no AiContextService:**

```csharp
// Adicionar ao AiContextService.cs existente

public ElementContext? LastCapturedElement { get; private set; }

public async Task<string> BuildElementPromptAsync(
    AiPromptIntent intent,
    ElementContext elementContext,
    AiTerminalContext? terminalContext = null)
{
    LastCapturedElement = elementContext;

    var sb = new StringBuilder();

    // Combinar contexto de terminal (se disponível) com contexto de elemento
    if (terminalContext != null)
    {
        sb.AppendLine($"[Terminal Context] {terminalContext.FormatForPrompt()}");
        sb.AppendLine();
    }

    // Contexto do elemento formatado para o intent
    sb.AppendLine(ElementContextFormatter.FormatForTerminal(elementContext, intent));

    return sb.ToString();
}
```

### 1.5 Novos AiPromptIntent Values

```csharp
// Estender o enum existente em Models/AiTerminalContext.cs
public enum AiPromptIntent
{
    // ─── Existentes ──────────────────────────────────────────────
    FixError,
    ExplainOutput,
    SuggestCommand,
    GeneralQuestion,
    SendContext,

    // ─── Novos para Browser Element Context ──────────────────────
    AnalyzeElement,       // Analisar estrutura, propósito, issues do elemento
    FixElementBug,        // Identificar e corrigir bug no elemento
    ImproveElementUX,     // Sugestões de UX, a11y, layout
    LocateElementCode,    // Encontrar arquivo fonte do componente
}
```

**Quando usar cada intent:**

| Intent | Trigger | Prompt Focus |
|---|---|---|
| AnalyzeElement | Default após seleção | "O que é este elemento, como funciona, o que pode melhorar?" |
| FixElementBug | Botão "Fix Bug" no picker | "Este elemento tem um bug, corrija o código" |
| ImproveElementUX | Botão "Melhorar UX" | "Melhore a11y, responsive design, visual hierarchy" |
| LocateElementCode | Botão "Localizar Código" | "Encontre o arquivo fonte com base nas pistas do DOM" |

### 1.6 Injeção de Contexto em Terminais via TerminalService.WriteAsync

**Regras críticas de formatting e escaping:**

```csharp
public static class TerminalContextInjector
{
    /// <summary>
    /// Sanitiza e injeta contexto de elemento em um terminal ConPTY.
    /// </summary>
    public static async Task InjectAsync(
        ITerminalService terminalService,
        string sessionId,
        string contextText)
    {
        // 1. Sanitizar para terminal
        var sanitized = SanitizeForTerminal(contextText);

        // 2. Verificar tamanho (terminais têm buffer limitado)
        if (sanitized.Length > 8000)
        {
            sanitized = sanitized[..8000] + "\n... [context truncated at 8KB]";
        }

        // 3. Injetar com delimitadores claros
        //    Newline antes para não colar no prompt existente
        await terminalService.WriteAsync(sessionId, "\n");

        // 4. Enviar em chunks para não sobrecarregar o PTY buffer
        const int chunkSize = 1024;
        for (int i = 0; i < sanitized.Length; i += chunkSize)
        {
            var chunk = sanitized.Substring(i, Math.Min(chunkSize, sanitized.Length - i));
            await terminalService.WriteAsync(sessionId, chunk);
            await Task.Delay(10); // Dar tempo ao ConPTY processar
        }

        await terminalService.WriteAsync(sessionId, "\n");
    }

    private static string SanitizeForTerminal(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                // Permitir: printable ASCII, newline, tab
                case '\n' or '\t':
                    sb.Append(c);
                    break;

                // Bloquear: ESC (ANSI escape), BEL, etc.
                case '\x1b' or '\x07' or '\x00':
                    break;

                // Bloquear: outros control characters
                case < ' ' when c != '\n' && c != '\t':
                    break;

                // Permitir: tudo acima de space (incluindo Unicode)
                default:
                    sb.Append(c);
                    break;
            }
        }

        // Normalizar line endings
        return sb.ToString().Replace("\r\n", "\n").Replace("\r", "\n");
    }
}
```

**Para Claude Code / Codex especificamente:**

O contexto deve ser colado de forma que o agent CLI reconheça como input do usuário.
Como esses CLIs lêem de stdin, o texto colado será interpretado como prompt/mensagem
do usuário. O formato plain-text com delimitadores `==` funciona bem porque:
- Não tem markdown que geraria formatação indesejada
- Delimitadores claros para o LLM entender o início/fim do contexto
- Sem caracteres especiais que possam ser interpretados como comandos

### 1.7 Envio de Contexto para AssistantPanelViewModel

**Usar CommunityToolkit.Mvvm Messenger (já no projeto):**

```csharp
// Models/Messages/ElementContextMessage.cs
public sealed class ElementContextMessage
{
    public ElementContext Context { get; init; }
    public AiPromptIntent Intent { get; init; }
    public ChatMessage FormattedMessage { get; init; }

    public ElementContextMessage(
        ElementContext context,
        AiPromptIntent intent,
        ChatMessage formattedMessage)
    {
        Context = context;
        Intent = intent;
        FormattedMessage = formattedMessage;
    }
}
```

**Modificações no AssistantPanelViewModel:**

```csharp
// No construtor, registrar handler do messenger:
WeakReferenceMessenger.Default.Register<ElementContextMessage>(this, (r, m) =>
{
    var vm = (AssistantPanelViewModel)r;
    Application.Current.Dispatcher.Invoke(() =>
    {
        vm.ReceiveElementContext(m);
    });
});

// Novo método:
public void ReceiveElementContext(ElementContextMessage message)
{
    // 1. Adicionar mensagem de contexto com role especial
    Messages.Add(message.FormattedMessage);

    // 2. Opcional: auto-gerar resposta da IA sobre o contexto
    // se o provider estiver disponível
    if (IsProviderAvailable)
    {
        // Pré-preencher o input com instrução baseada no intent
        InputText = message.Intent switch
        {
            AiPromptIntent.AnalyzeElement => "Analise este elemento e identifique possíveis melhorias.",
            AiPromptIntent.FixElementBug => "Identifique o bug neste elemento e forneça o código corrigido.",
            AiPromptIntent.ImproveElementUX => "Como posso melhorar a UX e acessibilidade deste elemento?",
            AiPromptIntent.LocateElementCode => "Em qual arquivo do projeto este componente é definido?",
            _ => ""
        };
    }

    // 3. Notificação visual
    StatusText = $"Contexto de <{message.Context.RawCapture.TagName}> recebido";
}
```

**Novo tipo de ChatMessage para renderização especial no XAML:**

Estender o `ChatMessage.Role` para incluir `"element-context"` como valor válido.
No XAML do chat, usar DataTemplateSelector para renderizar esse tipo diferentemente:

```csharp
// Adicionar ao ChatMessage.cs
public bool IsElementContext => Role == "element-context";

// Factory
public static ChatMessage FromElementContext(string content) =>
    new() { Role = "element-context", Content = content };
```

No XAML, a mensagem de element-context deve ter:
- Background diferenciado (Surface1 do Catppuccin em vez de Surface0)
- Ícone 🔍 no header
- Seção de HTML com syntax highlighting (ou pelo menos monospace font)
- Botões inline: "Re-enviar para Terminal", "Copiar HTML", "Ver Screenshot"

### 1.8 Histórico de Seleções — Modelo de Dados e Persistência SQLite

**Schema SQLite (nova migration):**

```sql
-- Migration: 2.1.0 - Element Selection History
CREATE TABLE IF NOT EXISTS element_selections (
    id              TEXT PRIMARY KEY,
    tag_name        TEXT NOT NULL,
    element_id      TEXT,
    class_name      TEXT,
    css_selector    TEXT NOT NULL,
    page_url        TEXT NOT NULL,
    page_title      TEXT,
    project_id      TEXT,
    outer_html      TEXT,              -- Armazenar HTML sanitizado
    screenshot_b64  TEXT,              -- PNG base64 (opcional, pode ser grande)
    component_name  TEXT,              -- React/Vue component name
    code_file_path  TEXT,              -- Code mapping result
    target_agent    TEXT NOT NULL,      -- Para qual agent foi enviado
    intent          TEXT NOT NULL,      -- AiPromptIntent usado
    captured_at     TEXT NOT NULL,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    -- Limitar tamanho do outer_html
    CHECK(length(outer_html) <= 50000)
);

CREATE INDEX idx_element_selections_captured_at ON element_selections(captured_at DESC);
CREATE INDEX idx_element_selections_project_id ON element_selections(project_id);
CREATE INDEX idx_element_selections_page_url ON element_selections(page_url);
```

**Modelo C#:**

```csharp
// Models/Browser/ElementSelectionRecord.cs
public sealed class ElementSelectionRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string TagName { get; init; } = string.Empty;
    public string? ElementId { get; init; }
    public string? ClassName { get; init; }
    public string CssSelector { get; init; } = string.Empty;
    public string PageUrl { get; init; } = string.Empty;
    public string? PageTitle { get; init; }
    public string? ProjectId { get; init; }
    public string? OuterHtml { get; init; }
    public string? ScreenshotBase64 { get; init; }
    public string? ComponentName { get; init; }
    public string? CodeFilePath { get; init; }
    public string TargetAgent { get; init; } = string.Empty;
    public string Intent { get; init; } = string.Empty;
    public DateTime CapturedAt { get; init; }
}
```

**Extensão do IPersistenceService:**

```csharp
// Adicionar à interface IPersistenceService
Task SaveElementSelectionAsync(ElementSelectionRecord record);
Task<IReadOnlyList<ElementSelectionRecord>> ListElementSelectionsAsync(
    string? projectId = null, int limit = 50);
Task<ElementSelectionRecord?> LoadElementSelectionAsync(string id);
Task<int> DeleteOldElementSelectionsAsync(TimeSpan maxAge);
```

**UI do histórico:**
- Lista vertical no painel lateral do browser
- Cada item mostra: tag + selector + timestamp + agent alvo
- Click para expandir: ver HTML, screenshot thumbnail
- Botão "Re-enviar" para reuso (ver seção 1.9)
- Auto-limpeza: manter últimos 100 registros, purgar >30 dias

### 1.9 Reuso de Contexto Anterior

```csharp
// No AiContextRouter, adicionar:
public async Task<bool> ResendContextAsync(
    string selectionId,
    AgentTarget newTarget,
    AiPromptIntent? newIntent = null)
{
    var record = await _persistence.LoadElementSelectionAsync(selectionId);
    if (record == null) return false;

    // Reconstruir ElementContext a partir do record
    var context = new ElementContext
    {
        Id = Guid.NewGuid().ToString(), // Novo ID para o re-envio
        CapturedAt = DateTime.UtcNow,
        PageUrl = record.PageUrl,
        PageTitle = record.PageTitle ?? "",
        ProjectId = record.ProjectId,
        RawCapture = new ElementCaptureData
        {
            TagName = record.TagName,
            Id = record.ElementId,
            ClassName = record.ClassName,
            CssSelector = record.CssSelector,
            OuterHtml = record.OuterHtml,
            Url = record.PageUrl
        },
        ScreenshotBase64 = record.ScreenshotBase64,
        CodeMapping = record.CodeFilePath != null ? new CodeMappingResult
        {
            FilePath = record.CodeFilePath,
            ComponentName = record.ComponentName,
            Confidence = 0.5, // Reduced confidence for re-sent
            Strategy = CodeMappingStrategy.None
        } : null
    };

    var intent = newIntent
        ?? Enum.Parse<AiPromptIntent>(record.Intent, ignoreCase: true);

    return await RouteToAgentAsync(context, newTarget, intent);
}
```

---

## PARTE 2: SEGURANÇA

### 2.1 Sanitização de HTML Capturado

```csharp
// Services/Browser/ElementSanitizer.cs
public static class ElementSanitizer
{
    private static readonly HashSet<string> SensitiveAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "data-token", "data-api-key", "data-secret", "data-auth",
        "data-session", "data-csrf", "data-nonce",
        "authorization", "x-api-key", "x-auth-token",
        "data-password", "data-credential"
    };

    private static readonly Regex TokenPattern = new(
        @"(eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,})" + // JWT
        @"|([A-Fa-f0-9]{32,})" +                           // Hex tokens
        @"|(sk-[A-Za-z0-9]{20,})" +                        // OpenAI keys
        @"|(ghp_[A-Za-z0-9]{36})" +                        // GitHub PATs
        @"|(Bearer\s+\S+)",                                 // Bearer tokens
        RegexOptions.Compiled);

    /// <summary>
    /// Sanitizes captured ElementCaptureData before sending to AI.
    /// Returns a new instance with sensitive data removed.
    /// </summary>
    public static (ElementCaptureData Sanitized, List<string> Actions) Sanitize(
        ElementCaptureData raw)
    {
        var actions = new List<string>();
        var sanitized = CloneCapture(raw);

        // 1. NEVER capture password field values
        SanitizePasswordFields(sanitized, actions);

        // 2. Remove sensitive attributes
        SanitizeSensitiveAttributes(sanitized, actions);

        // 3. Scan and redact tokens in all string fields
        SanitizeTokensInContent(sanitized, actions);

        // 4. Strip inline event handlers that might contain secrets
        SanitizeEventHandlers(sanitized, actions);

        // 5. Limit HTML size to prevent abuse
        TruncateHtml(sanitized, actions);

        // 6. Remove script tags from captured HTML
        SanitizeScriptTags(sanitized, actions);

        return (sanitized, actions);
    }

    private static void SanitizePasswordFields(
        ElementCaptureData data, List<string> actions)
    {
        // Check if this element IS a password field
        if (data.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase))
        {
            if (data.Attributes.TryGetValue("type", out var type) &&
                type.Equals("password", StringComparison.OrdinalIgnoreCase))
            {
                // Remove value entirely
                data.Attributes.Remove("value");
                data.TextContent = "[PASSWORD REDACTED]";
                data.InnerText = "[PASSWORD REDACTED]";
                actions.Add("Redacted password input value");
            }
        }

        // Also scan innerHTML/outerHTML for nested password inputs
        if (data.InnerHtml != null)
        {
            data.InnerHtml = RedactPasswordValues(data.InnerHtml);
            if (data.InnerHtml != raw_InnerHtml) // Changed
                actions.Add("Redacted nested password input values in innerHTML");
        }

        if (data.OuterHtml != null)
        {
            data.OuterHtml = RedactPasswordValues(data.OuterHtml);
        }
    }

    private static string RedactPasswordValues(string html)
    {
        // Regex to find <input type="password" ... value="xxx">
        // and replace the value attribute
        return Regex.Replace(html,
            @"(<input[^>]*type\s*=\s*[""']password[""'][^>]*)\bvalue\s*=\s*[""'][^""']*[""']",
            "$1value=\"[REDACTED]\"",
            RegexOptions.IgnoreCase);
    }

    private static void SanitizeSensitiveAttributes(
        ElementCaptureData data, List<string> actions)
    {
        var keysToRemove = data.Attributes.Keys
            .Where(k => SensitiveAttributes.Contains(k) ||
                        k.StartsWith("data-secret", StringComparison.OrdinalIgnoreCase) ||
                        k.StartsWith("data-key", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in keysToRemove)
        {
            data.Attributes[key] = "[REDACTED]";
            actions.Add($"Redacted attribute: {key}");
        }
    }

    private static void SanitizeTokensInContent(
        ElementCaptureData data, List<string> actions)
    {
        // Scan all text fields for token patterns
        data.OuterHtml = RedactTokens(data.OuterHtml, "outerHTML", actions);
        data.InnerHtml = RedactTokens(data.InnerHtml, "innerHTML", actions);
        data.TextContent = RedactTokens(data.TextContent, "textContent", actions);

        // Scan attribute values
        foreach (var key in data.Attributes.Keys.ToList())
        {
            var val = data.Attributes[key];
            var redacted = TokenPattern.Replace(val, "[TOKEN_REDACTED]");
            if (redacted != val)
            {
                data.Attributes[key] = redacted;
                actions.Add($"Redacted token in attribute: {key}");
            }
        }
    }

    private static string? RedactTokens(string? text, string field, List<string> actions)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var result = TokenPattern.Replace(text, "[TOKEN_REDACTED]");
        if (result != text)
            actions.Add($"Redacted token pattern in {field}");
        return result;
    }

    private static void SanitizeEventHandlers(
        ElementCaptureData data, List<string> actions)
    {
        var eventAttrs = data.Attributes.Keys
            .Where(k => k.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var attr in eventAttrs)
        {
            data.Attributes[attr] = "[EVENT_HANDLER_REMOVED]";
            actions.Add($"Removed inline event handler: {attr}");
        }
    }

    private static void TruncateHtml(
        ElementCaptureData data, List<string> actions)
    {
        const int maxHtmlLength = 50_000; // 50KB max

        if (data.OuterHtml?.Length > maxHtmlLength)
        {
            data.OuterHtml = data.OuterHtml[..maxHtmlLength] + "\n<!-- TRUNCATED FOR SAFETY -->";
            actions.Add($"Truncated outerHTML to {maxHtmlLength} chars");
        }

        if (data.InnerHtml?.Length > maxHtmlLength)
        {
            data.InnerHtml = data.InnerHtml[..maxHtmlLength] + "\n<!-- TRUNCATED -->";
            actions.Add($"Truncated innerHTML to {maxHtmlLength} chars");
        }
    }

    private static void SanitizeScriptTags(
        ElementCaptureData data, List<string> actions)
    {
        if (data.OuterHtml != null)
        {
            var cleaned = Regex.Replace(data.OuterHtml,
                @"<script\b[^>]*>[\s\S]*?</script>",
                "<!-- SCRIPT_REMOVED -->",
                RegexOptions.IgnoreCase);
            if (cleaned != data.OuterHtml)
            {
                data.OuterHtml = cleaned;
                actions.Add("Removed <script> tags from outerHTML");
            }
        }

        if (data.InnerHtml != null)
        {
            var cleaned = Regex.Replace(data.InnerHtml,
                @"<script\b[^>]*>[\s\S]*?</script>",
                "<!-- SCRIPT_REMOVED -->",
                RegexOptions.IgnoreCase);
            if (cleaned != data.InnerHtml)
            {
                data.InnerHtml = cleaned;
                actions.Add("Removed <script> tags from innerHTML");
            }
        }
    }
}
```

### 2.2 Rate Limiting no Picker

```csharp
// Services/Browser/WebMessageRateLimiter.cs
public sealed class WebMessageRateLimiter
{
    private readonly int _maxMessagesPerWindow;
    private readonly TimeSpan _window;
    private readonly Queue<DateTime> _timestamps = new();
    private readonly object _lock = new();

    // Default: max 10 messages per 5 seconds
    public WebMessageRateLimiter(int maxPerWindow = 10, int windowSeconds = 5)
    {
        _maxMessagesPerWindow = maxPerWindow;
        _window = TimeSpan.FromSeconds(windowSeconds);
    }

    /// <summary>
    /// Returns true if the message should be allowed, false if rate-limited.
    /// </summary>
    public bool TryAcquire()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var cutoff = now - _window;

            // Remove expired timestamps
            while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                _timestamps.Dequeue();

            if (_timestamps.Count >= _maxMessagesPerWindow)
                return false;

            _timestamps.Enqueue(now);
            return true;
        }
    }
}

// Uso no BrowserRuntimeService:
private readonly WebMessageRateLimiter _rateLimiter = new();

// No handler de WebMessageReceived:
_webView.CoreWebView2.WebMessageReceived += (s, e) =>
{
    if (!_rateLimiter.TryAcquire())
    {
        Debug.WriteLine("[Browser] WebMessage rate-limited");
        return;
    }
    WebMessageReceived?.Invoke(e.WebMessageAsJson);
};
```

### 2.3 Content Security Policy — Implicações do JS Injetado

**O JS do picker é injetado via `ExecuteScriptAsync()`, que roda no contexto da página.**

Implicações CSP:
- `ExecuteScriptAsync` **ignora CSP** — o script roda independente de `script-src`
  porque é injetado pelo host (browser process), não pela página
- Isso é by-design no WebView2: o host tem privilégio total
- O JS injetado NÃO precisa de `unsafe-inline` ou `unsafe-eval` no CSP da página
- No entanto, se o JS injetado tentar criar `<style>` tags (para o overlay),
  isso PODE ser bloqueado por `style-src` restritivo

**Mitigação para style injection:**
```javascript
// Em vez de criar <style> tags (que podem ser bloqueadas por CSP),
// usar inline styles diretamente nos elementos de overlay:
overlay.style.cssText = `
    position: fixed;
    border: 2px solid #89b4fa;
    background: rgba(137, 180, 250, 0.15);
    pointer-events: none;
    z-index: 2147483647;
    transition: all 0.15s ease;
`;
// Inline styles NÃO são afetados por style-src CSP quando injetados via ExecuteScriptAsync
```

### 2.4 Isolamento WebView2 Process vs WPF Host

**O que o WebView2 pode acessar do host:**
- ❌ Filesystem do host: NÃO (sandbox do Chromium)
- ❌ Memória do WPF process: NÃO (processos separados)
- ❌ HostObjects: DESABILITADOS (`AreHostObjectsAllowed = false` — já configurado)
- ✅ WebMessage API: SIM (canal bidirecional controlado)
- ❌ Clipboard do host: NÃO diretamente (controlado por settings)
- ❌ Hardware: limitado pelo Chromium sandbox

**O que a página pode tentar fazer:**
- `window.chrome.webview.postMessage()` — qualquer página localhost pode enviar WebMessages
  → Mitigação: sempre validar o JSON recebido, nunca confiar no conteúdo
- Tentar escapar do sandbox — extremamente difícil no Chromium
- Keylogging via JS — possível dentro da WebView, mas não captura input do WPF
- Abrir popups — controlado via `NewWindowRequested` event

**Configuração de segurança recomendada (já parcialmente implementada):**

```csharp
private void ConfigureSecurity()
{
    var settings = _webView.CoreWebView2.Settings;

    // Já configurados:
    settings.AreHostObjectsAllowed = false;       // ✅ Nenhum objeto host exposto
    settings.IsWebMessageEnabled = true;          // ✅ Necessário para picker
    settings.IsPasswordAutosaveEnabled = false;   // ✅ Nunca salvar passwords
    settings.IsGeneralAutofillEnabled = false;     // ✅ Sem autofill

    // Adicionar:
    settings.AreBrowserAcceleratorKeysEnabled = false;  // Bloquear Ctrl+O, Ctrl+S, etc.
    settings.IsBuiltInErrorPageEnabled = false;         // Sem páginas de erro padrão
    settings.IsSwipeNavigationEnabled = false;          // Sem navegação por swipe
    settings.IsPinchZoomEnabled = false;                // Zoom controlado pelo app
    settings.IsReputationCheckingRequired = true;       // SmartScreen ativo

    // Bloquear navegação para fora de localhost (já implementado)
    // Bloquear new windows
    _webView.CoreWebView2.NewWindowRequested += (s, e) =>
    {
        e.Handled = true; // Bloquear todas as popups
    };

    // Bloquear downloads
    _webView.CoreWebView2.DownloadStarting += (s, e) =>
    {
        e.Cancel = true;
    };
}
```

### 2.5 Riscos de Projeto Local Malicioso

**Cenário:** Um projeto clonado de repo público contém JS que tenta explorar o picker.

**Vetores de ataque:**
1. **Fake WebMessages:** A página pode enviar `postMessage` com payloads falsos
   → Mitigação: validar schema do JSON, rejeitar campos inesperados
2. **Exfiltrar dados do picker:** A página monitora DOM mutations para detectar overlay
   → Mitigação: o picker só revela informação que a página já conhece (seu próprio DOM)
3. **Denial of Service:** Spam de WebMessages ou DOM muito grande
   → Mitigação: rate limiter (seção 2.2) + truncamento de HTML (seção 2.1)
4. **XSS via picker context:** HTML malicioso capturado é enviado para AI → AI gera código malicioso
   → Mitigação: sanitização (seção 2.1), mas alertar que a AI pode ser enganada

**Validação de WebMessage:**

```csharp
private ElementCaptureData? ValidateWebMessage(string json)
{
    try
    {
        var data = JsonSerializer.Deserialize<ElementCaptureData>(json);
        if (data == null) return null;

        // Validações básicas
        if (string.IsNullOrEmpty(data.TagName)) return null;
        if (data.TagName.Length > 50) return null; // Tag names HTML são curtos
        if (data.CssSelector.Length > 1000) return null;
        if (data.Attributes.Count > 100) return null;
        if (data.OuterHtml?.Length > 100_000) return null; // 100KB max

        return data;
    }
    catch (JsonException)
    {
        return null;
    }
}
```

### 2.6 Detecção/Remoção do JS pelo Adversário

**Uma página adversária pode:**
- Usar MutationObserver para detectar novos elementos (overlay do picker)
- Sobrescrever `addEventListener` para interceptar o capture phase
- Monitorar `window.chrome.webview` para detectar postMessage calls

**Contramedidas:**
1. O JS do picker deve usar variáveis com nomes randomizados a cada injeção
2. O overlay pode ser criado em um Shadow DOM (difícil de detectar)
3. Event listeners devem usar capture phase com `{capture: true}`
4. Para aplicações confiáveis (seu próprio localhost), isso é um risco teórico baixo

**Na prática:** como o browser só carrega localhost de projetos do desenvolvedor,
o risco de adversário é mínimo. Documentar a limitação mas não over-engineer.

### 2.7 Resumo de Segurança - Sandbox Boundaries

```
┌─────────────────────────────────────────────────────┐
│  WPF Host Process (CommandDeck.exe)             │
│  ┌───────────────────────────────────────────────┐  │
│  │ Acesso total ao filesystem, rede, APIs do OS  │  │
│  │ SQLite, ConPTY, Git, ProcessMonitor           │  │
│  └─────────────────────┬─────────────────────────┘  │
│                        │ WebMessage API (string JSON)│
│                        │ ExecuteScriptAsync (string) │
│  ┌─────────────────────┴─────────────────────────┐  │
│  │ WebView2 Browser Process (sandboxed)           │  │
│  │ ┌───────────────────────────────────────────┐  │  │
│  │ │ Página localhost + JS injetado (picker)   │  │  │
│  │ │ NÃO acessa: filesystem, memória WPF,      │  │  │
│  │ │ outros processos, clipboard, host objects  │  │  │
│  │ │ SIM acessa: DOM, Network (localhost only), │  │  │
│  │ │ postMessage para host                      │  │  │
│  │ └───────────────────────────────────────────┘  │  │
│  └────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────┘
```

---

## PARTE 3: UX / PRODUCT DESIGN

### 3.1 Layout do Browser no CommandDeck

**Recomendação: Tab dedicada no sistema de tabs existente.**

O browser deve ser uma tab peer do Terminal Canvas e Dashboard, não um painel embutido em outro.

```
┌──────────────────────────────────────────────────────────────────┐
│ [Logo] CommandDeck     [🔍] [Search]  [Agent: Claude Code ▼]│
├──────────────────────────────────────────────────────────────────┤
│ [📟 Terminal Canvas] [📊 Dashboard] [🌐 Browser] [⚙ Settings]   │
├──────────────────────────────────────────────────────────────────┤
│ ┌────────────────────────────────────────────────┐ ┌──────────┐ │
│ │ ◄ ► 🔄 [http://localhost:3000________] [🔍 🛠]│ │ Context  │ │
│ ├────────────────────────────────────────────────┤ │ Panel    │ │
│ │                                                │ │          │ │
│ │              WebView2 Content Area             │ │ Selected │ │
│ │                                                │ │ Element: │ │
│ │              (localhost app)                    │ │ <button> │ │
│ │                                                │ │ .btn-sub │ │
│ │                                                │ │          │ │
│ │                                                │ │ [Analyze]│ │
│ │                                                │ │ [Fix Bug]│ │
│ │                                                │ │ [UX] [📁]│ │
│ │                                                │ │          │ │
│ │                                                │ │ History  │ │
│ │                                                │ │ ─────────│ │
│ │                                                │ │ <div>    │ │
│ │                                                │ │ <form>   │ │
│ │                                                │ │ <nav>    │ │
│ └────────────────────────────────────────────────┘ └──────────┘ │
│ [Status: Connected — localhost:3000] [Picker: Off] [Agent: CC]  │
└──────────────────────────────────────────────────────────────────┘
```

**Context Panel (direita):**
- Largura: 280-320px (colapsável com toggle ou drag)
- Aparece quando há elemento selecionado
- Auto-oculta quando picker é desativado
- Persistir estado open/closed via IPersistenceService

### 3.2 Botão de Seleção na Toolbar

**Posicionamento: à direita da address bar, antes do DevTools toggle.**

```
◄ ► 🔄  [http://localhost:3000______________]  [🔍] [🛠️]
                                                 ↑    ↑
                                            Picker  DevTools
```

**Design do botão:**
- Ícone: cursor com crosshair (🔍 ou custom SVG)
- Estado normal: ícone Subtext0 (#a6adc8)
- Estado hover: ícone Text (#cdd6f4)
- Estado ativo (picker on): ícone Blue (#89b4fa) + background Surface1 (#45475a)
- Toggle behavior: click para ativar/desativar
- Tooltip: "Selecionar Elemento (Ctrl+Shift+C)"

### 3.3 Design do Overlay de Seleção

**Cores (Catppuccin Mocha palette):**

```
Hover highlight:
  Border: Blue (#89b4fa) — 2px solid
  Background: Blue com 15% alpha — rgba(137, 180, 250, 0.15)

Selected (fixed):
  Border: Mauve (#cba6f7) — 2px solid
  Background: Mauve com 20% alpha — rgba(203, 166, 247, 0.20)

Margin indicator: Peach (#fab387) com 15% alpha
Padding indicator: Green (#a6e3a1) com 15% alpha
```

**Tooltip do overlay:**

```
┌─────────────────────────────────────────────────────┐
│  button#submit-btn.btn.btn-primary                  │
│  120 × 40 px                                        │
│  Component: CheckoutForm                            │
└─────────────────────────────────────────────────────┘
```

- Background: Crust (#11111b) com 95% opacity
- Text: Text (#cdd6f4)
- Font: monospace, 11px
- Posição: acima do elemento (ou abaixo se não couber)
- Border-radius: 4px
- Padding: 4px 8px

**Animações:**
- Hover transition: `all 0.15s ease-out` (rápido, não atrasa o dev)
- Seleção: scale pulse 1.0 → 1.02 → 1.0 em 200ms (feedback sutil)
- Tooltip: fade-in 100ms delay 200ms (não aparece em hover rápido)
- Deseleção: fade-out 150ms

### 3.4 Flow para Escolher Agent Alvo

**Após selecionar elemento, mostrar quick-action bar inline no context panel:**

```
┌─ Elemento Selecionado ────────────────────┐
│                                           │
│  <button#submit-btn>                      │
│  .btn.btn-primary                         │
│                                           │
│  ┌───────────────────────────────────┐    │
│  │ Enviar para:                      │    │
│  │                                   │    │
│  │  [🤖 Assistant Panel]             │    │
│  │  [🟣 Claude Code (Ativo)]    ★    │    │
│  │  [🔵 Codex Terminal]              │    │
│  │  [⚡ Hermes Agent]                │    │
│  │                                   │    │
│  │ Ação:                             │    │
│  │  [🔍 Analisar] [🐛 Fix Bug]      │    │
│  │  [✨ UX]       [📁 Localizar]     │    │
│  └───────────────────────────────────┘    │
│                                           │
│  [Enviar ↵]              [Cancelar Esc]   │
└───────────────────────────────────────────┘
```

**Flow completo:**
1. Elemento selecionado → context panel abre automaticamente
2. Agent ativo atual é pré-selecionado (★ indica ativo)
3. Intent default é "Analisar"
4. Enter ou click em "Enviar" → roteia para o agent
5. Escape → cancela seleção e fecha picker
6. Opcional: atalho direto sem context panel:
   - Ctrl+Shift+C → seleciona → Enter → envia para agent ativo com intent default
   - Para power users que não querem o flow visual

### 3.5 Confirmação Visual de Envio

**Após envio bem-sucedido:**

1. **Toast notification** (canto superior direito, padrão existente):
   ```
   ┌──────────────────────────────────────┐
   │ ✅ Contexto enviado para Claude Code │
   │ <button#submit-btn> • Analisar       │
   └──────────────────────────────────────┘
   ```
   Duração: 3 segundos, auto-dismiss

2. **Context panel**: border flash verde por 500ms, depois volta ao normal

3. **Status bar**: "Contexto enviado → Claude Code" por 5 segundos

4. **Tab indicator**: se o agent alvo é um terminal, a tab do terminal
   pisca brevemente (blink animação no tab header)

### 3.6 Exibição do Contexto Capturado

**Recomendação: Context Panel (painel lateral direito, colapsável)**

O popup/modal seria intrusivo para o workflow do desenvolvedor.
Inline no WebView não é possível sem afetar a página.
Painel lateral é a melhor opção — similar ao Chrome DevTools "Elements" panel.

**Estrutura do Context Panel:**

```
┌─ Context Panel ───────────────────────────────┐
│                                               │
│ ▼ Elemento                                    │
│   Tag: <button>                               │
│   ID: #submit-btn                             │
│   Classes: .btn .btn-primary                  │
│   Selector: #checkout > form > button.btn     │
│   Tamanho: 120 × 40 px                        │
│                                               │
│ ▶ HTML (click para expandir)                  │
│   <button id="submit-btn" class="btn btn-p... │
│                                               │
│ ▶ Computed Styles (5 propriedades)            │
│                                               │
│ ▼ Framework                                   │
│   React: CheckoutForm                         │
│   Stack: App > Layout > CheckoutPage > Form   │
│                                               │
│ ▶ Acessibilidade                              │
│   Role: button                                │
│   aria-label: "Finalizar compra"              │
│                                               │
│ ▶ Code Mapping (78% confiança)                │
│   📁 src/components/CheckoutForm.tsx          │
│   Linha ~42                                   │
│                                               │
│ ──── Ações ─────────────────────────────────  │
│ [🔍 Analisar] [🐛 Fix] [✨ UX] [📁 Code]    │
│                                               │
│ Enviar para: [Claude Code ▼]                  │
│ [      Enviar Contexto      ]                 │
│                                               │
│ ──── Histórico ─────────────────────────────  │
│ 10:32 <div.card> → CC                         │
│ 10:28 <form#login> → Assistant                │
│ 10:15 <nav.sidebar> → Codex                   │
└───────────────────────────────────────────────┘
```

### 3.7 Transitions e Animações

**Princípio: minimum viable motion — só animar o que precisa de feedback.**

| Elemento | Animação | Duração | Easing |
|---|---|---|---|
| Context panel open | Slide-in from right | 200ms | ease-out |
| Context panel close | Slide-out to right | 150ms | ease-in |
| Overlay hover | Border + background fade | 150ms | ease-out |
| Overlay selection | Scale pulse + border change | 200ms | ease-out |
| Toast appear | Slide-down + fade-in | 200ms | ease-out |
| Toast dismiss | Fade-out + slide-up | 150ms | ease-in |
| Agent selector dropdown | Fade-in + scale-y | 100ms | ease-out |
| Send button success | Background flash green | 500ms | ease-in-out |
| Tab blink | Opacity 1→0.5→1 (2 cycles) | 600ms | linear |

**XAML Animation example (Context Panel slide-in):**

```xml
<Storyboard x:Key="ContextPanelSlideIn">
    <DoubleAnimation
        Storyboard.TargetProperty="(UIElement.RenderTransform).(TranslateTransform.X)"
        From="300" To="0"
        Duration="0:0:0.2"
        EasingFunction="{StaticResource QuadraticEaseOut}" />
    <DoubleAnimation
        Storyboard.TargetProperty="Opacity"
        From="0" To="1"
        Duration="0:0:0.15" />
</Storyboard>
```

### 3.8 Acessibilidade

**Keyboard Navigation:**

| Key | Ação |
|---|---|
| Ctrl+Shift+C | Toggle element picker on/off |
| Escape | Cancel picker / close context panel |
| Tab (durante picker) | Mover entre elementos focáveis na página |
| Enter (durante picker) | Selecionar elemento com foco |
| Ctrl+B | Toggle browser tab |
| F5 | Reload página |
| Ctrl+Enter | Enviar contexto para agent ativo |
| Alt+← / Alt+→ | Browser back/forward |

**Screen Reader:**
- Context panel: `AutomationProperties.Name="Painel de Contexto do Elemento"`
- Botão picker: `AutomationProperties.Name="Ativar seleção de elemento"`
- Cada seção do context panel: headers com `AutomationProperties.HeadingLevel`
- Anunciar: "Elemento selecionado: button, id submit-btn" quando selecionar
- Anunciar: "Contexto enviado para Claude Code" quando enviar

**Focus management:**
- Quando picker ativa: foco vai para o WebView2
- Quando picker seleciona: foco vai para o context panel
- Quando context panel fecha: foco volta para a toolbar
- Tab trap: NÃO aprisionar foco no context panel

### 3.9 Dark Mode — Catppuccin Mocha Consistency

**Todas as cores devem usar a palette Catppuccin Mocha existente no Styles.xaml:**

```
Background layers:
  Base: #1e1e2e (fundo principal)
  Mantle: #181825 (sidebar background)
  Crust: #11111b (toolbar, menus)
  Surface0: #313244 (cards, elementos elevados)
  Surface1: #45475a (hover states, selected items)
  Surface2: #585b70 (borders, separadores)

Text:
  Text: #cdd6f4 (texto principal)
  Subtext1: #bac2de (texto secundário)
  Subtext0: #a6adc8 (texto terciário, placeholders)
  Overlay2: #9399b2 (texto desabilitado)

Accents:
  Blue: #89b4fa (links, seleção, picker overlay)
  Mauve: #cba6f7 (elemento selecionado/fixed)
  Green: #a6e3a1 (sucesso, envio confirmado)
  Red: #f38ba8 (erros, bugs)
  Peach: #fab387 (warnings, atenção)
  Yellow: #f9e2af (informações, highlights)
  Teal: #94e2d5 (secondary accents)
```

**Regras de aplicação no browser:**
- Browser toolbar: background Crust, texto Text
- Address bar: background Surface0, texto Text, placeholder Subtext0
- Context panel: background Mantle, cards Surface0, borders Surface2
- Overlay no WebView: NÃO afetado pelo tema WPF (é JS/CSS próprio)
  → usar cores Catppuccin equivalentes no CSS do overlay
- Botões de ação: background Surface1, hover Blue com 20% alpha
- Scrollbars: Surface1 thumb, Mantle track

### 3.10 Integração com Sistema de Tabs

**Adicionar tab "Browser" no MainViewModel:**

```csharp
// No MainViewModel.cs, onde são definidas as tabs:
public enum ViewType
{
    TerminalCanvas,
    Dashboard,
    Browser,     // ← Novo
    Settings
}
```

**O BrowserView deve:**
- Lazy-load: WebView2 só inicializa quando a tab é ativada pela primeira vez
- Manter estado: não destruir WebView2 ao trocar de tab
- Badge indicator: mostrar dot verde na tab quando servidor está conectado
- Tab icon: 🌐 (globe) ou custom SVG de browser

### 3.11 Status Bar Indicators

**Adicionar na barra inferior do CommandDeck:**

```
[Terminal: 3 sessions] [Git: main ↑2] [🌐 localhost:3000 ●] [Picker: 🔍 On] [Agent: CC]
                                      └──── browser status ──────────────────┘
```

| Indicator | States |
|---|---|
| Server status | ● Verde (connected), ○ Cinza (disconnected), ⊘ Vermelho (error) |
| Picker status | Visível apenas quando ativo. "🔍 On" em Blue, "Off" hidden |
| Last selection | Mostrar brevemente após seleção: "<button#sub...>" por 5s |

### 3.12 Atalhos de Teclado

**Registrar via CommandPaletteService existente:**

```csharp
// Em CommandPaletteRegistrar.cs, adicionar:
palette.RegisterCommand(new CommandDefinitionModel
{
    Id = "browser.toggle",
    Label = "Abrir/Fechar Browser",
    Shortcut = "Ctrl+B",
    Category = "Browser",
    Action = () => mainVm.ActivateView(ViewType.Browser)
});

palette.RegisterCommand(new CommandDefinitionModel
{
    Id = "browser.picker.toggle",
    Label = "Ativar/Desativar Element Picker",
    Shortcut = "Ctrl+Shift+C",
    Category = "Browser",
    Action = () => browserVm.TogglePickerCommand.Execute(null)
});

palette.RegisterCommand(new CommandDefinitionModel
{
    Id = "browser.picker.cancel",
    Label = "Cancelar Seleção",
    Shortcut = "Escape",
    Category = "Browser",
    Action = () => browserVm.CancelPickerCommand.Execute(null)
});

palette.RegisterCommand(new CommandDefinitionModel
{
    Id = "browser.reload",
    Label = "Recarregar Página",
    Shortcut = "F5",
    Category = "Browser",
    Action = () => browserVm.ReloadCommand.Execute(null)
});

palette.RegisterCommand(new CommandDefinitionModel
{
    Id = "browser.sendContext",
    Label = "Enviar Contexto para Agent",
    Shortcut = "Ctrl+Enter",
    Category = "Browser",
    Action = () => browserVm.SendToActiveAgentCommand.Execute(null)
});
```

---

## PARTE 4: CODE MAPPING (Elemento → Código Fonte)

### 4.1 Estratégia em Camadas

O code mapping deve tentar múltiplas estratégias em ordem de confiança decrescente:

```csharp
// Services/Browser/ICodeMappingService.cs
public interface ICodeMappingService
{
    Task<CodeMappingResult> MapElementToCodeAsync(
        ElementCaptureData element,
        string projectPath);
}

// Services/Browser/CodeMappingService.cs
public sealed class CodeMappingService : ICodeMappingService
{
    private readonly IProjectService _projectService;
    private readonly IGitService _gitService;

    public async Task<CodeMappingResult> MapElementToCodeAsync(
        ElementCaptureData element,
        string projectPath)
    {
        var candidates = new List<CodeMappingCandidate>();

        // Layer 1: React Fiber / Framework info (highest confidence)
        if (element.FrameworkInfo?.ComponentName != null)
        {
            var fiberCandidates = await SearchByComponentName(
                element.FrameworkInfo.ComponentName,
                element.FrameworkInfo.Framework,
                projectPath);
            candidates.AddRange(fiberCandidates);
        }

        // Layer 2: data-testid / data-component attributes
        var testIdCandidates = await SearchByDataAttributes(element, projectPath);
        candidates.AddRange(testIdCandidates);

        // Layer 3: Class name heuristics
        var classCandidates = await SearchByClassNames(element, projectPath);
        candidates.AddRange(classCandidates);

        // Layer 4: Tag structure + text content heuristics
        var structureCandidates = await SearchByStructure(element, projectPath);
        candidates.AddRange(structureCandidates);

        // Deduplicate and rank
        var ranked = RankCandidates(candidates);

        if (ranked.Count == 0)
        {
            return new CodeMappingResult
            {
                Strategy = CodeMappingStrategy.None,
                Confidence = 0,
                Candidates = new()
            };
        }

        var best = ranked[0];
        return new CodeMappingResult
        {
            FilePath = best.FilePath,
            LineNumber = best.LineNumber,
            ComponentName = element.FrameworkInfo?.ComponentName,
            Confidence = best.Confidence,
            Strategy = best.Strategy,
            Candidates = ranked
        };
    }
}
```

### 4.2 Layer 1 — React Fiber / Framework Detection

O JavaScript do picker já tenta acessar `__reactFiber$` ou `__reactInternalInstance$`:

```javascript
// No element-picker.js:
function getReactInfo(element) {
    // React 18+ uses __reactFiber$xxx
    const fiberKey = Object.keys(element).find(k => k.startsWith('__reactFiber$'));
    if (fiberKey) {
        const fiber = element[fiberKey];
        return walkFiberTree(fiber);
    }

    // React 16-17 uses __reactInternalInstance$xxx
    const instanceKey = Object.keys(element).find(k => k.startsWith('__reactInternalInstance$'));
    if (instanceKey) {
        const instance = element[instanceKey];
        return walkFiberTree(instance);
    }

    // Vue 3
    if (element.__vueParentComponent) {
        return getVueInfo(element);
    }

    return null;
}

function walkFiberTree(fiber) {
    const components = [];
    let current = fiber;
    while (current) {
        if (typeof current.type === 'function' || typeof current.type === 'object') {
            const name = current.type.displayName || current.type.name || null;
            if (name && !name.startsWith('_')) {
                components.push(name);
            }
        }
        current = current.return;
    }
    return {
        componentName: components[0] || null,
        componentStack: components
    };
}
```

**Busca no projeto:**

```csharp
private async Task<List<CodeMappingCandidate>> SearchByComponentName(
    string componentName, string? framework, string projectPath)
{
    var candidates = new List<CodeMappingCandidate>();
    var extensions = GetExtensions(framework); // [".tsx", ".jsx", ".ts", ".js", ".vue"]

    // Buscar arquivos com nome do componente
    var matchingFiles = await FindFilesAsync(projectPath, $"*{componentName}*", extensions);

    foreach (var file in matchingFiles)
    {
        var confidence = CalculateComponentConfidence(file, componentName);
        var lineNumber = await FindComponentDefinitionLine(file, componentName);

        candidates.Add(new CodeMappingCandidate
        {
            FilePath = GetRelativePath(file, projectPath),
            LineNumber = lineNumber,
            Confidence = confidence,
            Strategy = CodeMappingStrategy.ReactFiber,
            Reason = $"Component name '{componentName}' found in filename"
        });
    }

    // Também buscar dentro dos arquivos por export/function/class declarations
    if (candidates.Count == 0)
    {
        var allSourceFiles = await FindFilesAsync(projectPath, "*", extensions);
        foreach (var file in allSourceFiles.Take(500)) // Limitar busca
        {
            var content = await File.ReadAllTextAsync(file);
            if (ContainsComponentDefinition(content, componentName))
            {
                var line = FindDefinitionLine(content, componentName);
                candidates.Add(new CodeMappingCandidate
                {
                    FilePath = GetRelativePath(file, projectPath),
                    LineNumber = line,
                    Confidence = 0.8, // Alta confiança, encontrou definição
                    Strategy = CodeMappingStrategy.ReactFiber,
                    Reason = $"Component '{componentName}' defined in file"
                });
            }
        }
    }

    return candidates;
}

private bool ContainsComponentDefinition(string content, string name)
{
    // Patterns for component definitions
    var patterns = new[]
    {
        $"function {name}",          // function ComponentName
        $"const {name}",             // const ComponentName =
        $"class {name}",             // class ComponentName
        $"export default {name}",    // export default ComponentName
        $"export {{ {name}",         // export { ComponentName }
        $"export function {name}",   // export function ComponentName
    };

    return patterns.Any(p => content.Contains(p, StringComparison.Ordinal));
}
```

### 4.3 Layer 2 — data-testid e Atributos de Identificação

```csharp
private async Task<List<CodeMappingCandidate>> SearchByDataAttributes(
    ElementCaptureData element, string projectPath)
{
    var candidates = new List<CodeMappingCandidate>();
    var searchTerms = new List<string>();

    // data-testid é o mais confiável
    if (element.FrameworkInfo?.TestIds.Count > 0)
    {
        foreach (var (key, value) in element.FrameworkInfo.TestIds)
            searchTerms.Add(value);
    }

    // data-component, data-cy, data-qa
    foreach (var attr in new[] { "data-testid", "data-component", "data-cy", "data-qa" })
    {
        if (element.Attributes.TryGetValue(attr, out var val) && !string.IsNullOrEmpty(val))
            searchTerms.Add(val);
    }

    if (searchTerms.Count == 0) return candidates;

    var extensions = new[] { ".tsx", ".jsx", ".ts", ".js", ".vue", ".svelte" };
    var sourceFiles = await FindFilesAsync(projectPath, "*", extensions);

    foreach (var term in searchTerms)
    {
        foreach (var file in sourceFiles.Take(500))
        {
            var content = await File.ReadAllTextAsync(file);
            if (content.Contains(term, StringComparison.Ordinal))
            {
                var line = FindLineContaining(content, term);
                candidates.Add(new CodeMappingCandidate
                {
                    FilePath = GetRelativePath(file, projectPath),
                    LineNumber = line,
                    Confidence = 0.85, // data-testid é muito confiável
                    Strategy = CodeMappingStrategy.DataTestId,
                    Reason = $"data-testid '{term}' found in source"
                });
            }
        }
    }

    return candidates;
}
```

### 4.4 Layer 3 — Heurísticas por Class Names

```csharp
private async Task<List<CodeMappingCandidate>> SearchByClassNames(
    ElementCaptureData element, string projectPath)
{
    var candidates = new List<CodeMappingCandidate>();

    if (string.IsNullOrEmpty(element.ClassName)) return candidates;

    var classes = element.ClassName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // Filtrar classes genéricas (utilities de framework CSS)
    var meaningfulClasses = classes
        .Where(c => !IsUtilityClass(c))
        .Where(c => c.Length > 3) // Ignorar classes muito curtas
        .ToList();

    if (meaningfulClasses.Count == 0) return candidates;

    // Buscar classes que parecem ser BEM ou component-specific
    var componentClasses = meaningfulClasses
        .Where(c => c.Contains('-') || c.Contains('_') || char.IsUpper(c[0]))
        .ToList();

    var extensions = new[] { ".tsx", ".jsx", ".ts", ".js", ".vue", ".css", ".scss", ".module.css" };
    var sourceFiles = await FindFilesAsync(projectPath, "*", extensions);

    foreach (var cls in componentClasses.Take(5)) // Top 5 mais específicas
    {
        foreach (var file in sourceFiles.Take(500))
        {
            var content = await File.ReadAllTextAsync(file);
            if (content.Contains(cls, StringComparison.Ordinal))
            {
                candidates.Add(new CodeMappingCandidate
                {
                    FilePath = GetRelativePath(file, projectPath),
                    LineNumber = FindLineContaining(content, cls),
                    Confidence = 0.4, // Baixa confiança — classe pode ser de framework
                    Strategy = CodeMappingStrategy.ClassNameHeuristic,
                    Reason = $"Class '{cls}' found in source"
                });
            }
        }
    }

    return candidates;
}

private static bool IsUtilityClass(string className)
{
    // Tailwind, Bootstrap, common utility patterns
    var prefixes = new[] {
        "flex", "grid", "p-", "m-", "px-", "py-", "mx-", "my-",
        "text-", "bg-", "border-", "rounded", "shadow", "w-", "h-",
        "col-", "row-", "d-", "justify-", "items-", "align-",
        "hidden", "block", "inline", "relative", "absolute",
        "sm:", "md:", "lg:", "xl:", "2xl:", "hover:", "focus:",
        "container", "wrapper", "clearfix"
    };
    return prefixes.Any(p => className.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
```

### 4.5 File Search usando ProjectService/GitService

```csharp
private async Task<List<string>> FindFilesAsync(
    string projectPath, string pattern, string[] extensions)
{
    var results = new List<string>();

    // Usar Git para listar arquivos tracked (mais rápido que filesystem scan)
    try
    {
        var gitInfo = await _gitService.GetGitInfoAsync(projectPath);
        if (gitInfo != null)
        {
            // git ls-files é muito mais rápido que Directory.EnumerateFiles
            var process = new System.Diagnostics.Process
            {
                StartInfo = new()
                {
                    FileName = "git",
                    Arguments = "ls-files",
                    WorkingDirectory = projectPath,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var files = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (extensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                {
                    var fullPath = Path.Combine(projectPath, file.Replace('/', Path.DirectorySeparatorChar));
                    if (pattern == "*" || Path.GetFileName(file).Contains(
                        pattern.Replace("*", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(fullPath);
                    }
                }
            }

            return results;
        }
    }
    catch { }

    // Fallback: filesystem scan (ignore node_modules, .git, etc.)
    var excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "dist", "build", ".next", ".nuxt",
        "coverage", "__pycache__", "vendor", "bin", "obj"
    };

    try
    {
        foreach (var file in Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories))
        {
            // Skip excluded directories
            var relativePath = Path.GetRelativePath(projectPath, file);
            var parts = relativePath.Split(Path.DirectorySeparatorChar);
            if (parts.Any(p => excludeDirs.Contains(p))) continue;

            var ext = Path.GetExtension(file);
            if (extensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(file);
            }
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[CodeMapping] File search error: {ex.Message}");
    }

    return results;
}
```

### 4.6 Confidence Scoring

```csharp
private static List<CodeMappingCandidate> RankCandidates(
    List<CodeMappingCandidate> candidates)
{
    // Deduplicate by file path
    var grouped = candidates
        .GroupBy(c => c.FilePath)
        .Select(g =>
        {
            var best = g.OrderByDescending(c => c.Confidence).First();
            // Boost confidence if multiple strategies found the same file
            var strategyCount = g.Select(c => c.Strategy).Distinct().Count();
            var boostedConfidence = Math.Min(1.0, best.Confidence + (strategyCount - 1) * 0.15);

            return new CodeMappingCandidate
            {
                FilePath = best.FilePath,
                LineNumber = best.LineNumber,
                Confidence = boostedConfidence,
                Strategy = best.Strategy,
                Reason = strategyCount > 1
                    ? $"{best.Reason} (confirmed by {strategyCount} strategies)"
                    : best.Reason
            };
        })
        .OrderByDescending(c => c.Confidence)
        .Take(5) // Top 5 candidates
        .ToList();

    return grouped;
}
```

**Thresholds de confiança:**

| Score | Significado | UI Treatment |
|---|---|---|
| 0.9 - 1.0 | Quase certeza (React fiber + filename match) | ✅ Mostrar como resultado principal |
| 0.7 - 0.89 | Alta confiança (data-testid found) | ✅ Mostrar com indicador "provável" |
| 0.5 - 0.69 | Média confiança (class heuristic) | ⚠️ Mostrar com "possível" + lista de candidatos |
| 0.3 - 0.49 | Baixa confiança (text search) | ❓ Mostrar como "sugestão" com disclaimer |
| < 0.3 | Muito baixa | ❌ Não mostrar, informar que não foi possível |

### 4.7 UI para Code Mapping Results

**No Context Panel:**

```
▼ Code Mapping
  ┌──────────────────────────────────────────────┐
  │ ✅ Alta confiança (85%)                       │
  │                                              │
  │ 📁 src/components/CheckoutForm.tsx           │
  │    Linha ~42                                  │
  │    Estratégia: React Fiber + data-testid      │
  │                                              │
  │ [Abrir no Editor]  [Copiar Path]              │
  ├──────────────────────────────────────────────┤
  │ Outros candidatos:                            │
  │   📁 src/pages/Checkout.tsx (62%)             │
  │   📁 src/styles/checkout.module.css (35%)     │
  └──────────────────────────────────────────────┘
```

**Quando mapping não é possível:**

```
▼ Code Mapping
  ┌──────────────────────────────────────────────┐
  │ ❓ Não foi possível localizar o código fonte  │
  │                                              │
  │ Possíveis razões:                             │
  │ • Elemento é de um framework CSS (Bootstrap) │
  │ • Código minificado sem source maps           │
  │ • Projeto não é React/Vue/Svelte              │
  │                                              │
  │ Dica: Adicione data-testid ao elemento para   │
  │ facilitar a localização automática.            │
  │                                              │
  │ [Pedir à IA para localizar]                   │
  └──────────────────────────────────────────────┘
```

### 4.8 Limitações do Code Mapping

**Documentar claramente para o usuário:**

1. **Código minificado**: Se o projeto está em produção build, nomes de componentes
   são mangled. Funciona melhor com development builds (`npm run dev`).

2. **CSS-only frameworks**: Bootstrap, Tailwind etc. — as classes não mapeiam
   para componentes, apenas para o framework CSS.

3. **Server-side rendered**: Componentes renderizados no servidor podem não ter
   React fiber data no DOM.

4. **Shadow DOM**: Web Components com Shadow DOM encapsulado são mais difíceis
   de mapear.

5. **Monorepos grandes**: Busca em >10k arquivos pode ser lenta. Implementar
   caching e limitar a busca ao `src/` directory.

6. **Projetos sem Git**: Sem `git ls-files`, a busca por filesystem é mais lenta
   e pode incluir node_modules por engano (mitigado pela exclusion list).

---

## PARTE 5: RESUMO DE IMPLEMENTAÇÃO

### Novos Arquivos a Criar

```
Models/Browser/
  ElementContext.cs              — Modelo enriquecido
  CodeMappingResult.cs           — Resultado do code mapping
  ElementSelectionRecord.cs      — Registro para persistência
  ElementContextMessage.cs       — Mensagem para Messenger

Services/Browser/
  IAiContextRouter.cs            — Interface do router
  AiContextRouter.cs             — Implementação do routing
  ICodeMappingService.cs         — Interface do code mapping
  CodeMappingService.cs          — Implementação do mapping
  ElementSanitizer.cs            — Sanitização de segurança
  ElementContextFormatter.cs     — Formatação texto/estruturado
  TerminalContextInjector.cs     — Injeção em terminais
  WebMessageRateLimiter.cs       — Rate limiting
```

### Arquivos a Modificar

```
Models/AiTerminalContext.cs      — Adicionar novos AiPromptIntent values
Models/ChatMessage.cs            — Adicionar IsElementContext, FromElementContext()
Services/IAiContextService.cs    — Adicionar BuildElementPromptAsync, LastCapturedElement
Services/AiContextService.cs     — Implementar novos métodos
Services/Browser/BrowserRuntimeService.cs — Adicionar rate limiter, security hardening
ViewModels/BrowserViewModel.cs   — Adicionar picker toggle, context panel state
ViewModels/AssistantPanelViewModel.cs — Registrar ElementContextMessage handler
Services/IPersistenceService.cs  — Adicionar métodos para element selections
Services/PersistenceService.cs   — Implementar persistência de selections
App.xaml.cs                      — Registrar novos serviços no DI container
```

### Ordem de Implementação Recomendada

```
Phase 1: Foundation (1-2 dias)
  ├── ElementContext model + CodeMappingResult
  ├── ElementSanitizer
  ├── WebMessageRateLimiter
  └── Security hardening no BrowserRuntimeService

Phase 2: Core (2-3 dias)
  ├── ElementContextFormatter (texto + estruturado)
  ├── TerminalContextInjector
  ├── AiContextRouter
  ├── Estender IAiContextService + AiContextService
  └── Novos AiPromptIntent values

Phase 3: UI (2-3 dias)
  ├── Context Panel XAML + ViewModel
  ├── Agent selector dropdown
  ├── Keyboard shortcuts
  ├── AssistantPanelViewModel messenger integration
  └── Status bar indicators

Phase 4: Code Mapping (2-3 dias)
  ├── ICodeMappingService + CodeMappingService
  ├── React fiber search
  ├── data-testid search
  ├── Class name heuristics
  └── UI para resultados

Phase 5: Polish (1-2 dias)
  ├── Histórico de seleções (SQLite)
  ├── Reuso de contexto
  ├── Animações e transitions
  ├── Acessibilidade
  └── Testes manuais
```

### Registros DI em App.xaml.cs

```csharp
// Adicionar no ConfigureServices:
services.AddSingleton<IAiContextRouter, AiContextRouter>();
services.AddSingleton<ICodeMappingService, CodeMappingService>();
```

---

## APÊNDICE A: Checklist de Segurança

- [ ] Password fields: values NUNCA capturados
- [ ] Tokens JWT/API: redacted antes de enviar para AI
- [ ] Rate limiter: max 10 messages/5s no WebMessage
- [ ] HTML truncado: max 50KB por captura
- [ ] Script tags removidos do HTML capturado
- [ ] Event handlers inline removidos
- [ ] HostObjects desabilitados no WebView2
- [ ] Navegação restrita a localhost/127.0.0.1
- [ ] Downloads bloqueados
- [ ] Popups bloqueadas
- [ ] WebMessage JSON validado antes de deserializar
- [ ] Caracteres de controle stripped antes de injetar em terminal

## APÊNDICE B: Catppuccin Mocha Color Reference (para CSS do Picker)

```css
:root {
    --ctp-rosewater: #f5e0dc;
    --ctp-flamingo:  #f2cdcd;
    --ctp-pink:      #f5c2e7;
    --ctp-mauve:     #cba6f7;
    --ctp-red:       #f38ba8;
    --ctp-maroon:    #eba0ac;
    --ctp-peach:     #fab387;
    --ctp-yellow:    #f9e2af;
    --ctp-green:     #a6e3a1;
    --ctp-teal:      #94e2d5;
    --ctp-sky:       #89dcfe;
    --ctp-sapphire:  #74c7ec;
    --ctp-blue:      #89b4fa;
    --ctp-lavender:  #b4befe;
    --ctp-text:      #cdd6f4;
    --ctp-subtext1:  #bac2de;
    --ctp-subtext0:  #a6adc8;
    --ctp-overlay2:  #9399b2;
    --ctp-overlay1:  #7f849c;
    --ctp-overlay0:  #6c7086;
    --ctp-surface2:  #585b70;
    --ctp-surface1:  #45475a;
    --ctp-surface0:  #313244;
    --ctp-base:      #1e1e2e;
    --ctp-mantle:    #181825;
    --ctp-crust:     #11111b;
}
```
