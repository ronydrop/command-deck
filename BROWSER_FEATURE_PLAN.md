# DevWorkspaceHub — Browser Embutido + Element Picker + AI Context

## Plano de Implementação Completo

**Versão:** 1.0
**Data:** 31/03/2026
**Autor:** Staff Engineer Analysis (Multi-Specialist)

---

## 1. Entendimento da Funcionalidade

### O que é

Um navegador web embutido dentro do DevWorkspaceHub que carrega aplicações locais em execução (localhost), permitindo ao usuário **selecionar visualmente elementos HTML** e enviar o contexto completo desses elementos para um **agent de IA específico** (assistant panel ou terminal com AI CLI).

### Fluxo ideal de uso

```
1. Usuário tem um projeto aberto com servidor local rodando (ex: localhost:3000)
2. Abre o browser embutido → app carrega automaticamente na porta detectada
3. Navega pela aplicação normalmente
4. Clica no botão "Selecionar Elemento" (ou Ctrl+Shift+C)
5. Cursor muda para crosshair, elementos são destacados ao hover
6. Clica em um elemento → overlay azul fixa, tooltip mostra tag/id/classes
7. Painel lateral mostra contexto capturado (HTML, selector, screenshot)
8. Usuário escolhe destino: Assistant AI ou terminal específico com agent
9. Contexto é injetado como mensagem/prompt no agent escolhido
10. Agent responde com análise, sugestão ou implementação baseada no contexto
```

### Diferenciação clara dos componentes

| Componente | Responsabilidade |
|---|---|
| **Browser local embutido** | WebView2 carregando localhost. Navegação, reload, URL bar, DevTools toggle |
| **Seleção visual de elementos** | JavaScript injetado no WebView2. Overlay, hover highlight, click capture, DOM serialization |
| **Envio de contexto para agent** | C# recebe dados via WebMessage, monta payload estruturado, roteia para agent alvo |
| **Assistant AI** | Recebe contexto como mensagem no chat panel (AssistantPanelViewModel). Usa IAssistantService |
| **Terminal/Agent específico** | Contexto é colado/injetado no terminal como texto estruturado. Agent CLI (cc, claude) processa |

---

## 2. Casos de Uso Práticos

### Caso 1: Botão quebrado
> Seleciono um botão que não funciona → envio para o agent → "Este botão com classe `.btn-submit` no formulário de checkout não está disparando o submit. Analise o handler e corrija."
> Agent recebe: HTML do botão, event listeners, selector, componente React `CheckoutForm`, arquivo provável `src/components/CheckoutForm.tsx`.

### Caso 2: Formulário com problemas de UX
> Seleciono o `<form>` inteiro → envio para assistant → "Melhore a validação e UX deste formulário"
> Agent recebe: HTML completo do form, campos, labels, atributos de validação, classes CSS, estado dos inputs.

### Caso 3: Refactor de seção
> Seleciono uma seção `<section class="hero">` → envio para terminal com CC → "Refatore esta seção para usar Tailwind ao invés de CSS custom"
> Agent recebe: outerHTML, classes, computed styles, ancestry, arquivo CSS relacionado.

### Caso 4: Localizar componente no código
> Seleciono um card de produto → sistema detecta React fiber → mostra `ProductCard` em `src/components/ProductCard.tsx:24`
> Mesmo sem source maps, heurística por data-testid, nome de classe ou estrutura DOM.

### Caso 5: Explicação técnica
> Seleciono um elemento SVG complexo → envio para assistant → "Explique o que este SVG faz e como posso animá-lo"
> Agent recebe: SVG markup, viewBox, paths, atributos de animação existentes.

---

## 3. Arquitetura Geral da Solução

### Diagrama de alto nível

```
┌──────────────────────────────────────────────────────────────────┐
│                    DevWorkspaceHub (WPF)                         │
│                                                                  │
│  ┌─────────────┐  ┌──────────────────┐  ┌───────────────────┐  │
│  │  Terminal    │  │  Browser Panel   │  │  Assistant Panel  │  │
│  │  Canvas     │  │  ┌────────────┐  │  │                   │  │
│  │             │  │  │  WebView2  │  │  │  Chat + Context   │  │
│  │  ConPTY     │  │  │  localhost  │  │  │  Display          │  │
│  │  Sessions   │  │  │            │  │  │                   │  │
│  │             │  │  │  Injected  │  │  │                   │  │
│  │             │  │  │  JS Picker │  │  │                   │  │
│  │             │  │  └────────────┘  │  │                   │  │
│  │             │  │  [🔍 Select]     │  │                   │  │
│  └──────┬──────┘  └────────┬─────────┘  └─────────┬─────────┘  │
│         │                  │                       │             │
│         │    ┌─────────────┴───────────────┐       │             │
│         │    │  Element Context Builder    │       │             │
│         │    │  (DOM → Structured Payload) │       │             │
│         │    └─────────────┬───────────────┘       │             │
│         │                  │                       │             │
│         │    ┌─────────────┴───────────────┐       │             │
│         ├────┤    AI Context Router        ├───────┤             │
│         │    │  (Choose: terminal or AI)   │       │             │
│         │    └─────────────────────────────┘       │             │
│         │                                          │             │
│  ┌──────┴──────────────────────────────────────────┴──────────┐ │
│  │              Existing AI Services Layer                     │ │
│  │  IAiContextService │ IAssistantService │ IAiTerminalService │ │
│  └─────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
```

### Fluxo de dados

```
WebView2 JS (element-picker.js)
    │ window.chrome.webview.postMessage(JSON)
    ▼
WebView2 CoreWebView2.WebMessageReceived (C#)
    │ deserialize JSON → ElementCaptureData
    ▼
IElementContextBuilder.BuildContext(ElementCaptureData)
    │ enrich: screenshot, code mapping, console errors
    ▼
IAiContextRouter.RouteToAgent(ElementContext, AgentTarget)
    │
    ├──► AssistantPanelViewModel.ReceiveElementContext()
    │    → mostra no chat como mensagem de contexto
    │
    └──► TerminalSessionService.InjectContext(sessionId, formattedContext)
         → cola texto estruturado no terminal do agent
```

---

## 4. Browser Embutido / WebView

### Comparação de opções

| Critério | **WebView2** | CefSharp | WebBrowser (IE) |
|---|---|---|---|
| Engine | Edge/Chromium | Chromium (bundled) | IE11/Trident ❌ |
| .NET 8 | ✅ Suporte nativo | ✅ via NETCore pkg | Deprecated |
| Distribuição | Pequena (runtime já no Win11) | +300MB (bundle Chromium) | Zero |
| JS Interop | ExecuteScriptAsync + WebMessage | EvaluateScriptAsync + BindObject | InvokeScript (limitado) |
| CDP Access | ✅ CallDevToolsProtocolMethodAsync | ✅ GetDevToolsClient | ❌ |
| Screenshot | CapturePreviewAsync + CDP | ScreenshotAsync + CDP | ❌ |
| Segurança | Excelente (Microsoft, sandbox) | Boa (auto-gerenciar updates) | Péssima (EOL) |
| AnyCPU | ✅ | ❌ (x86 ou x64 apenas) | ✅ |
| DevTools | OpenDevToolsWindow() | ShowDevTools() | ❌ |
| Licença | BSD-like, gratuito | BSD 3-clause | MIT |

### Recomendação: **Microsoft WebView2**

**Razões decisivas:**

1. **Já incluído no Windows 11** — zero overhead de distribuição
2. **Microsoft-backed** — atualizações de segurança automáticas via Evergreen Runtime
3. **CDP completo** — acesso a DOM tree, screenshots, network, console via DevTools Protocol
4. **WebMessage API** — comunicação bidirecional perfeita para element picker
5. **Processo separado** — não bloqueia a UI thread do WPF
6. **AnyCPU** — sem restrições de plataforma
7. **Integração natural com MVVM** — WPF control pronto para usar

**NuGet necessário:**
```xml
<PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2903.40" />
```

**Limitações conhecidas:**
- Overlay domain do CDP NÃO disponível (highlight nativo) → resolvido com JS injetado
- Primeira inicialização leva ~500ms (criação do processo)
- ~100-200MB RAM base do Chromium

---

## 5. Element Picker / HTML Selector

### Arquitetura do picker

O sistema consiste em **JavaScript injetado** no WebView2 que:

1. **Ativa modo seleção** via `ExecuteScriptAsync("ElementPickerActivate()")`
2. **Intercepta mousemove** (capture phase) → destaca elemento sob cursor
3. **Intercepta click** (capture phase) → captura e serializa o elemento
4. **Envia dados** via `window.chrome.webview.postMessage()`
5. **Desativa** ao capturar ou ao pressionar Escape

### Overlay visual

```
┌─ Margin (laranja, rgba(246,178,107,0.3)) ─────────────────┐
│  ┌─ Border (azul, 2px solid #1a73e8) ─────────────────┐   │
│  │  ┌─ Padding (verde, rgba(147,196,125,0.4)) ──────┐ │   │
│  │  │  ┌─ Content ─────────────────────────────────┐ │ │   │
│  │  │  │                                           │ │ │   │
│  │  │  │          Elemento destacado               │ │ │   │
│  │  │  │                                           │ │ │   │
│  │  │  └───────────────────────────────────────────┘ │ │   │
│  │  └────────────────────────────────────────────────┘ │   │
│  └─────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────┘

[Tooltip: button#submit.btn.btn-primary | 120 × 40 | [data-testid="submit-btn"]]
```

### Dados capturados por elemento

```typescript
interface ElementCaptureData {
  // Identificação
  tagName: string;              // "button"
  id: string | null;            // "submit-btn"
  className: string;            // "btn btn-primary"

  // Seletores (múltiplas estratégias)
  cssSelector: string;          // "[data-testid='submit-btn']"
  xpath: string;                // "//button[@data-testid='submit-btn']"
  allSelectors: SelectorInfo[]; // todas as alternativas com score

  // Conteúdo
  textContent: string;          // "Enviar" (max 500 chars)
  innerText: string;            // "Enviar"
  innerHTML: string;            // "<span>Enviar</span>" (max 1000 chars)
  outerHTML: string;            // "<button class='btn'>..." (max 2000 chars)

  // Atributos
  attributes: Record<string, string>;

  // Box model
  boundingBox: { x, y, width, height };
  absolutePosition: { x, y, width, height };
  boxModel: { margin, padding, border };

  // Estilos computados (relevantes)
  computedStyles: Record<string, string>;

  // Contexto DOM
  ancestors: AncestorInfo[];    // até 10 níveis
  childrenSummary: {
    count: number;
    tags: ChildInfo[];          // até 20 filhos
  };

  // Acessibilidade
  accessibility: {
    role, ariaLabel, ariaExpanded, tabIndex, title, alt, placeholder, name, type, value
  };

  // Framework detection
  frameworkInfo: {
    framework: 'react' | 'vue' | 'angular' | 'svelte' | null;
    componentName: string | null;
    componentStack: string[];   // hierarquia de componentes
    testIds: Record<string, string>;
  };

  // Viewport
  viewport: { width, height, scrollX, scrollY };
  url: string;
  timestamp: number;
}
```

### Geração de seletores — Estratégias em ordem de prioridade

| Prioridade | Estratégia | Estabilidade | Exemplo |
|---|---|---|---|
| 1 | `data-testid` | Alta | `[data-testid="submit-btn"]` |
| 2 | ID único | Média | `#checkout-form` |
| 3 | aria-label + role | Média | `button[role="submit"][aria-label="Enviar"]` |
| 4 | Combinação de classes | Baixa | `button.btn-primary.btn-lg` |
| 5 | Path curto com ID ancestor | Média | `#form > div:nth-of-type(2) > button` |
| 6 | nth-child path completo | Frágil | `html > body > div:nth-child(1) > ...` |

### Navegação por teclado (como Chrome DevTools)

- **↑** = elemento pai
- **↓** = primeiro filho
- **←** = irmão anterior
- **→** = próximo irmão
- **Enter** = confirmar seleção
- **Escape** = cancelar

---

## 6. Contexto Enviado para IA

### Payload estruturado completo

```csharp
public class ElementContext
{
    // === ELEMENTO ===
    public string TagName { get; set; }
    public string? Id { get; set; }
    public string? ClassName { get; set; }
    public string CssSelector { get; set; }
    public string XPath { get; set; }
    public string? OuterHtml { get; set; }
    public string? InnerHtml { get; set; }
    public string? TextContent { get; set; }
    public Dictionary<string, string> Attributes { get; set; }
    public BoundingRect BoundingBox { get; set; }
    public Dictionary<string, string> ComputedStyles { get; set; }

    // === HIERARQUIA ===
    public List<AncestorInfo> Ancestors { get; set; }
    public ChildrenSummary Children { get; set; }
    public string? ParentHtmlSummary { get; set; }  // outerHTML resumido do pai

    // === FRAMEWORK ===
    public string? Framework { get; set; }
    public string? ComponentName { get; set; }
    public List<string>? ComponentStack { get; set; }

    // === PÁGINA ===
    public string Url { get; set; }
    public string? PageTitle { get; set; }
    public ViewportInfo Viewport { get; set; }

    // === SCREENSHOTS ===
    public byte[]? ElementScreenshot { get; set; }   // PNG do elemento
    public byte[]? ViewportScreenshot { get; set; }   // PNG da viewport
    public string? ElementScreenshotBase64 { get; set; }

    // === CONTEXTO EXTRA (opcional) ===
    public List<ConsoleError>? ConsoleErrors { get; set; }
    public List<NetworkRequest>? RecentRequests { get; set; }
    public string? AppState { get; set; }  // ex: Redux state slice

    // === CÓDIGO RELACIONADO ===
    public CodeMapping? RelatedCode { get; set; }

    // === METADATA ===
    public DateTime CapturedAt { get; set; }
    public string? ProjectPath { get; set; }
    public string? ProjectType { get; set; }
}

public class CodeMapping
{
    public string? FilePath { get; set; }        // "src/components/Button.tsx"
    public int? LineNumber { get; set; }
    public string? ComponentName { get; set; }
    public string? MappingMethod { get; set; }   // "react-fiber", "source-map", "heuristic"
    public float Confidence { get; set; }        // 0.0 a 1.0
}
```

### Formato de texto para agent (quando injetado no terminal)

```
═══ ELEMENTO SELECIONADO ═══
Tag: <button> #submit-btn .btn.btn-primary
Selector: [data-testid="submit-btn"]
XPath: //button[@data-testid="submit-btn"]
Texto: "Enviar pedido"

Atributos:
  type="submit"
  data-testid="submit-btn"
  disabled="false"
  aria-label="Enviar pedido"

Hierarquia: form#checkout > div.form-actions > button#submit-btn
Filhos: 1 (span)

HTML:
<button id="submit-btn" class="btn btn-primary" type="submit"
        data-testid="submit-btn" aria-label="Enviar pedido">
  <span>Enviar pedido</span>
</button>

Framework: React | Componente: CheckoutForm
Arquivo provável: src/components/CheckoutForm.tsx

URL: http://localhost:3000/checkout
Viewport: 1920x1080
═══════════════════════════
```

### Limites de payload

| Campo | Limite | Razão |
|---|---|---|
| outerHTML | 3000 chars | Evitar contexto gigante para LLM |
| innerHTML | 1500 chars | Foco no relevante |
| textContent | 500 chars | Texto visível suficiente |
| ancestors | 10 níveis | Hierarquia completa suficiente |
| children | 20 itens | Overview dos filhos |
| computedStyles | ~15 props | Apenas estilos relevantes |
| screenshot | 1280px max | Qualidade vs tamanho |
| **Total payload** | **~100KB max** | Dentro do context window da maioria dos LLMs |

---

## 7. Integração com Agents e Terminais

### Escolha do agent alvo

Ao capturar um elemento, o usuário vê um mini-menu:

```
┌─────────────────────────────────────────┐
│  Enviar contexto para:                  │
│                                         │
│  🤖 Assistant AI                        │
│  ─────────────────────                  │
│  📟 Terminal 1 — Claude Code (Sonnet)   │
│  📟 Terminal 2 — CC Agent (Opus)        │
│  📟 Terminal 3 — bash (sem AI)     [x]  │
│                                         │
│  [Copiar para clipboard]               │
└─────────────────────────────────────────┘
```

**Regras:**
- Terminais sem agent AI aparecen desabilitados (com `[x]`)
- O último agent usado fica destacado como "padrão"
- Shortcut: `Enter` envia para o padrão, `1-9` seleciona direto

### Como cada agent "enxerga" o contexto

**Assistant AI (AssistantPanelViewModel):**
- Recebe como `ElementContextMessage` no chat
- Exibido como card collapsible com preview do elemento
- Screenshot em miniatura clicável
- HTML em bloco de código com syntax highlight
- Agent recebe o payload completo no prompt

**Terminal/Agent CLI (cc, claude):**
- Contexto formatado como texto é colado via `TerminalService.WriteAsync()`
- Prefixado com instrução: "Analise o seguinte elemento HTML capturado da aplicação:"
- Agent CLI processa como prompt normal
- Screenshot salvo como arquivo temporário, path incluído no contexto

### Histórico de seleções

```csharp
public class SelectionHistoryEntry
{
    public string Id { get; set; }                // GUID
    public DateTime CapturedAt { get; set; }
    public string CssSelector { get; set; }
    public string TagSummary { get; set; }        // "button#submit.btn"
    public string Url { get; set; }
    public byte[]? Thumbnail { get; set; }        // 64x64 thumbnail
    public ElementContext FullContext { get; set; }
    public string? SentToAgent { get; set; }      // "assistant" ou sessionId
}
```

- Últimas 50 seleções mantidas em memória
- Painel lateral "Histórico" mostra timeline com thumbnails
- Click para re-enviar uma seleção anterior para outro agent
- Persistência opcional em SQLite (PersistenceService)

---

## 8. Descoberta do Código Relacionado

### Abordagens comparadas

| Abordagem | Viabilidade | Precisão | Complexidade |
|---|---|---|---|
| **React Fiber internals** | Alta (React apps) | Alta | Média |
| **Source maps** | Média | Alta | Alta |
| **data-testid / data-component** | Alta | Média | Baixa |
| **Heurística por nome de classe** | Alta | Baixa-Média | Baixa |
| **Injeção de atributos no build** | Alta | Alta | Média (config build) |
| **Framework DevTools protocol** | Média | Alta | Alta |

### Recomendação: Abordagem em camadas

**Camada 1 — React Fiber (automática, zero config):**
```javascript
// Já implementado no element-picker.js
const reactFiber = Object.keys(element).find(k =>
  k.startsWith('__reactFiber$'));
if (reactFiber) {
  const fiber = element[reactFiber];
  componentName = fiber.type?.displayName || fiber.type?.name;
  // Walk fiber tree para component stack
}
```
- Funciona sem configuração
- React expõe fibers no DOM em development mode
- Obtém: nome do componente, props, component stack

**Camada 2 — data-testid e atributos convencionais:**
```javascript
const testId = element.getAttribute('data-testid');
// Heurística: data-testid="product-card" → buscar "ProductCard" nos arquivos
```
- Busca por nome similar no projeto via `search_files`
- Pattern: `data-testid="checkout-form"` → `CheckoutForm.tsx`

**Camada 3 — Heurística por classes e estrutura:**
```csharp
// No C#, após receber o elemento:
var className = elementData.ClassName; // "product-card__title"
// Buscar no projeto por: ProductCard, product-card, ProductCardTitle
var candidates = await SearchProjectFiles(
    projectPath,
    new[] { "ProductCard", "product-card", "product_card" },
    new[] { "*.tsx", "*.jsx", "*.vue", "*.svelte" }
);
```

**Camada 4 (futuro) — babel/webpack plugin para injeção:**
```javascript
// babel plugin que adiciona data-source="ComponentName:file:line"
// Só em development, removido em production
```

### Fluxo de code mapping

```
Element capturado
    │
    ├── React fiber disponível? ──► componentName + stack → buscar arquivo
    │
    ├── data-testid presente? ──► converter para PascalCase → buscar arquivo
    │
    ├── Classes CSS significativas? ──► pattern matching → buscar arquivo
    │
    └── Nenhum match? ──► retornar null (sem code mapping)
    
    Para cada match:
    └── Buscar no projeto via IGitService/ProjectService
        ├── Regex em arquivos do tipo do framework
        ├── Ranking por similaridade de nome
        └── Retornar top 3 candidatos com confidence score
```

---

## 9. UX / Produto

### Layout geral

```
┌──────────────────────────────────────────────────────────────────────────┐
│  [Dashboard] [Processes] [⚙ Settings]              [🤖 AI] [🌐 Browser] │
├────────┬────────────────────────────────────────────────┬────────────────┤
│        │                                                │                │
│ Sidebar│           Área Central                         │  Side Panel    │
│        │                                                │                │
│ ┌────┐ │  ┌─ Tabs ──────────────────────────────────┐   │  ┌──────────┐ │
│ │Proj│ │  │ [Terminal Canvas] [🌐 Browser] [+ New]   │   │  │Assistant │ │
│ │List│ │  └──────────────────────────────────────────┘   │  │  Panel   │ │
│ │    │ │                                                │  │          │ │
│ │    │ │  ┌──────────────────────────────────────────┐   │  │ [Chat]   │ │
│ │    │ │  │  🌐 Browser View                         │   │  │          │ │
│ │    │ │  │  [← →] [🔄] [http://localhost:3000___]   │   │  │ Element  │ │
│ │    │ │  │  [🔍 Select Element] [📸 Screenshot]     │   │  │ Context  │ │
│ │    │ │  │                                          │   │  │ Preview  │ │
│ │    │ │  │  ┌──────────────────────────────────┐    │   │  │          │ │
│ │    │ │  │  │                                  │    │   │  │          │ │
│ │    │ │  │  │       WebView2 Content           │    │   │  │          │ │
│ │    │ │  │  │       (localhost app)             │    │   │  │          │ │
│ │    │ │  │  │                                  │    │   │  │          │ │
│ │    │ │  │  └──────────────────────────────────┘    │   │  │          │ │
│ │    │ │  └──────────────────────────────────────────┘   │  │          │ │
│ └────┘ │                                                │  └──────────┘ │
├────────┴────────────────────────────────────────────────┴────────────────┤
│  Status: ● localhost:3000 connected | 🔍 Element picker active          │
└──────────────────────────────────────────────────────────────────────────┘
```

### Navegação entre modos

- **Tab "Terminal Canvas"**: layout atual com terminais draggáveis
- **Tab "Browser"**: navegador embutido (novo)
- **Toggle rápido**: `Ctrl+B` abre/fecha browser, `Ctrl+I` abre/fecha assistant
- **Split view** (futuro): terminal + browser lado a lado

### Indicadores visuais

| Estado | Indicador |
|---|---|
| Browser carregando | Spinner na URL bar |
| Picker ativo | Cursor crosshair, badge "🔍" na tab, status bar "Element picker active" |
| Elemento selecionado | Overlay azul persiste, toast "Elemento capturado ✓" |
| Contexto enviado | Toast "Contexto enviado para [agent name]", highlight no assistant panel |
| Servidor offline | Banner vermelho "localhost:3000 não responde" com botão retry |

### Atalhos de teclado

| Atalho | Ação |
|---|---|
| `Ctrl+B` | Toggle browser panel |
| `Ctrl+Shift+C` | Toggle element picker (como Chrome DevTools) |
| `Escape` | Cancelar seleção |
| `Enter` (com elemento) | Enviar para agent padrão |
| `Ctrl+Shift+S` | Screenshot da viewport |
| `F5` | Reload página no browser |
| `Ctrl+L` | Focar URL bar |

---

## 10. Segurança

### Análise de riscos e mitigações

| Risco | Severidade | Mitigação |
|---|---|---|
| **Injeção de script malicioso** | Alta | JS do picker é read-only, não modifica DOM do app. Executa em capture phase isolado |
| **Acesso a filesystem via WebView2** | Alta | `NavigationStarting` bloqueia qualquer URL fora de localhost/127.0.0.1 |
| **Exposição de tokens/secrets** | Alta | Não capturar `<input type="password">` values. Filtrar headers com Authorization |
| **Cross-origin iframes** | Média | Picker não atravessa iframes cross-origin (limitação natural do browser) |
| **Projeto local malicioso** | Média | WebView2 roda em processo separado (sandbox Chromium). Não tem acesso ao filesystem do WPF |
| **XSS via outerHTML capturado** | Baixa | HTML é tratado como texto plano no C#, nunca renderizado como HTML no WPF |
| **Memory leak do WebView2** | Média | Dispose explícito ao trocar de projeto. Limite de 1 instância ativa |
| **Acesso do agent ao DOM** | Média | Agent recebe snapshot estático, não tem acesso live ao browser |

### Configurações de segurança do WebView2

```csharp
webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;  // Sem alert/confirm
webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
webView.CoreWebView2.Settings.AreDevToolsEnabled = true;  // Dev only
webView.CoreWebView2.Settings.IsWebMessageEnabled = true;  // Necessário
webView.CoreWebView2.Settings.IsScriptEnabled = true;      // Necessário
webView.CoreWebView2.Settings.AreHostObjectsAllowed = false; // Não expor objetos C#

// Bloquear navegação fora de localhost
webView.CoreWebView2.NavigationStarting += (s, e) => {
    var uri = new Uri(e.Uri);
    if (uri.Host != "localhost" && uri.Host != "127.0.0.1" && uri.Scheme != "data")
        e.Cancel = true;
};

// User data folder isolado
var env = await CoreWebView2Environment.CreateAsync(
    userDataFolder: Path.Combine(appDataPath, "BrowserData"));
```

### Sanitização de dados capturados

```csharp
// Antes de enviar para AI:
// 1. Remover values de inputs type=password
// 2. Truncar atributos muito longos
// 3. Remover inline scripts
// 4. Limitar tamanho total do payload
```

---

## 11. Performance

### Custos e mitigações

| Operação | Custo | Mitigação |
|---|---|---|
| **WebView2 aberto** | ~100-200MB RAM | Lazy init, criar só quando tab "Browser" é aberta |
| **Element picker hover** | ~1ms por mousemove | Throttle a 60fps (requestAnimationFrame) |
| **DOM serialization** | ~5-50ms | Async, limites de profundidade e tamanho |
| **Screenshot viewport** | ~50-200ms | CDP Page.captureScreenshot é async e não bloqueia |
| **Screenshot elemento** | ~20-100ms | Clip region no CDP |
| **Code mapping search** | ~100-500ms | Cache de file index, busca em background |
| **Payload para AI** | ~10-100KB | Limites estritos, truncamento inteligente |

### Estratégias de otimização

1. **Lazy initialization**: WebView2 só é criado quando o tab "Browser" é aberto pela primeira vez
2. **Throttle no mousemove**: `requestAnimationFrame` para limitar updates do overlay
3. **DOM serialization bounds**: Max 3 níveis de profundidade, max 20 children, max 3KB outerHTML
4. **Screenshot caching**: Cache por URL + scroll position, invalidar em navegação
5. **Dispose agressivo**: Ao fechar tab browser ou trocar projeto, dispose WebView2
6. **Background thread**: Code mapping e enrichment rodam em Task.Run
7. **Debounce de re-render**: Overlay positions usam `transition: 0.05s ease-out` para suavizar

### Limites hard-coded

```csharp
public static class BrowserLimits
{
    public const int MaxOuterHtmlChars = 3000;
    public const int MaxInnerHtmlChars = 1500;
    public const int MaxTextContentChars = 500;
    public const int MaxAncestorDepth = 10;
    public const int MaxChildrenCount = 20;
    public const int MaxPayloadBytes = 100 * 1024;  // 100KB
    public const int MaxScreenshotWidth = 1280;
    public const int MaxSelectionHistory = 50;
    public const int PickerThrottleMs = 16;  // ~60fps
}
```

---

## 12. Estados e Edge Cases

### Tabela de edge cases

| Cenário | Comportamento | Implementação |
|---|---|---|
| **Projeto não rodando** | Banner "Servidor não encontrado" + botão "Iniciar projeto" | Check TCP port antes de navegar |
| **localhost indisponível** | Retry automático 3x com backoff, depois mostra erro | Timer de 2s/4s/8s |
| **Múltiplas rotas** | URL bar editável, back/forward funcionam | CoreWebView2.GoBack/GoForward |
| **iframe cross-origin** | Picker não entra no iframe, mostra o `<iframe>` como elemento | Limitação natural, documentar |
| **iframe same-origin** | Picker funciona dentro via contentDocument | Verificar same-origin antes |
| **Elementos dinâmicos** | Selector pode ficar stale → avisar | Tag no histórico: "pode estar desatualizado" |
| **Shadow DOM** | `element.shadowRoot.elementFromPoint()` como fallback | Implementado no picker |
| **Canvas** | Capturar como `<canvas>`, sem DOM interno | Informar: "Elemento canvas, sem DOM acessível" |
| **SVG** | Funciona normalmente, SVG elements têm attributes | getBBox() ao invés de getBoundingClientRect |
| **Conteúdo virtualizado** | Só elementos renderizados são selecionáveis | OK — é o comportamento esperado |
| **Página muito grande** | Throttle no mousemove, limite de serialization | Bounds já implementados |
| **Múltiplos projetos** | 1 WebView2 por vez, trocar projeto = trocar URL | Dispose + recreate ao trocar |
| **Seleção cancelada** | Escape → deactivate picker, limpar overlay | Event handler |
| **Agent offline** | Toast "Agent não disponível" + opção clipboard | Check agent state antes de enviar |
| **Terminal sem agent** | Opção desabilitada no menu de envio | IAiAgentStateService check |
| **Reload durante picker** | Picker desativa automaticamente | NavigationStarting → deactivate |
| **DevTools aberto** | Funciona normalmente, não interfere | WebView2 suporta ambos |

---

## 13. Arquitetura Técnica Detalhada

### Módulos e responsabilidades

```
Services/Browser/
├── IBrowserRuntimeService.cs          → Gerencia lifecycle do WebView2
├── BrowserRuntimeService.cs
├── ILocalAppSessionService.cs         → Detecta porta/URL do projeto local
├── LocalAppSessionService.cs
├── IDomSelectionService.cs            → Injeção de JS, ativação do picker
├── DomSelectionService.cs
├── IElementContextBuilder.cs          → Monta ElementContext a partir do raw data
├── ElementContextBuilder.cs
├── IAiContextRouter.cs                → Roteia contexto para agent alvo
├── AiContextRouter.cs
├── IElementScreenshotService.cs       → Screenshots via CDP
├── ElementScreenshotService.cs
├── ICodeMappingService.cs             → Relaciona elemento → código fonte
├── CodeMappingService.cs
├── ISelectionHistoryService.cs        → Histórico de seleções
├── SelectionHistoryService.cs
└── Scripts/
    ├── element-picker.js              → JS do picker (embedded resource)
    ├── selector-generator.js          → Geração de seletores
    └── dom-serializer.js              → Serialização do DOM

Models/Browser/
├── ElementCaptureData.cs              → Raw data do JS
├── ElementContext.cs                  → Contexto enriquecido para AI
├── BrowserSession.cs                  → Estado da sessão do browser
├── SelectionHistoryEntry.cs           → Entrada no histórico
├── CodeMapping.cs                     → Mapeamento elemento → arquivo
└── AgentTarget.cs                     → Destino do contexto (assistant/terminal)

ViewModels/
├── BrowserViewModel.cs                → VM principal do browser
├── BrowserToolbarViewModel.cs         → URL bar, botões, picker toggle
├── ElementContextViewModel.cs         → Preview do elemento selecionado
└── AgentTargetSelectorViewModel.cs    → Menu de seleção de agent

Views/
├── BrowserView.xaml                   → View com WebView2 + toolbar
├── ElementContextPanel.xaml           → Painel de preview do contexto
└── AgentTargetPopup.xaml              → Popup de seleção de agent

Controls/
├── BrowserToolbar.xaml                → URL bar + navigation buttons
└── ElementPreviewCard.xaml            → Card resumo do elemento
```

### Diagrama de comunicação entre módulos

```
                    ┌─────────────────────┐
                    │   BrowserViewModel   │
                    │  (orchestrator)      │
                    └──────────┬──────────┘
                               │
            ┌──────────────────┼──────────────────┐
            │                  │                   │
            ▼                  ▼                   ▼
  ┌─────────────────┐ ┌──────────────────┐ ┌──────────────┐
  │ BrowserRuntime  │ │ DomSelection     │ │ AiContext     │
  │ Service         │ │ Service          │ │ Router        │
  │                 │ │                  │ │               │
  │ - Init WebView2 │ │ - Inject JS     │ │ - Route to    │
  │ - Navigate      │ │ - Handle msgs   │ │   assistant   │
  │ - Lifecycle     │ │ - Parse data    │ │ - Route to    │
  └────────┬────────┘ └────────┬─────────┘ │   terminal   │
           │                   │           └───────┬──────┘
           │                   ▼                   │
           │         ┌──────────────────┐          │
           │         │ ElementContext   │          │
           │         │ Builder         │          │
           │         │                  │          │
           │         │ - Enrich data   │          │
           │         │ - Screenshots   │──────────┘
           │         │ - Code mapping  │
           │         └────────┬─────────┘
           │                  │
           │    ┌─────────────┼─────────────┐
           │    │             │              │
           │    ▼             ▼              ▼
           │ ┌──────────┐ ┌──────────┐ ┌──────────┐
           │ │Screenshot│ │ Code     │ │Selection │
           │ │ Service  │ │ Mapping  │ │ History  │
           │ │ (CDP)    │ │ Service  │ │ Service  │
           │ └──────────┘ └──────────┘ └──────────┘
           │
           ▼
  ┌─────────────────┐
  │ LocalAppSession │
  │ Service         │
  │                 │
  │ - Detect port   │
  │ - Health check  │
  │ - Auto-connect  │
  └─────────────────┘
```

### Registro no DI (App.xaml.cs)

```csharp
// Browser Services
services.AddSingleton<IBrowserRuntimeService, BrowserRuntimeService>();
services.AddSingleton<ILocalAppSessionService, LocalAppSessionService>();
services.AddSingleton<IDomSelectionService, DomSelectionService>();
services.AddSingleton<IElementContextBuilder, ElementContextBuilder>();
services.AddSingleton<IAiContextRouter, AiContextRouter>();
services.AddSingleton<IElementScreenshotService, ElementScreenshotService>();
services.AddSingleton<ICodeMappingService, CodeMappingService>();
services.AddSingleton<ISelectionHistoryService, SelectionHistoryService>();

// Browser ViewModels
services.AddSingleton<BrowserViewModel>();
```

---

## 14. Modelo de Dados

### BrowserSession

```csharp
public class BrowserSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; }
    public string Url { get; set; }                    // "http://localhost:3000"
    public int Port { get; set; }
    public BrowserSessionState State { get; set; }     // Disconnected, Connecting, Connected, Error
    public DateTime ConnectedAt { get; set; }
    public DateTime? LastNavigationAt { get; set; }
    public List<string> NavigationHistory { get; set; } = new();
    public bool IsPickerActive { get; set; }
}

public enum BrowserSessionState
{
    Disconnected,
    Connecting,
    Connected,
    Error,
    Loading
}
```

### SelectedElementContext (completo)

```csharp
public class SelectedElementContext
{
    // Core identification
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; }

    // Element data (from JS)
    public ElementCaptureData RawData { get; set; }

    // Enriched context
    public ElementContext EnrichedContext { get; set; }

    // Agent routing
    public AgentTarget? Target { get; set; }
    public bool WasSent { get; set; }
    public DateTime? SentAt { get; set; }

    // Screenshots
    public string? ElementScreenshotPath { get; set; }
    public string? ViewportScreenshotPath { get; set; }

    // Code mapping
    public CodeMapping? RelatedCode { get; set; }

    // Metadata
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}
```

### AgentTarget

```csharp
public class AgentTarget
{
    public AgentTargetType Type { get; set; }   // Assistant, Terminal
    public string? TerminalSessionId { get; set; }
    public string DisplayName { get; set; }     // "Claude Code (Sonnet)"
    public string? AgentType { get; set; }      // "cc", "claude", "ollama"
}

public enum AgentTargetType
{
    Assistant,
    Terminal
}
```

### SelectionHistory

```csharp
public class SelectionHistoryEntry
{
    public string Id { get; set; }
    public string SessionId { get; set; }
    public DateTime CapturedAt { get; set; }
    public string TagSummary { get; set; }       // "button#submit.btn-primary"
    public string CssSelector { get; set; }
    public string Url { get; set; }
    public string? ElementScreenshotPath { get; set; }
    public AgentTarget? SentTo { get; set; }
    public SelectedElementContext FullContext { get; set; }
}
```

### PageInspectionState

```csharp
public class PageInspectionState
{
    public string Url { get; set; }
    public string Title { get; set; }
    public bool IsPickerActive { get; set; }
    public SelectedElementContext? CurrentSelection { get; set; }
    public List<ConsoleLogEntry> ConsoleLog { get; set; } = new();
    public List<NetworkRequestEntry> NetworkLog { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}
```

### LinkedProjectRuntime

```csharp
public class LinkedProjectRuntime
{
    public string ProjectId { get; set; }
    public string ProjectPath { get; set; }
    public ProjectType ProjectType { get; set; }
    public int Port { get; set; }
    public string BaseUrl { get; set; }
    public bool IsRunning { get; set; }
    public string? ProcessName { get; set; }    // "node", "php", "dotnet"
    public int? ProcessId { get; set; }
}
```

---

## 15. Fases de Implementação

### Fase 1 — Browser Local Embutido (1-2 semanas)

**Objetivo:** Abrir e navegar em apps locais dentro do DevWorkspaceHub.

**Tarefas:**
1. Adicionar `Microsoft.Web.WebView2` ao .csproj
2. Criar `IBrowserRuntimeService` + implementação
   - Init/Dispose WebView2
   - Configuração de segurança (localhost only)
   - Navegação, back, forward, reload
3. Criar `ILocalAppSessionService`
   - Detectar porta do projeto (parse de package.json scripts, launchSettings.json, .env)
   - TCP health check na porta
   - Auto-detect via ProcessMonitorService (node em porta X)
4. Criar `BrowserViewModel`
   - URL, IsLoading, CanGoBack, CanGoForward
   - Navigate, GoBack, GoForward, Reload commands
5. Criar `BrowserView.xaml`
   - WebView2 control
   - Toolbar com URL bar + botões de navegação
6. Adicionar tab "Browser" no MainWindow
   - ViewType.Browser no enum
   - Toggle com Ctrl+B
7. Registrar tudo no DI (App.xaml.cs)
8. Status bar: indicador de conexão com localhost

**Entregável:** Pode abrir localhost:XXXX dentro do app e navegar.

---

### Fase 2 — Element Picker (1-2 semanas)

**Objetivo:** Selecionar elementos visuais com hover highlight e click capture.

**Tarefas:**
1. Criar `element-picker.js` como Embedded Resource
   - Overlay com margin/padding/content boxes
   - Tooltip com tag/id/class/dimensions
   - Capture-phase event listeners
   - Hover highlight com requestAnimationFrame
   - Click capture com preventDefault
   - Escape para cancelar
   - Arrow keys para navegar DOM tree
2. Criar `selector-generator.js` (Embedded Resource)
   - 6 estratégias de geração de selector
   - XPath generation
3. Criar `IDomSelectionService`
   - InjectPickerScript()
   - ActivatePicker() / DeactivatePicker()
   - Handle WebMessageReceived events
   - Parse ElementCaptureData do JSON
4. Criar `ElementCaptureData` model
5. Adicionar botão "🔍 Select Element" na toolbar do browser
6. Adicionar atalho Ctrl+Shift+C
7. Status bar: "Element picker active" / "Element picker off"

**Entregável:** Pode ativar picker, hover destaca elementos, click captura dados.

---

### Fase 3 — Envio para IA (1-2 semanas)

**Objetivo:** Enviar contexto do elemento para assistant AI ou terminal com agent.

**Tarefas:**
1. Criar `IElementContextBuilder`
   - Converter ElementCaptureData → ElementContext
   - Formatar como texto para terminal
   - Formatar como mensagem para assistant
2. Criar `IAiContextRouter`
   - Listar agents disponíveis (assistant + terminais com AI)
   - Enviar para AssistantPanelViewModel
   - Enviar para terminal via TerminalService.WriteAsync
3. Criar `AgentTargetSelectorViewModel` + popup
   - Lista de agents disponíveis
   - Shortcut: Enter para padrão, 1-9 para seleção direta
4. Integrar com `AssistantPanelViewModel`
   - Novo tipo de mensagem: ElementContextMessage
   - Exibir como card collapsible no chat
5. Integrar com `TerminalSessionService`
   - Injetar contexto formatado no terminal
6. Toast de confirmação: "Contexto enviado para [agent]"

**Entregável:** Selecionar elemento e enviar contexto para AI funciona end-to-end.

---

### Fase 4 — Contexto Avançado (1-2 semanas)

**Objetivo:** Screenshots, ancestry detalhada, metadata extra.

**Tarefas:**
1. Criar `IElementScreenshotService`
   - Screenshot da viewport via CDP
   - Screenshot do elemento via CDP clip region
   - Salvar como PNG temporário
   - Thumbnail 64x64 para histórico
2. Implementar captura de console errors
   - CoreWebView2.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled")
   - Últimos 20 console.error/warn
3. Implementar captura de network requests (opcional)
   - CDP Network.requestWillBeSent / Network.responseReceived
   - Últimos 10 requests com status
4. Enriquecer ElementContext com screenshots + console + network
5. Criar `ElementContextPanel.xaml`
   - Preview visual do elemento capturado
   - HTML com syntax highlight
   - Atributos em lista
   - Screenshot clicável
   - Botão "Enviar para..."

**Entregável:** Contexto rico com screenshots e metadata extra.

---

### Fase 5 — Code Mapping (1-2 semanas)

**Objetivo:** Relacionar elemento visual com código fonte do projeto.

**Tarefas:**
1. Implementar detecção de framework no JS (React fiber, Vue instance, Angular)
2. Criar `ICodeMappingService`
   - React: extrair componentName do fiber, buscar arquivo
   - data-testid: converter para PascalCase, buscar arquivo
   - Classes CSS: pattern matching em arquivos do framework
   - Retornar top 3 candidatos com confidence score
3. Integrar com `ElementContextBuilder`
4. Mostrar "Arquivo provável: src/components/X.tsx" no contexto
5. Ação "Abrir arquivo" que abre no terminal/editor

**Entregável:** Ao selecionar componente React/Vue, mostra arquivo fonte provável.

---

### Fase 6 — Refinamento (2-3 semanas)

**Objetivo:** Histórico, polish de UX, hardening.

**Tarefas:**
1. **Histórico de seleções**
   - `ISelectionHistoryService` com últimas 50 entries
   - UI com timeline + thumbnails
   - Re-enviar seleção anterior para outro agent
   - Persistir em SQLite
2. **UX polish**
   - Animações suaves no overlay
   - Transition entre picker ativo/inativo
   - Dark mode para toolbar do browser
   - Ícones consistentes com tema Catppuccin
3. **Segurança hardening**
   - Sanitização completa de HTML capturado
   - Filtro de password fields
   - Rate limit no picker (anti-spam de mensagens)
4. **Performance**
   - Lazy init do WebView2
   - Dispose ao trocar projeto
   - Memory profiling
   - Throttle de eventos
5. **Edge cases**
   - Shadow DOM traversal
   - SVG elements (getBBox)
   - Canvas detection
   - iframe same-origin support
   - Reload durante picker
6. **Testes**
   - Unit tests para ElementContextBuilder
   - Unit tests para CodeMappingService
   - Integration tests para DomSelectionService

**Entregável:** Feature completa, polida, segura e performática.

---

## 16. Riscos e Trade-offs

### Riscos técnicos

| Risco | Probabilidade | Impacto | Mitigação |
|---|---|---|---|
| WebView2 Runtime não instalado (Win10) | Baixa | Alto | Detectar na inicialização, oferecer download |
| Performance com apps pesados (Next.js dev) | Média | Médio | WebView2 é Chromium, performance equivalente a Edge |
| Selector gerado não é único | Média | Baixo | Fallback para nth-child path (sempre único) |
| React fiber inacessível em production | Alta | Médio | Funciona em dev mode, fallback para heurísticas |
| DOM serialization muito grande | Média | Médio | Limites estritos já definidos |
| Conflito entre picker JS e app JS | Baixa | Alto | Capture phase, namespace isolado, MutationObserver |
| WebView2 crash/freeze | Baixa | Alto | CoreWebView2.ProcessFailed event, auto-restart |

### Trade-offs principais

| Decisão | Opção A (escolhida) | Opção B (rejeitada) | Razão |
|---|---|---|---|
| Browser engine | WebView2 (runtime) | CefSharp (bundled) | Menor footprint, auto-update |
| Overlay | JS injetado | CDP Overlay domain | CDP Overlay indisponível no WebView2 |
| Comunicação JS↔C# | WebMessage (postMessage) | Host Objects (COM) | Mais simples, sem COM visible |
| Screenshots | CDP Page.captureScreenshot | html2canvas | CDP é mais rápido e preciso |
| 1 browser vs múltiplos | 1 por vez | Múltiplas tabs/WebViews | Simplicidade no MVP, RAM |
| Persistência do histórico | SQLite | Apenas memória | Consistência com PersistenceService existente |

---

## 17. MVP Recomendado

### MVP = Fases 1 + 2 + 3

**O que entra (MUST HAVE):**

- [x] WebView2 embutido carregando localhost
- [x] Toolbar com URL, back, forward, reload
- [x] Detecção automática de porta do projeto
- [x] Element picker com hover highlight
- [x] Click para capturar elemento
- [x] Serialização de: tag, id, classes, attributes, selector, HTML, texto, ancestry
- [x] Enviar para Assistant AI como mensagem de chat
- [x] Enviar para terminal com agent como texto formatado
- [x] Menu de seleção de agent alvo
- [x] Atalhos: Ctrl+B (browser), Ctrl+Shift+C (picker)
- [x] Escape para cancelar picker
- [x] Status bar com indicadores

**O que fica para depois (NICE TO HAVE):**

- [ ] Screenshots (Fase 4)
- [ ] Console errors / network requests (Fase 4)
- [ ] Code mapping / source detection (Fase 5)
- [ ] Histórico de seleções (Fase 6)
- [ ] Navegação por arrow keys no DOM (Fase 6)
- [ ] Shadow DOM traversal (Fase 6)
- [ ] Split view terminal + browser (futuro)
- [ ] Múltiplas tabs de browser (futuro)

### Estimativa MVP

| Fase | Estimativa | Complexidade |
|---|---|---|
| Fase 1 — Browser | 1-2 semanas | Média (WebView2 setup, lifecycle) |
| Fase 2 — Picker | 1-2 semanas | Alta (JS injection, overlay, event handling) |
| Fase 3 — AI integration | 1 semana | Média (routing, formatting, UI) |
| **Total MVP** | **3-5 semanas** | |

---

## 18. Recomendações Finais

### Síntese

DevWorkspaceHub já possui uma **infraestrutura madura** com 34 serviços, AI integration robusta (9 serviços de AI), terminal ConPTY completo, e sistema de canvas espacial. A adição de um browser embutido com element picker é uma **evolução natural** que conecta o mundo visual (frontend) com o mundo de desenvolvimento (terminais + AI agents).

### Abordagem técnica recomendada

**WebView2** é a escolha unânime. Sem trade-offs significativos comparado às alternativas. A comunicação via **WebMessage (postMessage)** é a mais simples e segura para o element picker. O **CDP** complementa para screenshots e DOM tree avançado.

### Princípios de implementação

1. **Incremental**: Cada fase entrega valor independente
2. **Consistente**: Seguir padrão MVVM existente (interfaces, DI, ObservableProperty, RelayCommand)
3. **Seguro por padrão**: Localhost-only, sem Host Objects COM, sanitização
4. **Performance-aware**: Lazy init, throttle, limites de payload
5. **Extensível**: Serviços separados permitem evolução independente

### O que torna essa feature transformadora

- **Fecha o loop**: ver o problema → selecionar → enviar para AI → receber solução → aplicar
- **Zero context switching**: não precisa sair do DevWorkspaceHub para inspecionar UI
- **AI contextualizada**: agent recebe exatamente o que o usuário está vendo, não precisa descrever
- **Multiplataforma de agents**: funciona com assistant local (Ollama) e terminal agents (CC, Claude)

### Prioridade #1

Comece pela **Fase 1** (WebView2 básico). É a fundação sobre a qual tudo se constrói. A complexidade maior está na Fase 2 (element picker JS), mas com o JS bem estruturado (já documentado neste plano), é implementável de forma modular.

---

## Apêndice A — NuGet Package a Adicionar

```xml
<PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2903.40" />
```

## Apêndice B — Enum ViewType Atualizado

```csharp
public enum ViewType
{
    Terminal,
    Dashboard,
    ProcessMonitor,
    Settings,
    Browser        // NOVO
}
```

## Apêndice C — Arquivos a Criar (Resumo)

```
src/DevWorkspaceHub/
├── Services/Browser/
│   ├── IBrowserRuntimeService.cs
│   ├── BrowserRuntimeService.cs
│   ├── ILocalAppSessionService.cs
│   ├── LocalAppSessionService.cs
│   ├── IDomSelectionService.cs
│   ├── DomSelectionService.cs
│   ├── IElementContextBuilder.cs
│   ├── ElementContextBuilder.cs
│   ├── IAiContextRouter.cs
│   ├── AiContextRouter.cs
│   ├── IElementScreenshotService.cs
│   ├── ElementScreenshotService.cs
│   ├── ICodeMappingService.cs
│   ├── CodeMappingService.cs
│   ├── ISelectionHistoryService.cs
│   └── SelectionHistoryService.cs
├── Models/Browser/
│   ├── ElementCaptureData.cs
│   ├── ElementContext.cs
│   ├── BrowserSession.cs
│   ├── SelectionHistoryEntry.cs
│   ├── CodeMapping.cs
│   ├── AgentTarget.cs
│   ├── PageInspectionState.cs
│   └── LinkedProjectRuntime.cs
├── ViewModels/
│   ├── BrowserViewModel.cs
│   └── ElementContextViewModel.cs
├── Views/
│   ├── BrowserView.xaml
│   └── ElementContextPanel.xaml
├── Controls/
│   ├── BrowserToolbar.xaml
│   └── ElementPreviewCard.xaml
└── Resources/Scripts/
    ├── element-picker.js
    ├── selector-generator.js
    └── dom-serializer.js
```

Total: **~30 arquivos novos**, zero modificação destrutiva nos existentes.
Modificações em existentes: `App.xaml.cs` (DI), `MainViewModel.cs` (ViewType.Browser), `MainWindow.xaml` (tab Browser).
