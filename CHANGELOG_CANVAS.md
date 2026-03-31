# CHANGELOG — Canvas de Terminais + Script Iniciar.bat

**Data**: 30/03/2026  
**Versão**: feature/terminal-canvas

---

## Resumo das Mudanças

### FEATURE 1 — Layout Canvas de Terminais

#### Arquivos Criados

**`src/CommandDeck/Views/TerminalCanvasView.xaml`**
- `UserControl` que substitui o `TerminalView.xaml` (TerminalView **não foi deletado**, mantido como backup).
- Estrutura com dois rows: barra de ferramentas (40px) + área do canvas arrastável.
- **Barra de ferramentas**: botão "Novo Terminal" (AccentButton, dispara `NewTerminalCommand`), indicador de zoom (ex: "100%"), botão "Ver Todos" (oculto quando não há foco ativo).
- **Área do canvas**: `Grid` com `ClipToBounds=True` em fundo `CrustBgBrush`, contendo um `Canvas` com `TransformGroup` (ScaleTransform + TranslateTransform). O `WrapPanel` interno organiza os cards automaticamente.
- **Estado vazio**: `StackPanel` centralizado com ícone e texto português, visível quando não há terminais.
- Converters declarados: `BoolToVisibilityConverter`, `TerminalStatusToColorConverter`.

**`src/CommandDeck/Views/TerminalCanvasView.xaml.cs`**
- Criação dinâmica de cards de terminal via `AddTerminalCard(TerminalViewModel)`.
- Cada card tem: `Border` (600×420px, MantelBg, Surface0 borda, CornerRadius=8, sombra), barra de título (CrustBg, status dot com binding `Session.Status`, TextBlock com binding `Title`, botão fechar que invoca `CloseTerminalCommand`), e `TerminalControl` embedded com `DataContext = tvm`.
- Sincronização com `ObservableCollection<TerminalViewModel>` via `INotifyCollectionChanged`.
- Resolução do `MainViewModel` via `Window.GetWindow(this).DataContext` no evento `Loaded` (compatível com a arquitetura existente onde o UserControl não recebe DataContext explícito).

**Drag para navegar:**
- `MouseDown` no canvas (exceto dentro de `TerminalControl`, `RichTextBox` ou `TextBox`) inicia drag.
- `IsDragTarget()` percorre a árvore visual para garantir que clicks dentro do terminal não ativem o drag.
- `MouseMove` atualiza `CanvasTranslate.X/Y`.
- `MouseUp` libera o capture de mouse.

**Zoom com Ctrl+Scroll:**
- `OnCanvasMouseWheel` só processa quando `Ctrl` está pressionado.
- Zoom centralizado no cursor: calcula posição do mouse no espaço do canvas antes e depois, ajusta `TranslateTransform` para manter o ponto sob o cursor fixo.
- Limites: min 25%, max 200%. Passo de 12% por scroll.
- Atualiza o `ZoomLabel` ("100%", "75%", etc).

**Modo foco (duplo-clique em card):**
- `EnterFocusMode(TerminalViewModel)`: calcula escala para ocupar 90% da área visível e centraliza o card com animação suave (280ms, `CubicEase EaseInOut`).
- `ExitFocusMode()`: calcula escala para caber todos os cards e centraliza com animação.
- `FillBehavior=Stop` nas animações + atribuição manual dos valores ao completar, garantindo que drag/zoom continuem funcionando após animar.
- Botão "Ver Todos" aparece quando em modo foco, fica oculto na visão geral.

**Foco de teclado:**
- O `TerminalControl` mantém seu padrão existente: `OnMouseDown` envia foco para `HiddenInput`.
- O drag do canvas NÃO interfere com o foco do terminal pois `IsDragTarget()` retorna `false` para qualquer evento originado dentro de `TerminalControl`.

#### Arquivos Modificados

**`src/CommandDeck/Views/MainWindow.xaml`**
- Linha 133: substituído `<views:TerminalView>` por `<views:TerminalCanvasView>`.
- Binding de `Visibility` mantido idêntico (ConverterParameter=Terminal).
- `TerminalView.xaml` e `TerminalView.xaml.cs` **preservados** sem alteração.

#### Arquivos NÃO Modificados

- `ViewModels/MainViewModel.cs` — nenhuma alteração necessária. O `NewTerminalCommand`, `CloseTerminalCommand`, e `ObservableCollection<TerminalViewModel> Terminals` funcionam diretamente com o novo canvas.
- `Controls/TerminalControl.xaml` / `.cs` — sem alterações.
- Todos os outros ViewModels, Services, Converters, Resources.

---

### FEATURE 2 — Script de Inicialização

**`Iniciar.bat`** (raiz do repositório)
- Encoding: ASCII/DOS batch (compatível com `cmd.exe` Windows).
- Verifica se o .NET 8 SDK está instalado (`dotnet --version`).
- Em caso de erro, exibe mensagem em português com link para download.
- Executa `dotnet restore` e depois `dotnet run` no projeto principal.
- `pause` ao final para manter a janela aberta.

---

## Validação Realizada

| Item | Status |
|------|--------|
| `TerminalCanvasView.xaml` — XML válido | ✅ Confirmado com parser XML Python |
| `MainWindow.xaml` — XML válido | ✅ Confirmado com parser XML Python |
| Todos os `x:Name` no XAML correspondem ao code-behind | ✅ |
| Todos os event handlers no XAML têm método correspondente no .cs | ✅ |
| Converters referenciados existem no namespace `CommandDeck.Converters` | ✅ |
| `using` directives cobrem todos os tipos usados | ✅ |
| `.csproj` usa SDK-style (auto-discover) — não precisa registrar novos arquivos | ✅ |
| `TerminalView.xaml` e `.cs` preservados como backup | ✅ |
| `Iniciar.bat` encoding ASCII/DOS | ✅ Confirmado com `file` command |
| Textos em português BR | ✅ |

---

## Arquitetura do Canvas

```
TerminalCanvasView (UserControl)
├── Grid (root)
│   ├── Border (toolbar, 40px)              ← MantelBgBrush
│   │   └── DockPanel
│   │       ├── StackPanel (right)
│   │       │   ├── Button "Ver Todos"      ← oculto por padrão
│   │       │   └── Border (zoom label)     ← "100%"
│   │       └── StackPanel (left)
│   │           ├── Button "Novo Terminal"  ← NewTerminalCommand
│   │           └── TextBlock "Canvas de Terminais"
│   └── Grid "CanvasArea" (*)              ← CrustBgBrush, ClipToBounds
│       ├── Canvas "RootCanvas"            ← ScaleTransform + TranslateTransform
│       │   └── WrapPanel "TerminalsPanel" ← cards adicionados dinamicamente
│       │       └── Border (card) × N
│       │           └── Grid
│       │               ├── Row 0: Border (titleBar)  ← título, status dot, fechar
│       │               └── Row 1: TerminalControl    ← DataContext = TerminalViewModel
│       └── StackPanel "EmptyState"        ← visível quando sem terminais
```

(*) Eventos de mouse para drag e zoom registrados no `CanvasArea`.
