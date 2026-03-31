# Revisão Técnica — CommandDeck Multi-Subagent Integration

Data: 2026-03-30
Escopo: 4 subagents modificaram simultaneamente o mesmo projeto WPF/.NET 8.
Arquivos analisados: 14 arquivos (CS + XAML + resources).

---

## RESUMO GERAL

A integração é majoritariamente sólida. A arquitetura MVVM foi respeitada, o DI está correto e a maior parte dos bindings XAML estão válidos. Existem **3 problemas críticos** causados pela execução paralela dos subagents (race condition em write_file) e por uma incompatibilidade arquitetural entre o serviço de câmera e as transformações da View.

---

## PROBLEMAS CRÍTICOS

### C1 — MiniMapControl ausente do TerminalCanvasView.xaml

**Causa:** Subagent 2 fez `write_file` completo em TerminalCanvasView.xaml. Subagent 3 fez `patch` no mesmo arquivo em paralelo. O `write_file` do S2 sobrescreveu os patches do S3. O arquivo final tem o novo toolbar, sidebar e dot-grid do S2, mas não tem o MiniMapControl do S3.

**Evidência:** Lendo as 575 linhas de TerminalCanvasView.xaml, o ViewportArea Grid (linhas 191–333) contém apenas: dot-grid Canvas, WorldCanvas, FocusOverlay Rectangle e EmptyState StackPanel. Nenhum `ctrl:MiniMapControl`.

**Impacto:** O mini-mapa é instanciado no DI e exposto no ViewModel, mas nunca renderizado. Funcionalidade totalmente ausente em runtime.

**Correção exata:** Adicionar dentro do ViewportArea Grid, após o `</Canvas>` do WorldCanvas (linha 303) e antes de `</Grid>` (linha 333):

```xml
<!-- Mini-map overlay — canto inferior direito do viewport -->
<ctrl:MiniMapControl
    HorizontalAlignment="Right"
    VerticalAlignment="Bottom"
    Margin="0,0,16,16"
    Panel.ZIndex="100"
    DataContext="{Binding CanvasViewModel.MiniMap}" />
```

O namespace `xmlns:ctrl` já está declarado na linha 7. Funciona sem mudanças adicionais.

---

### C2 — ExitFocusMode — regressão: PopCameraSnapshot retorna estado obsoleto (0, 0, 1.0)

**Causa:** Subagent 1 modificou `ExitFocusMode` para preferir `PopCameraSnapshot()` sobre os campos `_preFocusScale/X/Y`. Mas `ICanvasCameraService.Current` **nunca é atualizado** durante pan e zoom do usuário — a View manipula `CanvasTranslate.X/Y` e `CanvasScale.ScaleX/Y` diretamente, sem passar pelo serviço. Logo o snapshot salvo em `RequestFocus → SaveSnapshot()` sempre contém `{OffsetX=0, OffsetY=0, Zoom=1}` (valores iniciais do serviço, nunca alterados por uso normal).

**Evidência:**
- `OnViewportMouseMove` (linha 147): `CanvasTranslate.X = _panOriginX + pos.X - _panStart.X` — não chama `_cameraService.Pan()`
- `OnViewportMouseWheel` (linha 218): anima `CanvasTranslate/CanvasScale` diretamente — não chama `_cameraService.ZoomToPoint()`
- `ApplyMomentum` (linha 274): `CanvasTranslate.X += _momentumVelX * 0.016` — não chama serviço
- `AnimateFocusOnItem` (linha 465): salva `_preFocusScale = CanvasScale.ScaleX` e `_preFocusTransX = CanvasTranslate.X` — estes SÃO os valores corretos
- `ExitFocusMode` (linhas 497–512): usa `PopCameraSnapshot()` que retorna `snap.OffsetX = 0`, `snap.OffsetY = 0`, substituindo os valores corretos

**Impacto:** Ao sair do focus mode (ESC ou "Ver Todos"), a câmera anima para (zoom=1, X=0, Y=0) em vez de voltar à posição anterior. Regressão visível imediatamente.

**Correção exata** em `TerminalCanvasView.xaml.cs`, método `ExitFocusMode`:

```csharp
private void ExitFocusMode(bool animated)
{
    _canvasVm?.ExitFocusModeCommand.Execute(null);
    BtnVerTodos.Visibility = Visibility.Collapsed;
    AnimateFocusOverlay(0);

    if (!animated) return;

    // Usar sempre os campos _preFocus* — eles capturam CanvasTranslate/CanvasScale
    // diretamente antes do focus, que é a fonte de verdade da View.
    // PopCameraSnapshot() retornaria valores obsoletos (0,0,1) porque o serviço
    // não é atualizado durante pan/zoom normal.
    AnimateCanvasTo(_preFocusScale, _preFocusTransX, _preFocusTransY);
}
```

Remover as linhas 497–512 que consultam `PopCameraSnapshot`.

---

### C3 — MiniMap viewport rect nunca atualiza durante uso normal

**Causa:** `MiniMapViewModel.OnCameraChanged()` é subscriber de `ICanvasCameraService.CameraChanged`. Mas como demonstrado em C2, o serviço **nunca dispara** `CameraChanged` durante pan/zoom normal do usuário. Resultado: o retângulo de viewport no mini-mapa fica estático na posição inicial.

**Evidência:**
- `_cameraService.CameraChanged` só é disparado dentro de `Pan()`, `ZoomToPoint()`, `CenterOnItem()`, `RestoreSnapshot()` no service
- Nenhum desses é chamado de `OnViewportMouseMove`, `OnViewportMouseWheel`, ou `ApplyMomentum`
- `MiniMapViewModel._lastViewportW/H` são atualizados por `UpdateViewport()` que é chamado pelo `MiniMapControl` no Loaded, mas o viewport rect só recalcula em `OnCameraChanged`, que nunca dispara

**Impacto:** Mini-mapa mostra itens corretamente, mas o indicador de viewport não se move enquanto o usuário faz pan/zoom.

**Correção:** Adicionar um método de sync ao serviço e chamá-lo da View. Duas partes:

**Parte A — `ICanvasCameraService.cs`:**
```csharp
/// <summary>
/// Synchronises Current state from external source (View transforms) and fires CameraChanged.
/// Called by the View to keep the service in sync after direct transform manipulation.
/// </summary>
void SyncState(double offsetX, double offsetY, double zoom);
```

**Parte B — `CanvasCameraService.cs`:**
```csharp
public void SyncState(double offsetX, double offsetY, double zoom)
{
    Current.OffsetX = offsetX;
    Current.OffsetY = offsetY;
    Current.Zoom    = zoom;
    CameraChanged?.Invoke();
}
```

**Parte C — `TerminalCanvasView.xaml.cs`:** Adicionar chamadas nos pontos de mudança de transform:

Em `OnViewportMouseMove`, após atualizar os transforms:
```csharp
// Ao terminar de mover (não a cada frame — no MouseUp é suficiente para o minimap)
```

Em `OnViewportMouseUp`, após parar o pan:
```csharp
_canvasVm?.SyncCamera(CanvasTranslate.X, CanvasTranslate.Y, CanvasScale.ScaleX);
```

Em `ApplyMomentum`, na condição de stop:
```csharp
_canvasVm?.SyncCamera(CanvasTranslate.X, CanvasTranslate.Y, CanvasScale.ScaleX);
```

Nos `Completed` handlers do zoom e do `AnimateCanvasTo`:
```csharp
_canvasVm?.SyncCamera(finalTransX, finalTransY, finalScale);
```

**Parte D — `TerminalCanvasViewModel.cs`:** Expor o sync:
```csharp
public void SyncCamera(double offsetX, double offsetY, double zoom)
    => _cameraService.SyncState(offsetX, offsetY, zoom);
```

**Nota:** Para pan contínuo em tempo real no minimap, chamar `SyncCamera` dentro de `OnViewportMouseMove` também (a cada frame). O custo é mínimo pois só atualiza propriedades numéricas.

---

## PROBLEMAS MÉDIOS

### M1 — Dead code: `OnTitleBarDoubleClick` em CanvasCardControl.xaml.cs

Método definido na linha 123 mas nunca conectado ao XAML. O double-click já é tratado em `OnTitleBarMouseDown` (ClickCount == 2). Não causa erro de compilação mas é enganoso.

**Correção:** Remover o método `OnTitleBarDoubleClick`.

---

### M2 — `CanvasItemType` em MiniMapViewModel — verificar enum

`MiniMapViewModel.cs` linha 187:
```csharp
IsTerminal = item.ItemType == CanvasItemType.Terminal,
```

`CanvasItemType` precisa existir como enum com membro `Terminal`. Verificar `CanvasItemViewModel.cs` e `CanvasItemModel.cs`. Se o enum tiver outro nome (ex: `ItemType`, `CanvasItemKind`), haverá erro de compilação.

**Ação:** Ler `CanvasItemViewModel.cs` e confirmar o tipo da propriedade `ItemType`.

---

### M3 — `AnimateCardOut` causa elemento "fantasma" se remoção falhar

`AnimateCardOut` seta `card.Opacity = 0` no `Completed` e só então chama `onComplete`. Se `CloseTerminalCommand` for assíncrono e não remover o item da coleção imediatamente (ex: aguarda confirmação de IO), o card fica invisível mas presente. Improvável em prática mas possível.

**Correção sugerida:** Garantir que `CloseTerminalCommand` em `MainViewModel` é síncrono para a remoção do item da coleção `Items`, mesmo que o cleanup do terminal seja async.

---

## PROBLEMAS LEVES

### L1 — ZoomLabel.Text não é binding

`ZoomLabel.Text` é atualizado diretamente em `UpdateZoomLabel()` no code-behind. Isso funciona, mas se `ZoomPercent` for atualizado por outro caminho (via `OnCameraChanged` após C3 ser corrigido), o label não atualizará automaticamente.

**Sugestão:** Fazer o label bindar em `CanvasViewModel.ZoomPercent` com StringFormat e remover a atribuição direta de `ZoomLabel.Text`. Mas requer `Converter` para formato `"{0}%"`. Baixa prioridade.

---

### L2 — `SidebarItem` DataTemplate — NameScope e TargetName

O `Storyboard.TargetName="HoverOverlay"` dentro de `Border.Triggers` de `SidebarItem` funciona porque ambos estão no mesmo NameScope do DataTemplate. **Confirmado correto.** ✅

---

### L3 — GlowBorder NameScope em CardBorder.Triggers

`CardBorder.Triggers` anima `GlowBorder` via `Storyboard.TargetName`. Ambos estão dentro do mesmo `UserControl` que tem um único NameScope. **Confirmado correto.** ✅

---

### L4 — Entrance animation em CanvasCardControl usa FillBehavior padrão (HoldEnd)

Intencional: a animação de entrada é one-shot e deve manter `Opacity=1` permanentemente. `HoldEnd` é correto aqui. ✅

---

### L5 — Dot-grid hairlines (opacity 0.04) podem ser invisíveis

As linhas `Layer 2a/2b` têm `Opacity="0.04"` num tile de 32x32px. Cada linha de 0.5px de espessura com 4% de opacidade pode ser imperceptível dependendo da resolução do monitor. Não é um bug, mas considerar aumentar para 0.06–0.08 se o efeito não aparecer em tela.

---

## ARQUIVOS COM STATUS CONFIRMADO

| Arquivo | Status |
|---|---|
| `Services/ICanvasCameraService.cs` | ✅ Consistente — todos os métodos implementados |
| `Services/CanvasCameraService.cs` | ✅ Consistente — PanBy, ComputeCenterOnItem, ComputeFocusItem, PopSnapshot ok |
| `ViewModels/TerminalCanvasViewModel.cs` | ✅ Construtor com MiniMapViewModel + PopCameraSnapshot coexistem |
| `App.xaml.cs` | ✅ DI: MiniMapViewModel registrado antes de TerminalCanvasViewModel |
| `ViewModels/MiniMapViewModel.cs` | ✅ Lógica de mapping correta, PropertyChanged filtrado, SafeScale guarda div-by-zero |
| `Models/MiniMapItemRect.cs` | ✅ Record com init properties, correto |
| `Controls/MiniMapControl.xaml` | ✅ Bindings corretos, ViewportRect com Canvas.Left/Top bindados |
| `Controls/MiniMapControl.xaml.cs` | ✅ FindHostViewportSize via VisualTree, drag logic correta |
| `Controls/CanvasCardControl.xaml` | ✅ GlowBorder + DragScale + CardShadow coexistem sem conflito |
| `Controls/CanvasCardControl.xaml.cs` | ✅ AnimateDragScale com FillBehavior.Stop, GetCanvasZoom via VisualTree |
| `Converters/ZoomLevelConverter.cs` | ✅ MarkupExtension singleton, retorna "low"/"high"/"normal" |
| `Resources/Styles.xaml` | ✅ Motion tokens (easings) adicionados antes de FONTS |
| `Resources/Icons.xaml` | ✅ CloseIcon existe — não há referência quebrada no close button |
| `Resources/Themes/VSCodeDark.xaml` | ✅ AccentYellowBrush, AccentBlueBrush, AccentMauveBrush presentes |
| `Views/TerminalCanvasView.xaml.cs` | ⚠️ Correto exceto ExitFocusMode (C2) |
| `Views/TerminalCanvasView.xaml` | ❌ MiniMapControl ausente (C1) |

---

## CORREÇÕES RECOMENDADAS — ORDEM DE EXECUÇÃO

### 1. Corrigir C1 — Adicionar MiniMapControl no XAML (5 min)

Arquivo: `Views/TerminalCanvasView.xaml`  
Inserir após linha 303 (fechamento `</Canvas>` do WorldCanvas):

```xml
<ctrl:MiniMapControl
    HorizontalAlignment="Right"
    VerticalAlignment="Bottom"
    Margin="0,0,16,16"
    Panel.ZIndex="100"
    DataContext="{Binding CanvasViewModel.MiniMap}" />
```

### 2. Corrigir C2 — Remover PopCameraSnapshot de ExitFocusMode (2 min)

Arquivo: `Views/TerminalCanvasView.xaml.cs`  
Substituir o bloco `ExitFocusMode` para usar apenas `_preFocusScale/X/Y`.

### 3. Corrigir C3 — Adicionar SyncState ao serviço e chamar da View (20 min)

Arquivos: `ICanvasCameraService.cs`, `CanvasCameraService.cs`, `TerminalCanvasViewModel.cs`, `TerminalCanvasView.xaml.cs`  
Seguir passos A–D da correção de C3 acima.

### 4. Verificar M2 — CanvasItemType enum (5 min)

Ler `ViewModels/CanvasItemViewModel.cs`. Confirmar que `item.ItemType == CanvasItemType.Terminal` compila.

### 5. Remover M1 — Dead code OnTitleBarDoubleClick (1 min)

---

## RISCOS REMANESCENTES APÓS CORREÇÕES

- **MiniMap update frequency:** após C3, cada frame de pan vai disparar `SyncState` → `CameraChanged` → `MiniMapViewModel.OnCameraChanged()`. Isso é leve (só recalcula `ViewportRect`, não `ItemRects`), mas monitorar performance com muitos itens.
- **AnimateCardOut e async close:** ver M3. Se aparecer ghost cards, revisar MainViewModel.CloseTerminalCommand.
- **Temas Dracula e VSCodeLight:** não foram verificados linha-a-linha para `AccentMauveBrush` (usado em MiniMapControl). Confirmar antes de testar com esses temas.
