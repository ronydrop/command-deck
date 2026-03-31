# CommandDeck — Browser Embutido + Element Picker + AI Context
## Plano Consolidado de Implementação

**Data:** 31/03/2026
**Tipo:** Análise arquitetural + plano de implementação
**Método:** Multi-specialist analysis (Desktop/WebView Engineer + DOM Inspection Engineer + AI Context/Security/UX Engineer)

---

## 1. Estado Atual do Projeto

### O que já existe e funciona

O CommandDeck já possui uma **Fase 1 parcialmente implementada**:

| Componente | Status | Localização |
|---|---|---|
| WebView2 NuGet | ✅ Instalado | `Microsoft.Web.WebView2 1.0.2903.40` no .csproj |
| BrowserRuntimeService | ✅ Funcional | `Services/Browser/BrowserRuntimeService.cs` — Init, navigate, back/forward, reload, ExecuteScript, CaptureScreenshot, WebMessageReceived |
| BrowserViewModel | ✅ Funcional | `ViewModels/BrowserViewModel.cs` — URL bar, state management, DetectAndNavigate |
| BrowserView.xaml | ✅ Funcional | `Views/BrowserView.xaml` — Toolbar, address bar, WebView2 control, status bar, overlays |
| IBrowserRuntimeService | ✅ Interface completa | `Services/Browser/IBrowserRuntimeService.cs` — 11 membros |
| LocalAppSessionService | ✅ Funcional | `Services/Browser/LocalAppSessionService.cs` — Detecta portas via package.json, .env, common ports |
| BrowserSession model | ✅ Básico | `Models/Browser/BrowserSession.cs` — Id, ProjectId, Url, Port, State, IsPickerActive |
| AgentTarget model | ✅ Básico | `Models/Browser/AgentTarget.cs` — Type(Assistant/Terminal), SessionId, DisplayName |
| DI Registration | ✅ Registrado | `App.xaml.cs` — IBrowserRuntimeService, ILocalAppSessionService, BrowserViewModel |
| Segurança básica | ✅ Implementada | Localhost-only navigation filter, script dialogs disabled, host objects blocked |

### Infraestrutura AI existente (para integração)

| Serviço | Propósito | Relevância |
|---|---|---|
| IAgentSelectorService | Lista agents, ActiveAgent, SelectAgent() | Alta — escolher destino do contexto |
| IAiContextService | BuildPromptAsync, AiPromptIntent enum | Alta — estender para ElementContext |
| IAiTerminalService | Gerencia sessões AI terminal (CC, Claude) | Alta — injetar contexto em terminals |
| IAssistantService | Chat panel AI (Ollama/OpenAI) | Alta — receber contexto como mensagem |
| ITerminalService | Write para terminais ConPTY | Alta — colar contexto formatado |
| INotificationService | Toasts de notificação | Média — feedback de envio |
| IPersistenceService | SQLite persistence | Média — histórico de seleções |
| AgentSelectorViewModel | UI para escolher agent | Alta — reusar para seleção de destino |
| AgentDefinition | Id, Name, Icon, SessionType, ModelOrAlias | Alta — metadata do agent alvo |

### BROWSER_FEATURE_PLAN.md existente

Já existe um plano de 1300 linhas (`/BROWSER_FEATURE_PLAN.md`) com arquitetura, modelos de dados, fases, e JavaScript do element picker documentados. Este plano **consolida, valida e complementa** aquele documento com descobertas técnicas dos subagents especializados.

---

## 2. Gap Analysis — O Que Falta

### 2.1 Gaps Críticos no Código Existente (P0)

| GAP | Problema | Impacto |
|---|---|---|
| **GAP-01** | WebView2 init sem `WebView2RuntimeNotFoundException` handling | Win10 sem Edge = crash |
| **GAP-02** | Security incompleta — falta WebResourceRequested, NewWindowRequested, DownloadStarting, ProcessFailed handlers | Requests externos via fetch/iframes passam; links target=_blank abrem browser externo; downloads descontrolados |
| **GAP-03** | ExecuteScriptAsync sem AddScriptToExecuteOnDocumentCreatedAsync | Picker JS precisa ser re-injetado em cada navegação |
| **GAP-04** | CaptureScreenshotAsync só viewport (CapturePreviewAsync) | Precisa CDP para screenshot de elemento específico |
| **GAP-05** | WebMessageReceived sem protocolo definido, sem rate limiting | Payload sem schema; página maliciosa pode spammar |
| **GAP-06** | Airspace issue — WPF overlays são invisíveis sobre WebView2 HWND | Element picker DEVE ser HTML/JS injetado, NÃO overlay WPF |
| **GAP-07** | Keyboard focus — WebView2 captura atalhos globais do app | Ctrl+B, Ctrl+Shift+C não funcionam quando WebView2 tem foco |

### 2.2 Features Ausentes (P1-P2)

| Feature | Prioridade | Estimativa |
|---|---|---|
| Element Picker (JS injection + DOM serialization + overlay) | P1 | 8-12h |
| DomSelectionService (C# bridge) | P1 | 4h |
| ElementContextBuilder + Formatter | P1 | 4h |
| AiContextRouter (routing para assistant/terminal) | P1 | 4h |
| Agent Target Selector UI (popup pós-seleção) | P1 | 3h |
| CDP Service abstraction | P1 | 4h |
| Element screenshots via CDP | P2 | 3h |
| Health check periódico + auto-reconnect | P2 | 6h |
| Code mapping (React fiber, data-testid, heurísticas) | P2 | 6h |
| Selection history (SQLite + UI) | P2 | 4h |
| Multi-tab (dispose+recreate) | P3 | 6h |

---

## 3. Arquitetura Técnica Recomendada

### 3.1 Abordagem para Browser Embutido: WebView2 (confirmado)

Análise já feita no plano anterior e validada pelo Desktop Engineer. WebView2 é a **única opção viável** para WPF .NET 8:

- **CefSharp**: +300MB, x86/x64 only, sem AnyCPU → descartado
- **WebBrowser (IE)**: Deprecated, IE11 → descartado
- **WebView2**: Runtime no Win11, NuGet leve, CDP completo, sandbox Chromium, AnyCPU

### 3.2 Element Picker: JavaScript Injetado (não CDP Overlay)

O CDP `Overlay.setInspectMode` **não é viável** como método principal porque:
1. Não permite customização do visual (cores, tooltip, box model)
2. Não serializa o DOM automaticamente
3. Não suporta framework detection (React fiber, Vue instance)

**Abordagem**: JavaScript IIFE injetado via `ExecuteScriptAsync`, comunicando via `window.chrome.webview.postMessage()`.

### 3.3 Diagrama de Comunicação

```
┌──────────────────────────────────────────────────────────────────────────┐
│                     CommandDeck (WPF .NET 8)                         │
│                                                                          │
│  ┌──────────────┐   ┌──────────────────────┐   ┌─────────────────────┐  │
│  │ Terminal      │   │ Browser Panel        │   │ Assistant Panel     │  │
│  │ Canvas        │   │                      │   │                     │  │
│  │              │   │  ┌──────────────────┐ │   │  Chat + Context     │  │
│  │ ConPTY        │   │  │  WebView2        │ │   │  Display            │  │
│  │ Sessions      │   │  │  (localhost)     │ │   │                     │  │
│  │              │   │  │                  │ │   │                     │  │
│  │              │   │  │  element-picker  │ │   │                     │  │
│  │              │   │  │  .js (IIFE)      │ │   │                     │  │
│  │              │   │  └────────┬─────────┘ │   │                     │  │
│  │              │   │  [🔍 Select Element]  │   │                     │  │
│  └──────┬───────┘   └──────────┬────────────┘   └─────────┬───────────┘  │
│         │                      │                           │              │
│         │       ┌──────────────┴────────────────┐          │              │
│         │       │  WebMessageReceived (JSON)     │          │              │
│         │       │  PickerMessage protocol        │          │              │
│         │       └──────────────┬────────────────┘          │              │
│         │                      │                           │              │
│         │       ┌──────────────┴────────────────┐          │              │
│         │       │  DomSelectionService           │          │              │
│         │       │  Parse → ElementCaptureData    │          │              │
│         │       └──────────────┬────────────────┘          │              │
│         │                      │                           │              │
│         │       ┌──────────────┴────────────────┐          │              │
│         │       │  ElementContextBuilder         │          │              │
│         │       │  Enrich → ElementContext        │          │              │
│         │       │  (screenshots, code mapping)   │          │              │
│         │       └──────────────┬────────────────┘          │              │
│         │                      │                           │              │
│         │       ┌──────────────┴────────────────┐          │              │
│         │       │  AiContextRouter               │          │              │
│         │       │  Route to target agent         │          │              │
│         │       └────────┬──────────┬───────────┘          │              │
│         │                │          │                       │              │
│         ├────────────────┘          └───────────────────────┘              │
│         │                                                                 │
│  ┌──────┴──────────────────────────────────────────────────────────────┐  │
│  │              Existing AI Services Layer                              │  │
│  │  IAiContextService │ IAssistantService │ IAiTerminalService         │  │
│  │  ITerminalService  │ IAgentSelectorService │ INotificationService   │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────┘
```

### 3.4 Fluxo de Dados End-to-End

```
1. User clica "🔍 Select Element" (ou Ctrl+Shift+C)
2. C# chama: ExecuteScriptAsync("window.__ELEMENT_PICKER__.activate()")
3. JS: cursor → crosshair, overlay → visible, events → capture phase
4. User hover sobre elementos → JS highlight com box model (margin/padding/border/content)
5. User clica no elemento
6. JS: serializa DOM (tag, attributes, HTML, selector, ancestors, framework, accessibility)
7. JS: postMessage({type:"picker", action:"element-selected", payload: {...}})
8. C#: WebMessageReceived → DomSelectionService.HandleMessage()
9. C#: DomSelectionService → parse JSON → ElementCaptureData
10. C#: ElementContextBuilder.Build() → enrich com project info, code mapping, screenshot
11. C#: UI mostra AgentTargetPopup → user escolhe: Assistant ou Terminal X
12. C#: AiContextRouter.RouteToAgentAsync(context, target, intent)
    a. Assistant → WeakReferenceMessenger → AssistantPanelViewModel.ReceiveElementContext()
    b. Terminal → ElementContextFormatter.FormatForTerminal() → TerminalService.WriteAsync()
13. C#: NotificationService.Notify("Contexto enviado para {agent}")
14. C#: PersistenceService.SaveElementSelectionAsync() → SQLite history
```

---

## 4. Element Picker — Arquitetura JavaScript

### 4.1 Estrutura do Script

```javascript
(function() {
  'use strict';
  
  // Double-injection guard
  if (window.__ELEMENT_PICKER__ && window.__ELEMENT_PICKER__._active) {
    window.__ELEMENT_PICKER__.deactivate();
    return;
  }

  const MessageBus = { /* postMessage protocol */ };
  const FrameworkDetector = { /* React/Vue/Angular/Svelte detection */ };
  const SelectorGenerator = { /* 8 strategies with scoring */ };
  const DOMSerializer = { /* Full element serialization */ };
  const OverlayRenderer = { /* Box model overlay + tooltip */ };
  const IframeHandler = { /* Same-origin iframe traversal */ };
  const KeyboardNav = { /* Arrow keys DOM navigation */ };
  const DOMWatcher = { /* MutationObserver for changes */ };
  const Picker = { /* Main controller */ };

  window.__ELEMENT_PICKER__ = {
    activate: () => Picker.activate(),
    deactivate: () => Picker.deactivate(),
    inspectSelector: (sel) => Picker.inspectSelector(sel),
    getPageInfo: () => Picker.getPageInfo(),
    _active: false
  };
})();
```

### 4.2 Overlay Visual

```
┌─── Margin (rgba(246,178,107,0.3)) ────────────────────┐
│  ┌─── Border (rgba(255,229,153,0.65)) ──────────────┐ │
│  │  ┌─── Padding (rgba(147,196,125,0.4)) ─────────┐ │ │
│  │  │  ┌─── Content (rgba(111,168,220,0.4)) ─────┐ │ │ │
│  │  │  │                                          │ │ │ │
│  │  │  └──────────────────────────────────────────┘ │ │ │
│  │  └────────────────────────────────────────────────┘ │ │
│  └──────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────┘

Tooltip: [button#submit.btn-primary | 120×40]
```

- `position: fixed; pointer-events: none; z-index: 2147483647`
- Appended to `document.documentElement` (não body)
- Box model calculado via `getComputedStyle()`
- Tooltip com edge detection (flip above/below/clamp)

### 4.3 Event Handling (Capture Phase)

Todos os listeners em **capture phase** para interceptar antes da app:

```javascript
document.addEventListener('mousemove', handler, true);  // Capture
document.addEventListener('click', handler, true);        // Capture
// click handler: preventDefault + stopPropagation + stopImmediatePropagation
```

- `requestAnimationFrame` throttle no mousemove (~60fps)
- Overlay hidden durante `elementFromPoint` (single-frame toggle)
- Hover = lightweight (tag + rect only), Selection = full serialization

### 4.4 Selector Generation — 8 Estratégias

| Score | Estratégia | Estabilidade | Exemplo |
|---|---|---|---|
| 100 | data-testid | Highest | `[data-testid="login-btn"]` |
| 90 | ID (hand-written) | High | `#main-header` |
| 85 | role + aria-label | High | `[role="button"][aria-label="Save"]` |
| 80 | aria-label | High | `[aria-label="Close dialog"]` |
| 75 | name (forms) | High | `input[name="email"]` |
| 60 | Class combo | Medium | `button.btn.btn-primary` |
| 45 | Text XPath | Medium | `//button[contains(text(),"OK")]` |
| 30 | nth-of-type path | Low | `div > ul > li:nth-of-type(3)` |

Cada selector validado: `querySelectorAll(sel).length === 1`. Auto-generated IDs (ember, react, vue, hash) penalizados. CSS module hashes filtrados.

### 4.5 Framework Detection

| Framework | Detecção Global | Detecção Per-Element |
|---|---|---|
| React | `__REACT_DEVTOOLS_GLOBAL_HOOK__` | `__reactFiber$` → fiber.type.name |
| Vue 2 | `window.Vue` | `element.__vue__.$options.name` |
| Vue 3 | `window.__VUE__` | `element.__vue_parentComponent.type.name` |
| Angular | `window.ng` | `ng.getComponent(element).constructor.name` |
| Svelte | — | `element.$$`, `class*="svelte-"` |
| Next.js | `window.__NEXT_DATA__` | (via React fiber) |
| Nuxt.js | `window.__NUXT__` | (via Vue instance) |

### 4.6 WebMessage Protocol

```json
{
  "type": "picker|dom|framework|error|chunk",
  "action": "element-selected|hover|activated|deactivated|js-error",
  "payload": { ... },
  "timestamp": 1711857600000
}
```

Chunking para payloads >512KB (split JSON string, reassemble em C#).

### 4.7 Edge Cases Cobertos

| Case | Solução |
|---|---|
| Shadow DOM (open) | `element.shadowRoot.elementFromPoint()` |
| Shadow DOM (closed) | Report host element only |
| Cross-origin iframe | Report `<iframe>` como elemento opaco com src |
| Same-origin iframe | Traverse via `contentDocument` |
| SVG elements | `getBBox()` + `getScreenCTM()` |
| Canvas | Flag "No DOM inside canvas" |
| Virtualized lists | Heuristic: scrollHeight > clientHeight*3 && children < 50 |
| contenteditable | Capture selection state |
| MutationObserver | Detect selected element removal |
| Scroll during picker | Re-highlight on scroll event |
| z-index wars | Max int32 + documentElement attachment |

---

## 5. AI Context Integration

### 5.1 ElementContext Model (enriquecido)

```csharp
public sealed class ElementContext
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;
    public ElementCaptureData RawCapture { get; init; } = new();
    public string PageUrl { get; init; } = string.Empty;
    public string PageTitle { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string? ProjectPath { get; init; }
    public CodeMappingResult? CodeMapping { get; init; }
    public string? ScreenshotBase64 { get; init; }
    public List<string> ConsoleErrors { get; init; } = new();
    public bool WasSanitized { get; init; }
}
```

### 5.2 Dois Formatos de Output

**Terminal agents (plain text, 8KB max):**
```
== ELEMENT CONTEXT [ANALYZE] ==

Tag: <button>
ID: #submit-btn
Classes: .btn .btn-primary
Selector: [data-testid="submit-btn"]
Page: http://localhost:3000/checkout

HTML:
<button id="submit-btn" class="btn btn-primary" type="submit">
  <span>Enviar pedido</span>
</button>

Component: CheckoutForm (React)
Probable source file: src/components/CheckoutForm.tsx
Confidence: 85%

Analyze this HTML element. Explain its purpose, structure, and any issues.

== END ELEMENT CONTEXT ==
```

**Assistant panel (Markdown com metadata):** Card colapsável no chat com syntax highlight, screenshot thumbnail, botões inline "Re-enviar", "Copiar HTML".

### 5.3 AiContextRouter

```
IAiContextRouter
├── RouteToAgentAsync(context, target, intent)
│   ├── target.Type == Assistant → WeakReferenceMessenger → AssistantPanelViewModel
│   └── target.Type == Terminal  → Formatter → TerminalService.WriteAsync()
├── GetAvailableTargets() → [Assistant, Active Agent, All AI Terminals]
└── ResendContextAsync(selectionId, newTarget) → reuso do histórico
```

### 5.4 Novos AiPromptIntent

```csharp
// Adicionar ao enum existente
AnalyzeElement,     // Análise geral do elemento
FixElementBug,      // Identificar e corrigir bug
ImproveElementUX,   // Sugestões de UX/a11y
LocateElementCode,  // Encontrar arquivo fonte
```

### 5.5 Terminal Injection Rules

- Sanitizar: strip ANSI escapes, control chars (exceto \n, \t)
- Chunked write: 1KB chunks com 10ms delay (ConPTY buffer)
- Max 8KB total (dentro do context window da maioria dos LLMs)
- Prefixar com `\n` para não colar no meio de prompt existente

---

## 6. Segurança

### 6.1 Medidas Implementadas

| Medida | Status | Detalhes |
|---|---|---|
| Localhost-only navigation | ✅ Existe | NavigationStarting cancela non-localhost |
| Script dialogs disabled | ✅ Existe | AreDefaultScriptDialogsEnabled = false |
| Host objects blocked | ✅ Existe | AreHostObjectsAllowed = false |

### 6.2 Medidas a Implementar

| Medida | Prioridade | Detalhes |
|---|---|---|
| WebResourceRequested filter | P0 | Bloquear fetch/XHR para hosts externos |
| NewWindowRequested handler | P0 | Cancelar ou redirecionar links target=_blank |
| DownloadStarting handler | P0 | Bloquear downloads |
| ProcessFailed handler | P0 | Crash recovery (renderer, GPU, browser) |
| Password field filtering | P1 | Nunca capturar values de input[type=password] |
| Token/secret detection | P1 | Regex para JWT, API keys em atributos |
| WebMessage rate limiting | P1 | Sliding window 10msg/5s |
| HTML sanitization | P1 | Strip inline scripts, truncar 50KB |
| Context menu control | P2 | AreDefaultContextMenusEnabled = false |
| Browser accelerator keys | P2 | AreBrowserAcceleratorKeysEnabled = false |

### 6.3 Sanitização de Dados Capturados

```csharp
// ElementSanitizer — antes de enviar para AI
1. input[type=password] → value redacted
2. Regex: JWT (eyJ...), API keys (sk-..., pk_...) → [REDACTED]
3. Atributos sensíveis: data-token, data-secret, Authorization → removed
4. Script tags no innerHTML → stripped
5. outerHTML > 50KB → truncated
6. Total payload > 100KB → trimmed
```

---

## 7. Performance

### 7.1 Custos e Mitigações

| Operação | Custo | Mitigação |
|---|---|---|
| WebView2 aberto | ~100-200MB RAM | Lazy init, criar só quando tab Browser abre |
| Hover highlight | <1ms por frame | requestAnimationFrame throttle |
| Full serialization | 5-30ms | Só no click, não no hover |
| Selector generation | 1-10ms | 8 strategies parallelizable |
| Screenshot viewport | 50-200ms | CDP async, não bloqueia UI |
| Screenshot element | 20-100ms | CDP clip region |
| Code mapping search | 100-500ms | Background thread, cache de file index |
| Payload para AI | 10-100KB | Limites estritos, truncamento |

### 7.2 Estratégia Multi-Projeto

**Dispose+Recreate** (recomendado, não pool):
- 1 WebView2 ativo por vez → ~120MB vs ~400MB para 3 tabs
- ~300-800ms para trocar de projeto
- CoreWebView2Environment pré-criado para warmup
- Overlay "Loading project..." durante transição

### 7.3 UserDataFolder por Projeto

```
%LOCALAPPDATA%/CommandDeck/WebView2Data/{ProjectId}/
```
- Isolamento completo de cookies, localStorage, cache
- Cleanup: ao deletar projeto, ao startup (pastas órfãs), periódico (>1GB)
- File locks: dispose WebView2 → wait 2s → delete com retry

---

## 8. UX / Produto

### 8.1 Layout

O browser é **tab peer** no conteúdo central (ao lado de Terminal Canvas e Dashboard):

```
┌────────────────────────────────────────────────────────────────────┐
│  [Dashboard] [Processes] [⚙]                    [🤖 AI] [🌐]     │
├─────────┬──────────────────────────────────────────┬───────────────┤
│         │ [Terminal Canvas] [🌐 Browser] [+]       │               │
│ Sidebar │                                          │ Assistant     │
│ (Proj)  │ ┌─ Browser Toolbar ────────────────────┐ │ Panel         │
│         │ │ [← →] [🔄] [🔍] [http://localhost___]│ │               │
│         │ └──────────────────────────────────────┘ │ ┌───────────┐ │
│         │ ┌──────────────────────────────────────┐ │ │ Chat      │ │
│         │ │                                      │ │ │           │ │
│         │ │        WebView2 Content              │ │ │ Element   │ │
│         │ │        (localhost app)                │ │ │ Context   │ │
│         │ │                                      │ │ │ Preview   │ │
│         │ └──────────────────────────────────────┘ │ │           │ │
│         │ [● localhost:3000] [🔍 Picker active]    │ └───────────┘ │
├─────────┴──────────────────────────────────────────┴───────────────┤
│  Status Bar                                                        │
└────────────────────────────────────────────────────────────────────┘
```

### 8.2 Atalhos de Teclado

| Atalho | Ação |
|---|---|
| `Ctrl+B` | Toggle browser panel |
| `Ctrl+Shift+C` | Toggle element picker |
| `Escape` | Cancelar seleção / sair do WebView2 focus |
| `Enter` (com elemento) | Enviar para agent padrão |
| `F5` | Reload página |
| `Ctrl+L` | Focar URL bar |
| `↑↓←→` (picker ativo) | Navegar DOM tree |

### 8.3 Flow Pós-Seleção

```
Elemento selecionado → Popup aparece:
┌─────────────────────────────────────┐
│  Enviar contexto para:              │
│                                     │
│  🤖 Assistant AI          [Enter]   │
│  ─────────────────────              │
│  🟣 Claude Code (Sonnet)  [1]      │
│  🔵 CC Agent (Opus)       [2]      │
│  📋 Copiar para clipboard [C]      │
│                                     │
│  ── Intent ─────────────            │
│  🔍 Analisar    🐛 Fix Bug         │
│  ✨ Melhorar UX  📁 Localizar       │
└─────────────────────────────────────┘
```

### 8.4 Cores (Catppuccin Mocha)

- Hover highlight: `#89b4fa` (Blue) com 40% opacity
- Selected: `#cba6f7` (Mauve) com 50% opacity
- Margin box: `#fab387` (Peach) com 30% opacity
- Padding box: `#a6e3a1` (Green) com 30% opacity
- Tooltip: `#1e1e2e` (Base) com `#cdd6f4` (Text) border

---

## 9. WebView2 Risks Específicos de WPF

### 9.1 Airspace Problem (CRÍTICO)

WebView2 usa HWND nativo — qualquer WPF overlay sobre ele é **invisível**.

**Impacto direto**: Element picker overlay DEVE ser HTML/JS dentro do WebView2, não WPF.

**Solução para overlays WPF**: Usar `Visibility.Collapsed` no WebView2 quando mostrar overlay WPF (ex: disconnected state). Nunca Z-stack WPF sobre WebView2.

### 9.2 Keyboard Focus

WebView2 captura keyboard focus e não devolve facilmente.

**Solução**: `AcceleratorKeyPressed` handler que intercepta atalhos do app (Ctrl+B, Ctrl+T, Escape) e redireciona para WPF. `AreBrowserAcceleratorKeysEnabled = false`.

### 9.3 DPI / Multi-Monitor

Pode causar conteúdo borrado ao mover entre monitores.

**Solução**: `PerMonitorV2` no manifest + resize hack no `OnDpiChanged`.

### 9.4 Safe Dispose

```csharp
// CORRETO:
1. Navigate to about:blank
2. await Task.Delay(100)
3. Remove all event handlers
4. Remove from visual tree (parent.Children.Remove)
5. webView.Dispose()
// Forçar GC após dispose para liberar COM objects
```

---

## 10. Code Mapping (Elemento → Código Fonte)

### Estratégia em 4 Camadas

| Camada | Método | Confiança | Quando |
|---|---|---|---|
| 1 | React Fiber → componentName → buscar arquivo | 80-90% | React dev mode |
| 2 | data-testid → PascalCase → buscar arquivo | 85% | Apps com test attrs |
| 3 | CSS classes → pattern match → buscar em *.tsx/jsx/vue | 40% | Sempre |
| 4 | Text content → search em arquivos do framework | 30% | Fallback |

**File search**: `git ls-files` (rápido, respeita .gitignore) com fallback para filesystem. Ignorar: node_modules, dist, build, .next, .nuxt.

**Confidence boosting**: +15% quando múltiplas estratégias encontram o mesmo arquivo.

---

## 11. Modelo de Dados Completo

### Novos Models

```
Models/Browser/
├── ElementCaptureData.cs      ← Raw data do JS (tag, attrs, html, selector, ancestry, framework)
├── ElementContext.cs           ← Enriched context (+ project info, code mapping, screenshot)
├── CodeMappingResult.cs        ← File path, line, confidence, strategy
├── PickerMessage.cs            ← WebMessage protocol (type, action, payload, timestamp)
├── ElementSelectionRecord.cs   ← SQLite persistence record
├── PageInspectionState.cs      ← URL, title, picker active, console log, network log
└── LinkedProjectRuntime.cs     ← ProjectId, port, process name, pid
```

### SQLite Table

```sql
CREATE TABLE element_selections (
    id              TEXT PRIMARY KEY,
    tag_name        TEXT NOT NULL,
    css_selector    TEXT NOT NULL,
    page_url        TEXT NOT NULL,
    project_id      TEXT,
    outer_html      TEXT,
    component_name  TEXT,
    code_file_path  TEXT,
    target_agent    TEXT NOT NULL,
    intent          TEXT NOT NULL,
    captured_at     TEXT NOT NULL,
    CHECK(length(outer_html) <= 50000)
);
```

---

## 12. Novos Serviços e Arquivos

### Services/Browser/ (novos)

| Arquivo | Responsabilidade |
|---|---|
| IDomSelectionService.cs | Inject picker JS, activate/deactivate, handle WebMessages |
| DomSelectionService.cs | Implementação |
| IElementContextBuilder.cs | ElementCaptureData → ElementContext (enrich) |
| ElementContextBuilder.cs | Implementação |
| IAiContextRouter.cs | Route ElementContext para agent target |
| AiContextRouter.cs | Implementação |
| ICodeMappingService.cs | Elemento → arquivo fonte (4 camadas) |
| CodeMappingService.cs | Implementação |
| ISelectionHistoryService.cs | CRUD histórico de seleções (SQLite) |
| SelectionHistoryService.cs | Implementação |
| ElementContextFormatter.cs | FormatForTerminal() / FormatForAssistant() |
| ElementSanitizer.cs | Sanitização de dados capturados |
| ICdpService.cs | Chrome DevTools Protocol abstraction |
| CdpService.cs | Implementação |
| PortHealthCheckService.cs | TCP health check periódico |
| WebView2RuntimeChecker.cs | Runtime detection + install UX |

### Resources/Scripts/ (novos, Embedded Resources)

| Arquivo | Tamanho | Descrição |
|---|---|---|
| element-picker.js | ~40KB | Picker completo (IIFE) |

### Views/ e Controls/ (novos)

| Arquivo | Descrição |
|---|---|
| ElementContextPanel.xaml | Preview do elemento capturado |
| AgentTargetPopup.xaml | Popup de seleção de agent + intent |

### Modificações em Existentes

| Arquivo | Mudança |
|---|---|
| App.xaml.cs | Registrar ~12 novos serviços no DI |
| IAiContextService.cs | Adicionar BuildElementPromptAsync, LastCapturedElement |
| AiContextService.cs | Implementar novos métodos |
| AiPromptIntent enum | Adicionar 4 novos valores |
| BrowserViewModel.cs | Adicionar picker commands, element context state |
| BrowserView.xaml | Adicionar botão picker, airspace fix |
| AssistantPanelViewModel.cs | Register messenger for ElementContextMessage |
| MainViewModel.cs / MainWindow.xaml | Integrar browser tab |

**Total: ~20 arquivos novos + ~8 arquivos modificados**

---

## 13. Roadmap de Implementação

### Fase 1 — Estabilidade do Browser (P0) — 1 semana

**Objetivo**: Browser existente funciona de forma robusta.

1. WebView2 Runtime detection com fallback UI
2. ProcessFailed handler (crash recovery para renderer, GPU, browser)
3. Security hardening: WebResourceRequested, NewWindowRequested, DownloadStarting
4. Airspace fix: overlays usam `Visibility.Collapsed` no WebView2
5. Keyboard focus: AcceleratorKeyPressed intercepta atalhos do app
6. AreBrowserAcceleratorKeysEnabled = false
7. DPI manifest (PerMonitorV2)

**Entregável**: Browser não crasha em Win10, recupera de crashes, atalhos do app funcionam.

### Fase 2 — Element Picker (P1) — 1.5 semanas

**Objetivo**: Selecionar elementos visuais com overlay e capturar dados do DOM.

1. Criar `element-picker.js` como Embedded Resource
   - Overlay com box model (margin/padding/border/content)
   - Tooltip com tag/id/class/dimensions
   - Capture-phase event listeners
   - requestAnimationFrame throttle
   - Keyboard navigation (↑↓←→, Escape, Enter)
   - Framework detection (React, Vue, Angular, Svelte)
   - 8 estratégias de selector generation
   - Shadow DOM support (open)
   - SVG/Canvas detection
2. Criar IDomSelectionService + implementação
   - InjectPickerScript via AddScriptToExecuteOnDocumentCreatedAsync
   - ActivatePicker() / DeactivatePicker()
   - Handle WebMessageReceived → parse PickerMessage protocol
   - Rate limiting (10msg/5s)
3. Criar ElementCaptureData model
4. Criar ElementSanitizer (password redaction, token detection)
5. Adicionar botão "🔍" e atalho Ctrl+Shift+C no BrowserView

**Entregável**: Picker funciona, hover destaca, click captura dados sanitizados.

### Fase 3 — Integração com IA (P1) — 1 semana

**Objetivo**: Enviar contexto do elemento para assistant AI ou terminal com agent.

1. Criar IElementContextBuilder + implementação
2. Criar ElementContextFormatter (terminal text / assistant markdown)
3. Criar IAiContextRouter + implementação
4. Estender IAiContextService com BuildElementPromptAsync
5. Adicionar 4 novos AiPromptIntent values
6. Criar AgentTargetPopup (escolha de agent + intent)
7. Integrar com AssistantPanelViewModel via WeakReferenceMessenger
8. Implementar TerminalContextInjector (chunked write, sanitização)
9. Toast de confirmação via INotificationService

**Entregável**: Selecionar elemento → escolher agent → contexto enviado end-to-end.

### Fase 4 — Contexto Avançado (P2) — 1.5 semanas

**Objetivo**: Screenshots, console errors, health check, code mapping.

1. Criar ICdpService + implementação (abstração do DevTools Protocol)
2. Element screenshots via CDP Page.captureScreenshot com clip
3. Console errors via CDP Runtime.consoleAPICalled
4. PortHealthCheckService (TCP check 3s, auto-reconnect com backoff)
5. ICodeMappingService (React fiber → data-testid → heurísticas)
6. ElementContextPanel.xaml (preview do elemento capturado)
7. ISelectionHistoryService + SQLite table + UI

**Entregável**: Contexto rico com screenshots, code mapping, histórico persistido.

### Fase 5 — Refinamento (P3) — 1.5 semanas

**Objetivo**: Polish, edge cases, multi-projeto.

1. Multi-tab (dispose+recreate com state cache)
2. UserDataFolder per-project com cleanup
3. Reuso de contexto anterior (re-enviar do histórico)
4. Shadow DOM traversal avançado
5. iframe same-origin support
6. Animações suaves (Catppuccin Mocha colors)
7. Split view terminal + browser (futuro)
8. Memory profiling e otimização

**Entregável**: Feature polida, segura, performática.

---

## 14. MVP Recomendado

### MVP = Fases 1 + 2 + 3 (3-3.5 semanas)

**MUST HAVE (MVP):**
- [x] WebView2 robusto com crash recovery e security
- [x] Element picker com overlay visual
- [x] Click para capturar: tag, id, classes, attributes, selector, HTML, ancestry
- [x] Framework detection (React, Vue)
- [x] Password redaction + token sanitization
- [x] Enviar para Assistant AI como mensagem no chat
- [x] Enviar para terminal com agent como texto formatado
- [x] Menu de seleção de agent + intent
- [x] Atalhos: Ctrl+B, Ctrl+Shift+C, Escape, F5
- [x] Status bar indicators

**NICE TO HAVE (Fases 4-5):**
- [ ] Screenshots (CDP)
- [ ] Console errors / network requests
- [ ] Code mapping (arquivo fonte)
- [ ] Histórico de seleções (SQLite)
- [ ] Multi-tab / multi-projeto
- [ ] Arrow keys DOM navigation
- [ ] Split view terminal + browser

### Estimativa Total

| Fase | Estimativa | Complexidade |
|---|---|---|
| Fase 1 — Estabilidade | ~16h | Média |
| Fase 2 — Picker | ~20h | Alta (JS cross-boundary) |
| Fase 3 — AI Integration | ~12h | Média |
| Fase 4 — Avançado | ~16h | Média-Alta |
| Fase 5 — Refinamento | ~14h | Média |
| **Total** | **~78h** | |
| **MVP (Fases 1-3)** | **~48h** | |

---

## 15. Riscos e Trade-offs

### Riscos Técnicos

| Risco | Probabilidade | Impacto | Mitigação |
|---|---|---|---|
| WebView2 Runtime não instalado (Win10) | Baixa | Alto | Detectar + oferecer Evergreen bootstrapper |
| Airspace issue com popups/tooltips WPF | Alta | Médio | Popups fora da área do WebView2 |
| Picker JS conflita com app JS | Baixa | Alto | Capture phase, IIFE, namespace isolado |
| React fiber inacessível em production | Alta | Médio | Funciona em dev mode, fallback heurísticas |
| WebView2 crash durante picker | Baixa | Alto | ProcessFailed handler + auto-restart |
| Payload muito grande para LLM | Média | Médio | Limites estritos (8KB terminal, 100KB total) |
| Focus keyboard roubado pelo WebView2 | Alta | Alto | AcceleratorKeyPressed + ESC handler |

### Trade-offs Principais

| Decisão | Escolhida | Rejeitada | Razão |
|---|---|---|---|
| Picker overlay | HTML/JS injetado | WPF overlay | Airspace problem |
| Multi-tab | Dispose+recreate | Pool de WebViews | -80MB RAM por tab |
| Comunicação JS↔C# | WebMessage (postMessage) | Host Objects (COM) | Mais simples, sem COM visible |
| Screenshots | CDP (futuro) | CapturePreviewAsync | CDP suporta clip region |
| Selector principal | data-testid first | ID first | IDs podem ser auto-generated |

---

## 16. Documentos de Referência Gerados

Os subagents produziram documentos técnicos detalhados que servem como referência durante a implementação:

| Documento | Localização | Conteúdo |
|---|---|---|
| BROWSER_FEATURE_PLAN.md | `/BROWSER_FEATURE_PLAN.md` | Plano original (1300 linhas) |
| BROWSER_TECHNICAL_ANALYSIS.md | `~/CommandDeck-Analysis/` | Gap analysis + WebView2 lifecycle + WPF risks (1065 linhas) |
| TECHNICAL_GUIDE.md | `~/webview2-element-picker/docs/` | DOM Inspection + JS architecture + Edge cases (815 linhas) |
| element-picker.js | `~/webview2-element-picker/js/` | JS completo do picker (~40KB, pronto para uso) |
| chunk-reassembler.cs | `~/webview2-element-picker/js/` | C# integration code para WebMessage chunking |
| BROWSER_AI_CONTEXT_RECOMMENDATIONS.md | `/BROWSER_AI_CONTEXT_RECOMMENDATIONS.md` | AI integration + Security + UX + Code mapping (2275 linhas) |

---

## 17. Recomendação Final

### Começar pela Fase 1 (Estabilidade)

O browser existente funciona mas tem gaps de segurança e robustez que impedem uso real. Resolver P0 primeiro garante uma base sólida.

### Fase 2 (Picker) é o core da feature

A maior complexidade está na bridge JS↔C# e no overlay que funciona sobre qualquer app. O `element-picker.js` já foi gerado pelo DOM Inspection Engineer e pode ser usado como ponto de partida.

### Fase 3 (AI Integration) fecha o loop

É onde a feature gera valor real: selecionar → enviar → IA responde. A arquitetura de routing (AiContextRouter) se integra naturalmente com os serviços AI existentes.

### O que torna esta feature transformadora

1. **Fecha o loop visual→código**: ver problema → selecionar → AI corrige
2. **Zero context switching**: tudo dentro do CommandDeck
3. **AI contextualizada**: agent recebe exatamente o que o usuário vê
4. **Multi-agent**: funciona com assistant local (Ollama), Claude Code, Codex
5. **Evolução natural**: o CommandDeck já tem 34+ serviços, 9 de AI, terminal ConPTY — o browser + picker é a peça que falta para conectar frontend visual com backend de desenvolvimento
