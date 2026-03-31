# Plano de Correção — CommandDeck Stabilization Sprint

**Data:** 2026-03-31
**Projeto:** CommandDeck (WPF .NET 8)
**Base:** `/mnt/c/Users/ronyo/Desktop/Rony/Projetos/CommandDeck/`

---

## Objetivo

Corrigir 5 problemas críticos de usabilidade identificados na análise do utilizador.
Cada problema será tratado por um subagent independente.

---

## Contexto / Estado Atual

- Terminal usa `TerminalControl` (hidden TextBox + RichTextBox) com `TranslateKey()` para VT100
- O chat de IA usa `AssistantService` com providers hardcoded (Ollama/OpenAI) via `AssistantProviderType` enum
- Settings salva `AiProvider` ("none"/"openai"/"local") e `AiModel` mas o chat ignora esses valores — usa `AssistantSettings` com `OllamaModel`/`OpenAIModel` fixos
- Card 7 de Settings tem "Modelos IA (Terminal)" com slots (Sonnet/Opus/Haiku/Agent) que são do `IAiModelConfigService` — separados do chat
- Troca de projetos em `SwitchProjectAsync()` salva layout, mata terminais, limpa canvas, recarrega — potencial bottleneck no `SaveCurrentLayoutAsync()` + `LoadLayoutAsync()`
- Nenhum `TextBox` na app usa placeholder/watermark text

---

## Plano por Subagent

### Subagent 1: Terminal — Backspace e Arrow Keys

**Problema:** Backspace e setas não funcionam corretamente no terminal.

**Diagnóstico Preliminar:**
O código em `TerminalControl.cs` parece correto à primeira vista:
- `OnPreviewKeyDown` chama `TranslateKey()` que mapeia `Key.Back → "\x7F"`, arrows → `\x1B[A/B/C/D`
- `e.Handled = true` é setado antes do `await`
- O `HiddenInput` TextBox tem `AcceptsReturn=False`, `AcceptsTab=False`

**Possíveis causas raiz:**
1. O `HiddenInput` TextBox pode estar processando Backspace ANTES do `PreviewKeyDown` chegar ao UserControl pai — o evento `PreviewKeyDown` está registrado no **UserControl**, mas o TextBox é quem tem foco, então o evento tunnel deveria chegar ao UC primeiro... MAS: se o TextBox tiver `IsUndoEnabled=False` e o WPF processar internamente o Backspace no TextBox antes do tunnel reach, pode haver conflito
2. O `HiddenInput` pode acumular caracteres (o `Clear()` só é chamado em `OnPreviewTextInput`, não em `OnPreviewKeyDown`), e o TextBox com conteúdo acumulado pode reagir ao Backspace internamente
3. As arrows podem estar sendo capturadas pelo sistema de navegação do WPF (`KeyboardNavigation.DirectionalNavigation=None` está no UC mas não no TextBox)
4. Focus leak: outro elemento pode estar recebendo o foco em vez do `HiddenInput`

**Ações:**
1. Adicionar `PreviewKeyDown` handler diretamente no `HiddenInput` TextBox (em vez de no UserControl) para garantir intercepção antes do TextBox processar
2. No `OnPreviewKeyDown`, chamar `HiddenInput.Clear()` para prevenir acumulação de texto
3. Adicionar `KeyboardNavigation.DirectionalNavigation="None"` no `HiddenInput`
4. Considerar trocar `HiddenInput` de TextBox para um custom control que não processa keys internamente
5. Adicionar debug logging para verificar se os eventos estão de facto a chegar

**Arquivos:**
- `Controls/TerminalControl.xaml` — adicionar atributos no HiddenInput
- `Controls/TerminalControl.xaml.cs` — refatorar event handling
- `Controls/CanvasCardControl.xaml.cs` — verificar se foco é redirecionado corretamente

**Validação:**
- Abrir terminal, digitar texto, Backspace apaga caracteres
- Setas esquerda/direita movem cursor na linha de comando
- Seta cima/baixo navegam histórico do shell
- Ctrl+C, Ctrl+D, Tab funcionam

---

### Subagent 2: Performance — Troca de Projetos e Latência do Chat

**Problema:** Latência ao alternar entre projetos e ao enviar mensagens no chat.

**Diagnóstico Preliminar:**
- `SwitchProjectAsync()` em `MainViewModel.cs:428` faz sequencialmente: save layout → dispose terminais → clear canvas → update settings → get git info → load layout → restore terminais
- Cada terminal restore chama `InitializeAsync()` que spawna processo ConPTY
- `ExecuteWithProviderGuardAsync()` no chat faz `RefreshProviderInfoAsync()` ANTES de cada mensagem (network call)
- O timer de 15s em `AssistantPanelViewModel` faz polling de availability

**Ações:**
1. **SwitchProjectAsync:** Paralelizar operações independentes (save settings + get git info em paralelo; restore terminals em paralelo com `Task.WhenAll`)
2. **SwitchProjectAsync:** Adicionar indicador de progresso (StatusBarText durante cada fase)
3. **Chat:** Cachear resultado de `IsAvailable` por 30s em vez de verificar antes de cada mensagem; remover o `await RefreshProviderInfoAsync()` síncrono do guard
4. **Chat:** O polling de 15s pode ser aumentado para 60s ou só verificar quando o painel abre
5. **Git:** Verificar se `GetGitInfoAsync` é rápido o suficiente ou se precisa de cache

**Arquivos:**
- `ViewModels/MainViewModel.cs` — `SwitchProjectAsync()` optimization
- `ViewModels/AssistantPanelViewModel.cs` — cache de availability, timer reduction
- `ViewModels/TerminalCanvasViewModel.cs` — verificar `SaveCurrentLayoutAsync`/`LoadLayoutAsync`

**Validação:**
- Medir tempo de troca de projeto antes/depois (target: <500ms para projeto com 3 terminais)
- Chat deve responder imediatamente sem delay de provider check
- UI não trava durante troca de projeto

---

### Subagent 3: IA — Settings Desconectadas do Chat

**Problema:** Os modelos configurados em Settings não afetam o chat. O chat usa providers fixos (Ollama/OpenAI) hardcoded.

**Diagnóstico Preliminar:**
- `SettingsViewModel` salva `AiProvider` ("none"/"openai"/"local") e `AiModel` (string) em `AppSettings`
- `AssistantService` usa `AssistantSettings` com propriedades separadas: `ActiveProvider` (enum), `OllamaModel`, `OpenAIModel`
- Há dois sistemas paralelos de config de IA:
  - Card 6 (Settings): `AiProvider` / `AiModel` / `AiBaseUrl` / `AiApiKey` → salvos em `AppSettings` via `SettingsService`
  - Card 7 (Settings): `AiModelSlots` (Sonnet/Opus/Haiku/Agent) → salvos via `IAiModelConfigService`
  - Chat panel: usa `AssistantService` que opera com `AssistantProviderType` enum (Ollama/OpenAI)
- **Nenhum código conecta o Card 6 settings ao `AssistantService`** — são mundos separados
- O ComboBox no `AssistantPanelView` tem items hardcoded (Ollama/OpenAI), não usa o que está em Settings

**Ações:**
1. **Unificar config:** Quando settings são salvos, propagar `AiProvider`/`AiModel`/`AiBaseUrl`/`AiApiKey` para `AssistantService`/`AssistantSettings`
2. **Mapear providers:** "openai" → `AssistantProviderType.OpenAI`, "local" → `AssistantProviderType.Ollama`, "none" → desabilitar
3. **Propagar modelo:** O `AiModel` de settings deve ser usado pelo provider ativo (não o valor hardcoded)
4. **Remover provider selector hardcoded** do `AssistantPanelView` — usar o que vem de Settings
5. **Ou:** Manter o selector no chat panel mas sincronizar bidireccionalmente com Settings
6. **Simplificar:** Avaliar se Card 7 (Model Slots) deve alimentar o chat ao invés de ser separado

**Arquivos:**
- `Services/AssistantService.cs` — aceitar config de Settings
- `Models/AssistantModels.cs` — verificar `AssistantSettings` class
- `ViewModels/AssistantPanelViewModel.cs` — sincronizar com Settings
- `Views/AssistantPanelView.xaml` — remover/ajustar ComboBox hardcoded
- `App.xaml.cs` — verificar wiring de DI

**Validação:**
- Configurar provider como "openai" + modelo "gpt-4o" em Settings → chat usa esse provider/modelo
- Configurar como "local" + modelo "llama3" → chat usa Ollama com esse modelo
- Configurar como "none" → chat desabilitado com mensagem clara

---

### Subagent 4: UI — Placeholders e Layout de Settings

**Problema:** Campos de entrada sem placeholders; tela de Settings precisa ser mais larga.

**Diagnóstico Preliminar:**
- Nenhum TextBox na app usa `Tag` ou watermark para placeholder
- `SettingsView.xaml` tem `MaxWidth="900"` com `Margin="32,24"`
- O campo de input do chat (`AssistantPanelView`) também não tem placeholder (só ToolTip)
- O `DarkTextBox` style provavelmente não inclui watermark behavior

**Ações:**
1. **Criar WatermarkBehavior** ou usar `Tag` property com style trigger para mostrar texto hint quando TextBox está vazio
2. **Adicionar ao DarkTextBox style** em `Styles.xaml`: template com TextBlock placeholder que some quando há texto
3. **Auditar TODOS os TextBox/PasswordBox** da app e adicionar placeholders apropriados:
   - Settings: "Cascadia Code" (font), "14" (size), "C:\Users\..." (scan dir), etc.
   - Chat input: "Digite uma pergunta..."
   - Project edit: placeholders nos campos
   - Command palette search: "Buscar comando..."
4. **Settings layout:** Aumentar `MaxWidth` de `900` para `1100` e reduzir margin lateral

**Arquivos para auditar e editar:**
- `Resources/Styles.xaml` — DarkTextBox style (adicionar watermark template)
- `Views/SettingsView.xaml` — MaxWidth + placeholders em cada campo
- `Views/AssistantPanelView.xaml` — placeholder no chat input
- `Views/ProjectEditView.xaml` — placeholders
- `Views/CommandPaletteView.xaml` — placeholder na busca
- `Controls/TerminalControl.xaml` — N/A (é terminal, não input)

**Validação:**
- Todo campo de texto vazio mostra hint text em cor suave
- Settings ocupa mais largura e os campos não ficam espremidos
- Placeholders somem ao digitar e reaparecem ao limpar

---

### Subagent 5: QA — Validação Integrada

**Problema:** Garantir que as correções dos 4 subagents não quebraram nada.

**Ações:**
1. `dotnet build` sem erros
2. Verificar se todos os bindings XAML estão válidos (sem typos em property names)
3. Verificar que DI em `App.xaml.cs` registra todos os novos serviços/dependências
4. Code review dos diffs para verificar consistência de patterns (MVVM, naming, etc.)
5. Compilar lista de testes manuais para o Rony executar

**Arquivos:**
- Todos os modificados pelos outros subagents

---

## Ordem de Execução

```
[Subagent 1: Terminal]  ──┐
[Subagent 2: Performance] ──┼── paralelo (sem dependências entre si)
[Subagent 3: IA Config]  ──┤
[Subagent 4: UI/Placeholders]──┘
                              │
                              ▼
                    [Subagent 5: QA Build + Review]
```

Subagents 1–4 podem rodar em paralelo (max 3 simultâneos pelo sistema, então 3 + 1 sequencial).
Subagent 5 roda depois para validar o build.

---

## Riscos e Tradeoffs

1. **Terminal focus:** A solução de mover o handler para o HiddenInput pode quebrar Ctrl+shortcuts globais da MainWindow. Testar atalhos (Ctrl+Shift+T, Ctrl+B, etc.)
2. **IA unificação:** Conectar Settings ao chat pode criar race conditions se o user mudar settings enquanto o chat está streamando. Usar lock ou immutable snapshot.
3. **Performance:** Paralelizar restore de terminais pode causar picos de CPU/RAM ao spawnar múltiplos ConPTY simultaneamente. Limitar a 3 paralelos.
4. **Placeholders:** Se o DarkTextBox style for usado em contextos onde watermark é indesejável, o behavior deve ser opt-in (via attached property, não automático).
5. **Settings width:** Aumentar para 1100 pode não funcionar bem em monitores 1366×768. Considerar usar MinWidth/MaxWidth responsivo.

---

## Open Questions

- O `AssistantSettings` class é populado de onde? Se vier de DI configuration, precisa ser atualizado em runtime quando Settings muda.
- O Card 7 (Model Slots Sonnet/Opus/Haiku/Agent) deve ser integrado ao chat panel ou são para o sistema de AI Terminal sessions (context menu do CanvasCard)?
- Existe algum teste automatizado no projeto? Se sim, onde?
