# CommandDeck — AI-First Terminal Architecture

## Plano de Transformação: Terminal-Centric AI com `cc` / `claude`

---

# BLOCO A — ANÁLISE CRÍTICA

## 1. A direção faz sentido do ponto de vista de produto?

**Sim, faz sentido — e é uma das poucas direções que pode diferenciar o produto.**

O mercado de IDEs desktop está saturado por VS Code, Cursor, Windsurf, Zed. Tentar competir no eixo "editor + chat lateral" é suicídio. Mas existe um nicho real e mal atendido: **power users que já usam Claude Code / Codex / aider no terminal e precisam de um workspace manager em volta disso, não de um editor que tenta engolir o terminal**.

A premissa "a IA vive no terminal" é exatamente o que Claude Code, Codex CLI e aider já fazem. O CommandDeck não precisa reimplementar nada disso — ele precisa ser a **camada de orquestração visual** em volta dessas ferramentas.

**Veredicto: faz sentido. É a única direção que evita competir frontalmente com IDEs estabelecidas.**

## 2. Faz sentido do ponto de vista de arquitetura?

**Parcialmente. O estado atual da camada de IA precisa de cirurgia.**

O que existe hoje:
- `IAssistantProvider` / `IAssistantService` com providers Ollama e OpenAI Stub
- Chat via HTTP (API calls diretas)
- Painel lateral com chat básico, "explicar output", "sugerir comando"

Problemas:
- **A arquitetura atual trata IA como API HTTP remota**. No modelo `cc`/`claude`, a IA é um **processo local rodando dentro do terminal**. São paradigmas completamente diferentes.
- `OllamaProvider` e `OpenAIProviderStub` fazem chamadas HTTP. O `cc`/`claude` é um CLI invocado via ConPTY. Não dá pra reusar esses providers.
- `IAssistantProvider` com `ChatAsync`, `StreamChatAsync`, `ExplainAsync` — essa interface foi desenhada para APIs HTTP. Para processos interativos de terminal, o modelo é outro: stdin/stdout/lifecycle management.

**O `IAssistantService` atual deve ser preservado para o painel lateral de apoio (quick-explain, quick-suggest), mas o fluxo principal de IA precisa de uma camada nova e separada.**

## 3. Vantagens reais

1. **Zero dependência de API keys para o fluxo principal.** O `cc` e `claude` gerenciam suas próprias credenciais. O app não precisa armazenar chaves.

2. **IA stateful de verdade.** Claude Code e `cc` mantêm contexto do projeto inteiro — CLAUDE.md, árvore de arquivos, git state. Nenhum chat lateral reproduz isso.

3. **O usuário já sabe usar.** Quem usa `cc` no terminal hoje, usa igual dentro do app. Zero curva de aprendizado.

4. **Composabilidade.** O usuário pode combinar `cc run opus` com `grep`, `git`, `make` na mesma sessão. O app não precisa implementar integrations — o terminal já é o integration layer.

5. **Diferenciação clara.** "VS Code para quem já usa Claude Code" é um posicionamento limpo.

6. **Custo zero de manutenção de providers.** Quando Anthropic lança modelo novo, o `cc` já suporta. O app não precisa atualizar nada.

## 4. Riscos

### R1 — Fragilidade do parsing de output CLI
O `cc` e `claude` emitem output com ANSI, markdown renderizado, spinners, progress bars. Tentar parsear esse output para extrair respostas estruturadas é frágil. **O app NÃO deve parsear output do `cc`/`claude` para alimentar UI.** Deve apenas exibir no terminal e, opcionalmente, capturar o texto raw para search/history.

### R2 — Dependência de CLI externo
Se o `cc` mudar breaking changes na CLI, o app quebra. Mitigação: o `cc` é do próprio Rony, então controle é total. `claude` é da Anthropic e mais estável.

### R3 — Experiência degradada sem `cc`/`claude` instalados
O app precisa funcionar minimamente sem `cc`. O painel lateral com Ollama/OpenAI direto deve continuar existindo como fallback.

### R4 — Conflito de identidade do painel lateral
Se o painel lateral de IA faz chat via API e o terminal faz IA via `cc`, o usuário pode ficar confuso sobre quando usar cada um. **Precisa ficar muito claro na UI: terminal = fluxo principal, painel = apoio rápido.**

### R5 — Complexidade do model routing
Criar um `ModelRoutingService` que decide automaticamente "use haiku para isso, opus para aquilo" é overengineering prematuro. O usuário já sabe qual modelo quer. O app deve facilitar a seleção, não decidir por ele.

## 5. Pontos mal pensados

### P1 — ModelRoutingService automático
Você pediu regras tipo "tarefa rápida → haiku, refatoração → opus". Isso é uma armadilha. Razões:
- O app não tem como inferir a complexidade da tarefa do usuário de forma confiável
- O overhead de classificar antes de executar adiciona latência
- O usuário power-user quer controle direto, não automação mágica

**Recomendação: trocar auto-routing por quick-switch manual.** Um dropdown ou atalho para mudar o modelo ativo. Simples, previsível, sem mágica.

### P2 — "Gerar comando PowerShell/CMD/WSL" como feature de IA
Gerar comandos é trivial e qualquer LLM faz. Mas injetar comandos gerados por IA no terminal é perigoso (rm -rf, DROP TABLE). Essa feature precisa de um passo de confirmação explícita e visível.

### P3 — "Executar comando sugerido" no painel lateral
Se o painel lateral sugere `rm -rf node_modules && npm install` e tem um botão "Executar no terminal", isso é um vetor de risco. O comando deve ser **copiado** para o terminal como texto não-executado, nunca injetado diretamente.

### P4 — Excesso de serviços propostos
Você listou 7 novos serviços/interfaces. Isso é overengineering. O MVP precisa de 3 no máximo.

## 6. Recomendação final

**A direção faz sentido. Implementar com cirurgia, não com reescrita.**

Manter:
- `IAssistantService` / `IAssistantProvider` para o painel lateral (quick actions via API)
- Toda a infra de terminal, canvas, workspace, persistence

Criar:
- Uma camada nova e ortogonal para "IA via CLI no terminal"
- Integração com command palette para ações AI
- Quick-switch de modelo (não auto-routing)

Não criar:
- ModelRoutingService automático
- AiContextService complexo (o `cc`/`claude` já coletam contexto sozinhos)

---

# BLOCO B — ARQUITETURA / IMPLEMENTAÇÃO

## 1. Arquitetura recomendada

```
┌─────────────────────────────────────────────────────────────┐
│                    CommandDeck                          │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              CAMADA DE IA                            │   │
│  │                                                      │   │
│  │  ┌──────────────┐    ┌────────────────────────────┐  │   │
│  │  │ Painel       │    │ Terminal AI                 │  │   │
│  │  │ Lateral      │    │ (fluxo principal)           │  │   │
│  │  │              │    │                             │  │   │
│  │  │ IAssistant   │    │ IAiTerminalService          │  │   │
│  │  │ Service      │    │  - LaunchCcAsync()          │  │   │
│  │  │ (API HTTP)   │    │  - LaunchClaudeAsync()      │  │   │
│  │  │              │    │  - GetActiveAiSessions()     │  │   │
│  │  │ Quick-explain│    │  - IsCliAvailable("cc")     │  │   │
│  │  │ Quick-suggest│    │                             │  │   │
│  │  └──────────────┘    └────────────────────────────┘  │   │
│  │                             │                        │   │
│  │                             ▼                        │   │
│  │                ┌────────────────────────┐            │   │
│  │                │ IAiModelConfigService  │            │   │
│  │                │  - GetActiveModel()    │            │   │
│  │                │  - SetModel(slot,id)   │            │   │
│  │                │  - GetSlots()          │            │   │
│  │                │  - UseOpenRouter(bool) │            │   │
│  │                └────────────────────────┘            │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  ITerminalSessionService (existente)                 │   │
│  │  ITerminalService (existente — ConPTY)               │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  ICommandPaletteService (existente + novos comandos) │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### Princípio fundamental: separação de camadas

| Camada | Responsabilidade | Exemplo |
|---|---|---|
| **AI Terminal** (novo) | Criar/gerenciar sessões de terminal com `cc`/`claude` | `IAiTerminalService` |
| **AI Model Config** (novo) | Configurar modelos, slots, aliases, OpenRouter | `IAiModelConfigService` |
| **AI Panel** (existente) | Quick actions via API HTTP (Ollama/OpenAI) | `IAssistantService` (já existe) |
| **Terminal** (existente) | ConPTY, sessions, output, resize | `ITerminalService` (já existe) |
| **Command Palette** (existente) | Ações via Ctrl+K | `ICommandPaletteService` (já existe) |

## 2. Novos serviços / interfaces / models

### 2.1. `IAiTerminalService` / `AiTerminalService`

**Responsabilidade:** Criar sessões de terminal que rodam `cc`, `claude` ou `cc run <modelo>` como processo ConPTY.

```csharp
// Services/IAiTerminalService.cs
namespace CommandDeck.Services;

public interface IAiTerminalService
{
    /// Verifica se o CLI está disponível no PATH ($PATH / %PATH%)
    Task<bool> IsCliAvailableAsync(string cliName = "cc");

    /// Abre terminal com `cc` (modo padrão interativo)
    Task<TerminalSessionModel> LaunchCcAsync(
        string? workingDirectory = null,
        string? projectId = null);

    /// Abre terminal com `cc run <modelOrAlias>`
    Task<TerminalSessionModel> LaunchCcRunAsync(
        string modelOrAlias,
        string? workingDirectory = null,
        string? projectId = null);

    /// Abre terminal com `cc or` (OpenRouter picker)
    Task<TerminalSessionModel> LaunchCcOpenRouterAsync(
        string? workingDirectory = null,
        string? projectId = null);

    /// Abre terminal com `claude` direto (fallback)
    Task<TerminalSessionModel> LaunchClaudeAsync(
        string? workingDirectory = null,
        string? projectId = null);

    /// Envia prompt para terminal AI ativo (injeta texto + Enter)
    Task SendPromptToActiveAsync(string prompt);

    /// Retorna todas as sessões de terminal marcadas como AI
    IReadOnlyList<TerminalSessionModel> GetActiveAiSessions();

    /// Verifica qual CLI está disponível e retorna info
    Task<AiCliInfo> DetectCliAsync();
}
```

**Implementação interna:** Usa `ITerminalSessionService.CreateSessionAsync()` com shell customizado. Em vez de `wsl.exe` ou `powershell.exe`, lança `cc`, `cc run opus`, etc.

**Não é um provider novo de `IAssistantProvider`.** É um serviço que cria terminais. A diferença é fundamental.

### 2.2. `IAiModelConfigService` / `AiModelConfigService`

**Responsabilidade:** Gerenciar modelos, slots e aliases do `cc`. Persistir preferências.

```csharp
// Services/IAiModelConfigService.cs
namespace CommandDeck.Services;

public interface IAiModelConfigService
{
    /// Modelo ativo para cada slot
    string GetModel(AiModelSlot slot);
    Task SetModelAsync(AiModelSlot slot, string modelId);

    /// Aliases registrados no cc
    Task<IReadOnlyList<AiAlias>> GetAliasesAsync();
    Task AddAliasAsync(string name, string modelId);
    Task RemoveAliasAsync(string name);

    /// OpenRouter toggle
    bool IsOpenRouterEnabled { get; }
    Task SetOpenRouterEnabledAsync(bool enabled);

    /// Slot ativo (qual slot usar por default ao abrir cc)
    AiModelSlot ActiveSlot { get; }
    Task SetActiveSlotAsync(AiModelSlot slot);

    /// Carrega config do disco (cc model list / cc run list)
    Task RefreshFromCliAsync();
}
```

### 2.3. Novos models

```csharp
// Models/AiModelSlot.cs
namespace CommandDeck.Models;

public enum AiModelSlot
{
    Sonnet,
    Opus,
    Haiku,
    Agent
}

// Models/AiAlias.cs
namespace CommandDeck.Models;

public record AiAlias(string Name, string ModelId);

// Models/AiCliInfo.cs
namespace CommandDeck.Models;

public record AiCliInfo(
    bool CcAvailable,
    bool ClaudeAvailable,
    string? CcVersion,
    string? ClaudeVersion,
    bool OpenRouterConfigured);

// Models/AiSessionTag.cs — tag para marcar sessão como AI
namespace CommandDeck.Models;

public enum AiSessionType
{
    None,      // terminal normal
    Cc,        // aberto com cc
    CcRun,     // aberto com cc run <modelo>
    CcOr,      // aberto com cc or
    Claude     // aberto com claude direto
}
```

### 2.4. Extensão do `TerminalSessionModel`

Adicionar um campo para marcar sessões como AI:

```csharp
// Adicionar ao TerminalSessionModel existente:
[ObservableProperty]
private AiSessionType _aiSessionType = AiSessionType.None;

[ObservableProperty]
private string _aiModelUsed = string.Empty;
```

### 2.5. ViewModel: `AiTerminalViewModel` (NÃO criar)

**Não criar um ViewModel separado.** O `TerminalViewModel` existente já serve. A sessão AI é um terminal normal com metadata extra (`AiSessionType`). A UI pode reagir a esse campo para mostrar indicadores visuais (badge "AI", nome do modelo, etc).

## 3. Fluxo ideal do usuário

### Fluxo 1: Abrir agente AI rápido
1. Ctrl+Shift+P → "Abrir AI Terminal"
2. App detecta `cc` → abre terminal com `cc` no cwd do projeto ativo
3. Usuário interage normalmente no terminal
4. Badge "AI • Sonnet" aparece no card do terminal

### Fluxo 2: Escolher modelo específico
1. Ctrl+Shift+P → "AI: Usar Opus"
2. App abre terminal com `cc run opus` no cwd do projeto ativo
3. Badge "AI • Opus" no card

### Fluxo 3: Explicar erro do terminal atual
1. Terminal ativo mostra um stack trace
2. Ctrl+Shift+P → "Explicar erro do terminal"
3. App pega as últimas 50 linhas do output snapshot
4. Duas opções:
   - a) Abre `cc` em terminal novo com o contexto (mais poderoso)
   - b) Usa painel lateral via `IAssistantService` (mais rápido, menos contexto)
5. Resultado aparece no painel lateral OU no terminal AI dedicado

### Fluxo 4: OpenRouter
1. Ctrl+Shift+P → "AI: OpenRouter"
2. App abre terminal com `cc or`
3. Picker de modelos do `cc` aparece no terminal

### Fluxo 5: Quick-suggest sem sair do terminal
1. Usuário está no terminal normal
2. Ctrl+Shift+P → "Sugerir comando"
3. Painel lateral (IAssistantService/Ollama) gera sugestão rápida
4. Usuário copia e cola no terminal

## 4. Ordem de implementação

### Fase 1 — MVP (1-2 dias)
1. Criar `Models/AiModelSlot.cs`, `Models/AiAlias.cs`, `Models/AiCliInfo.cs`, `Models/AiSessionType.cs`
2. Adicionar `AiSessionType` e `AiModelUsed` ao `TerminalSessionModel`
3. Criar `Services/IAiTerminalService.cs` (interface)
4. Criar `Services/AiTerminalService.cs` (implementação)
5. Registrar em `App.xaml.cs`
6. Adicionar comandos AI na command palette via `CommandPaletteRegistrar`

**Resultado:** Usuário pode abrir `cc`, `cc run opus`, `claude` via command palette. Sessões AI são marcadas visualmente.

### Fase 2 — Model Config (1 dia)
7. Criar `Services/IAiModelConfigService.cs` (interface)
8. Criar `Services/AiModelConfigService.cs` (implementação — persiste em SQLite via `IPersistenceService`)
9. Adicionar comandos de model switching na command palette
10. Integrar com `AiTerminalService` para usar modelo configurado

**Resultado:** Usuário pode trocar modelos via command palette sem digitar manualmente.

### Fase 3 — UI Indicators (1 dia)
11. Modificar `CanvasCardControl.xaml` para mostrar badge AI + modelo quando `AiSessionType != None`
12. Adicionar ícone diferenciado na workspace tree para sessões AI
13. Cor diferente no mini-map para terminais AI

**Resultado:** Visualmente distinguível qual terminal é AI e qual é normal.

### Fase 4 — Terminal Context Actions (1-2 dias)
14. Adicionar "Explicar erro" na command palette que pega output do terminal ativo
15. Adicionar "Copiar output para AI" — abre `cc` com output como contexto
16. Adicionar "Perguntar sobre seleção" — pega texto selecionado do terminal e envia como prompt
17. Ações de right-click no terminal (context menu)

**Resultado:** Fluxo terminal → AI é fluido e natural.

### Fase 5 — Painel lateral como apoio (melhorias)
18. Redesign do `AssistantPanelView` para ser explicitamente "Quick Actions"
19. Botão "Copiar para terminal" em vez de "Executar"
20. Indicador de qual provider está ativo (Ollama local vs OpenAI)
21. Link "Abrir em terminal AI" para conversas complexas

**Resultado:** Painel lateral claramente posicionado como apoio, não como centro.

### Fase 6 — Futuro (não implementar agora)
- Chat contextual por projeto com histórico persistido
- Automações por agent (ex: "rode testes e corrija erros em loop")
- Integração com OpenRouter nativo (sem `cc`)
- Refatoração guiada com diff preview
- Execução autônoma supervisionada

## 5. Arquivos a criar

| Arquivo | Fase |
|---|---|
| `src/CommandDeck/Models/AiModelSlot.cs` | 1 |
| `src/CommandDeck/Models/AiAlias.cs` | 1 |
| `src/CommandDeck/Models/AiCliInfo.cs` | 1 |
| `src/CommandDeck/Models/AiSessionType.cs` | 1 |
| `src/CommandDeck/Services/IAiTerminalService.cs` | 1 |
| `src/CommandDeck/Services/AiTerminalService.cs` | 1 |
| `src/CommandDeck/Services/IAiModelConfigService.cs` | 2 |
| `src/CommandDeck/Services/AiModelConfigService.cs` | 2 |

## 6. Arquivos a modificar

| Arquivo | Mudança | Fase |
|---|---|---|
| `Models/TerminalSessionModel.cs` | Adicionar `AiSessionType`, `AiModelUsed` | 1 |
| `App.xaml.cs` | Registrar `IAiTerminalService` | 1 |
| `Helpers/CommandPaletteRegistrar.cs` | Adicionar comandos AI | 1 |
| `Models/ShellType.cs` | Possivelmente adicionar `Custom` para CLIs AI | 1 |
| `Controls/CanvasCardControl.xaml` | Badge AI | 3 |
| `Views/AssistantPanelView.xaml` | Redesign para "Quick Actions" | 5 |
| `ViewModels/AssistantPanelViewModel.cs` | Botão "Copiar para terminal" | 5 |

## 7. O que NÃO fazer

1. **NÃO criar `ModelRoutingService` automático.** O usuário escolhe o modelo. Ponto.

2. **NÃO criar `AiContextService` complexo.** O `cc`/`claude` já coletam contexto do projeto. O app não precisa duplicar isso. A única "coleta de contexto" que faz sentido é pegar output do terminal para quick-explain.

3. **NÃO criar um novo `IAssistantProvider` para `cc`/`claude`.** O `cc` não é uma API HTTP — é um processo interativo de terminal. Encaixá-lo na interface `IAssistantProvider` (que espera `ChatAsync`/`StreamChatAsync`) seria um square-peg-round-hole. O `IAiTerminalService` é a abstração correta.

4. **NÃO injetar comandos sugeridos direto no terminal.** Sempre copiar para clipboard ou inserir como texto não-executado.

5. **NÃO deletar `OllamaProvider`/`OpenAIProviderStub`.** Continuam funcionando para o painel lateral.

6. **NÃO tentar parsear output do `cc`/`claude`** para extrair respostas estruturadas. O output é para humanos, não para máquinas.

## 8. Decisão técnica: como lançar `cc` via ConPTY

O `ITerminalService.CreateSessionAsync` hoje recebe `ShellType` que mapeia para executável fixo. Para lançar `cc`, há duas abordagens:

### Opção A: Criar ShellType.Custom + parâmetros de comando
Adicionar a `ITerminalService` um overload que aceite executável e argumentos arbitrários:
```csharp
Task<TerminalSession> CreateSessionAsync(
    string executable,
    string arguments,
    string? workingDirectory = null,
    string? projectId = null,
    short columns = 120,
    short rows = 30);
```

**Vantagem:** Genérico, serve para qualquer CLI futuro.
**Desvantagem:** Precisa modificar `ITerminalService` e `TerminalService`.

### Opção B: Lançar via shell existente com comando
Usar `ShellType.WSL` e injetar `cc run opus\n` como primeiro comando após criação.
```csharp
var session = await _terminalSessionService.CreateSessionAsync(ShellType.WSL, cwd);
await _terminalSessionService.WriteAsync(session.Id, "cc run opus\n");
```

**Vantagem:** Zero mudança no `ITerminalService`.
**Desvantagem:** O shell WSL fica como wrapper; se `cc` sair, o terminal volta ao shell (pode ser desejável).

### Recomendação: **Opção B para MVP, migrar para A depois.**
A Opção B funciona imediatamente sem tocar em ConPTY. O fato do terminal "voltar ao shell" se `cc` sair é até um UX positivo — o usuário pode continuar trabalhando.

## 9. Conflitos / riscos de implementação

1. **`TerminalSessionModel` já é grande.** Adicionar `AiSessionType` e `AiModelUsed` é aceitável (2 campos). Não expandir além disso.

2. **Command Palette tem duplicação WIN/WSL.** O `CommandPaletteRegistrar` precisa ser limpo antes de adicionar mais comandos. Os comandos WSL-style (`FilteredCommands`, `NavigateUp`, `NavigateDown`) e WIN-style (`Results`, `MoveUp`, `MoveDown`) coexistem de forma confusa. Resolver isso é pré-requisito de sanidade, mas não é bloqueante para o MVP.

3. **Detecção de CLI.** `IsCliAvailableAsync("cc")` precisa funcionar tanto em Windows nativo quanto em WSL. Em WSL, `which cc` funciona. Em Windows, `where.exe cc` ou `Get-Command cc` no PowerShell. O `AiTerminalService` precisa detectar o shell ativo para escolher o método certo.

4. **Ciclo de DI.** O `AiTerminalService` depende de `ITerminalSessionService`. Não há ciclo, então registro simples funciona.

## 10. Resumo executivo

| Item | Decisão |
|---|---|
| IA no terminal como fluxo principal? | **Sim** |
| `cc` como backend principal? | **Sim** |
| `claude` como fallback? | **Sim** |
| Painel lateral como apoio? | **Sim, manter e redesignar** |
| ModelRoutingService automático? | **Não — quick-switch manual** |
| AiContextService complexo? | **Não — `cc`/`claude` já fazem isso** |
| Novo IAssistantProvider para cc? | **Não — IAiTerminalService separado** |
| Quantos serviços novos? | **2 (AiTerminalService + AiModelConfigService)** |
| MVP em quantos dias? | **1-2 dias (Fase 1)** |
| Arquitetura completa? | **~5-7 dias (Fases 1-5)** |
