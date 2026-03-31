# Liquid Glass Premium Theme Overhaul

## Goal

Elevar os temas **Liquid Glass Light** e **Liquid Glass Dark** ao nível visual de produtos como macOS Sequoia, Arc Browser, Linear e Raycast — com glassmorphism convincente, hierarquia visual clara, contraste excelente e consistência total entre componentes.

---

## Diagnóstico dos Problemas Atuais

### Problemas Compartilhados (Light + Dark)

1. **Glass effect superficial** — Os gradientes `GlassBgBrush` usam apenas 2 GradientStops com transparência simples. Não há blur real (WPF não tem backdrop-blur nativo), nem simulação convincente de frosted glass. O resultado é um gradiente semi-transparente que parece mais "fade" do que "glass".

2. **Falta de tokens para estados** — Não existem cores dedicadas para `hover`, `active`, `focus`, `disabled` nos temas. O `Styles.xaml` usa `Opacity` para simular estados (ex: `AccentButton` hover = `Opacity 0.85`), o que é um pattern fraco — muda o visual do conteúdo inteiro em vez de só do background.

3. **Ausência de blur simulation** — WPF não tem `backdrop-filter: blur()`. Os temas não compensam isso com nenhuma técnica de simulação (noise texture, multi-layer gradient, ou frosted overlay).

4. **Sombras genéricas** — Todas as sombras usam `Color="#000000"` sem considerar o tema. No Light deveria ser cinza suave, no Dark deveria ter tom azulado. Os `BlurRadius` e `ShadowDepth` são uniformes em vez de escalonados por elevação.

5. **Hierarquia de superfícies fraca** — Existem `BaseBg`, `MantelBg`, `CrustBg`, `Surface0/1/2` mas a diferença visual entre camadas é sutil demais, especialmente no Light.

6. **Falta de inner glow / noise** — Glass real tem uma textura sutil de noise e inner glow. Os temas são "limpos demais" — parecem flat com transparência.

7. **Sem token de border-radius consistente** — O código usa `CornerRadius="8"` em botões, `"12"` em cards, `"6"` em inputs, `"4"` em menu items. Não há um sistema padronizado.

8. **Split button hover usa cores hardcoded** — `AccentButtonLeft/Right` usam `#44FFFFFF` e `#22FFFFFF` diretamente no template, não reagem ao tema.

### Problemas Específicos — Light

- **BaseBg `#F2F2F7` vs Surface0 `#FFFFFF`** — O contraste entre o fundo e os cards brancos é quase imperceptível. Cards não "saltam" do fundo.
- **GlassSidebarBrush** — Tem um tint azulado (`#D8E4E8F0`) que não combina com a paleta neutra do resto. Parece deslocado.
- **SubtextColor `#48484A`** — Contraste suficiente mas as cores intermediárias (`Surface1 #C6C6CC`, `Surface2 #A0A0A8`) são muito próximas entre si.
- **Canvas radial gradient** — Muito sutil, quase imperceptível. Deveria criar mais profundidade.
- **GlassTitleBarBrush** — Opaco (`#F0F4F8` → `#E4E8EE`), não tem sensação de glass.

### Problemas Específicos — Dark

- **BaseBg `#1A1A2E`** — O undertone azul é bom mas as superfícies (`Surface0 #24243A`, `Surface1 #32324E`) são muito próximas do fundo, criando pouca separação.
- **GlassHighlight `#30FFFFFF`** — Muito sutil para dark mode. A luminous edge precisa ser mais visível.
- **GlassBorder `#22FFFFFF`** — Quase invisível. Bordas em dark mode precisam de pelo menos `#33FFFFFF` para serem perceptíveis.
- **GlassTitleBarBrush opaco** — `#20203A` → `#1A1A32` sem qualquer transparência. Deveria ter alpha channel.
- **ElevatedShadow `#50000000`** — Muito forte e muito preto. Deveria usar um tom azul escuro para combinar com o undertone do tema.

---

## Nova Proposta Visual — Conceito

### Princípios de Design

1. **Layered Glass** — Cada camada (background → sidebar → cards → elevated → popups) tem um nível diferente de transparência, blur simulado e sombra.
2. **Chromatic Depth** — Sombras e highlights com tom de cor (não preto puro), criando profundidade cromática.
3. **Noise Texture** — Uma imagem PNG tileable 200x200 de noise a 2-3% opacity sobre superfícies glass para simular frosted glass.
4. **Consistent Elevation System** — 4 níveis de elevação (0, 1, 2, 3) com sombras e borders progressivos.
5. **State-aware Colors** — Cada estado interativo (rest, hover, active, focus, disabled) tem uma cor dedicada no tema.

---

## Sistema de Design Tokens (CSS-style, implementado como XAML resources)

### Tokens a Adicionar nos Temas

```
SECTION F: ELEVATION SHADOWS
  ElevationShadow0  — base level, no shadow (flat on surface)
  ElevationShadow1  — cards, inputs (subtle)
  ElevationShadow2  — dropdowns, popups (medium)
  ElevationShadow3  — modals, overlays (deep)

SECTION G: STATE COLORS
  HoverBg           — background tint on hover (semi-transparent)
  ActiveBg          — background tint on press/active
  FocusBorder       — border color for focused inputs
  DisabledFg        — foreground for disabled elements
  DisabledBg        — background for disabled elements

SECTION H: GLASS EXTENDED
  GlassNoiseOpacity — opacity for noise texture overlay (0.02-0.04)
  GlassInnerGlow    — inner top glow color
  GlassReflection   — secondary highlight for simulated reflection
  GlassFrost        — frosted overlay color (high-alpha white/black)

SECTION I: RADIUS TOKENS (in Styles.xaml, not per-theme)
  RadiusSmall  = 4   — pills, tags, small badges
  RadiusBase   = 8   — buttons, inputs, menu items
  RadiusMedium = 12  — cards, panels
  RadiusLarge  = 16  — modals, dialogs
  RadiusRound  = 9999 — fully round (avatar circles)
```

---

## Step-by-Step Plan

### Fase 1 — Design Tokens e Estrutura Base (estimativa: 2-3h)

#### Step 1.1: Expandir LiquidGlass.xaml (Light)

**Arquivo:** `src/CommandDeck/Resources/Themes/LiquidGlass.xaml`

Ações:
- Recalibrar cores base para maior contraste entre camadas:
  - `BaseBg`: `#F0F0F5` → manter (mas canvas usa gradient diferente)
  - `Surface0`: `#FFFFFF` → `#FAFBFF` (leve tint azul para não ser branco puro flat)
  - `Surface1`: `#C6C6CC` → `#D4D4DC` (mais separação do Surface2)
  - `Surface2`: `#A0A0A8` → `#9898A4` (manter como divisor)
  - `Overlay0`: `#7C7C84` → `#6E6E7A` (mais escuro para melhor leitura)

- Recalibrar Glass colors:
  - `GlassTop`: `#E8FFFFFF` → `#EAFFFFFF` (90%+ white, quase opaco no topo)
  - `GlassBottom`: `#A0F0F0F5` → `#88EEEEF5` (mais transparente embaixo)
  - `GlassHighlight`: `#99FFFFFF` → `#B0FFFFFF` (mais visível)
  - `GlassBorder`: `#66FFFFFF` → `#55D0D0D8` (não branco puro, usar cinza-frio para evitar brilho excessivo)
  - `GlassBorderSubtle`: `#33C0C0C8` → `#28B8B8C4`

- Adicionar novos tokens (Section F-H):
  - `ElevationShadow1Color`: `#12000020` (sombra azulada sutil)
  - `ElevationShadow2Color`: `#1E000028` 
  - `ElevationShadow3Color`: `#30000030`
  - `HoverBg`: `#0A000000` (10% black overlay)
  - `ActiveBg`: `#14000000`
  - `FocusBorder`: = AccentPurple
  - `DisabledFg`: `#A0A0A8`
  - `DisabledBg`: `#F0F0F4`
  - `GlassInnerGlow`: `#CCFFFFFF`
  - `GlassReflection`: `#18FFFFFF`
  - `GlassFrost`: `#D8F2F2F7` (high-alpha BaseBg — simula frosted sem blur)

- Recalibrar GlassSidebarBrush:
  - Remover tint azul deslocado
  - Usar gradient mais neutro: `#E8F0F0F5` → `#D8E8E8EE` → `#CCE0E0E8`

- Recalibrar GlassTitleBarBrush:
  - Adicionar alpha channel: `#E8F2F4F8` → `#D8EAECF2` (semi-transparente)

- Recalibrar CanvasDepthBrush:
  - Aumentar contraste do gradient radial para criar mais profundidade

- Adicionar novos brushes:
  - `HoverBgBrush` — SolidColorBrush from HoverBg
  - `ActiveBgBrush` — SolidColorBrush from ActiveBg
  - `FocusBorderBrush` — SolidColorBrush (= AccentBrush ou FocusBorder)
  - `GlassFrostBrush` — SolidColorBrush from GlassFrost
  - `GlassInnerGlowBrush` — SolidColorBrush from GlassInnerGlow

#### Step 1.2: Expandir LiquidGlassDark.xaml (Dark)

**Arquivo:** `src/CommandDeck/Resources/Themes/LiquidGlassDark.xaml`

Ações:
- Recalibrar cores base para maior separação:
  - `BaseBg`: `#1A1A2E` → `#161626` (mais escuro para dar mais room)
  - `MantelBg`: `#141424` → `#121220`
  - `CrustBg`: `#0F0F1C` → `#0C0C18`
  - `Surface0`: `#24243A` → `#22223A` (manter)
  - `Surface1`: `#32324E` → `#363656` (mais salto para hover states)
  - `Surface2`: `#444466` → `#4A4A70` (mais saturado)
  - `Overlay0`: `#5E5E80` → `#68688C` (mais brilhante para legibilidade)

- Recalibrar Glass colors:
  - `GlassTop`: `#C828284A` → `#CC2A2A4C` (ligeiramente mais opaco e saturado)
  - `GlassBottom`: `#881E1E38` → `#781A1A34` (mais transparente)
  - `GlassHighlight`: `#30FFFFFF` → `#40FFFFFF` (mais luminous)
  - `GlassBorder`: `#22FFFFFF` → `#33FFFFFF` (mais visível)
  - `GlassBorderSubtle`: `#14FFFFFF` → `#1CFFFFFF`

- Adicionar novos tokens:
  - `ElevationShadow1Color`: `#20000018` (sombra azul-escuro)
  - `ElevationShadow2Color`: `#38000020`
  - `ElevationShadow3Color`: `#50000028`
  - `HoverBg`: `#12FFFFFF` (white overlay sutil)
  - `ActiveBg`: `#20FFFFFF`
  - `FocusBorder`: = AccentPurple
  - `DisabledFg`: `#4A4A68`
  - `DisabledBg`: `#1E1E30`
  - `GlassInnerGlow`: `#18FFFFFF`
  - `GlassReflection`: `#0CFFFFFF`
  - `GlassFrost`: `#CC1A1A2E` (high-alpha BaseBg)

- Recalibrar GlassSidebarBrush:
  - Adicionar mais saturação e profundidade
  - `#D0181832` → `#B8141428` → `#A0101020` (mais profundo)

- Recalibrar GlassTitleBarBrush:
  - Adicionar transparência: `#CC20203C` → `#B81A1A34`

- Recalibrar ElevatedShadow:
  - `#50000000` → `#4000000F` (menos intenso, com blue undertone)

- Adicionar mesmos novos brushes que o Light (HoverBgBrush, ActiveBgBrush, etc.)

#### Step 1.3: Adicionar Radius Tokens em Styles.xaml

**Arquivo:** `src/CommandDeck/Resources/Styles.xaml`

Ações:
- Adicionar CornerRadius resources no topo:
  ```xml
  <CornerRadius x:Key="RadiusSmall">4</CornerRadius>
  <CornerRadius x:Key="RadiusBase">8</CornerRadius>
  <CornerRadius x:Key="RadiusMedium">12</CornerRadius>
  <CornerRadius x:Key="RadiusLarge">16</CornerRadius>
  ```
- Adicionar Thickness resources para spacing consistente:
  ```xml
  <Thickness x:Key="GlassBorderThicknessNormal">1</Thickness>
  <Thickness x:Key="GlassBorderThicknessMedium">1.5</Thickness>
  ```

#### Step 1.4: Atualizar Non-Glass Themes para Compatibilidade

**Arquivos:**
- `src/CommandDeck/Resources/Themes/CatppuccinMocha.xaml`
- `src/CommandDeck/Resources/Themes/Dracula.xaml`
- `src/CommandDeck/Resources/Themes/VSCodeDark.xaml`
- `src/CommandDeck/Resources/Themes/VSCodeLight.xaml`

Ações:
- Adicionar fallback flat brushes para todos os novos tokens (HoverBgBrush, ActiveBgBrush, etc.) para que os temas não quebrem.

---

### Fase 2 — Melhorias em Styles.xaml (estimativa: 3-4h)

#### Step 2.1: Refatorar AccentButton

**Arquivo:** `src/CommandDeck/Resources/Styles.xaml`

Mudanças:
- Remover uso de `Opacity` para hover/press — substituir por cores dedicadas
- Hover: usar cor accent ligeiramente mais clara (ou accent + overlay branco)
- Press: usar cor accent ligeiramente mais escura
- Adicionar `Transition`-like animation (DoubleAnimation em Opacity de um overlay Border)
- Usar `RadiusBase` token

#### Step 2.2: Refatorar GhostButton e IconButton

Mudanças:
- Hover: usar `HoverBgBrush` em vez de `Surface0Brush` (que é opaco)
- Active: usar `ActiveBgBrush`
- Melhorar CornerRadius para `RadiusBase`
- Adicionar transição suave via EventTrigger + Storyboard (150ms)

#### Step 2.3: Refatorar DarkTextBox (input)

Mudanças:
- Background: usar glass-tinted background em vez de `Surface0Brush` opaco
  - Alternativa: manter opaco mas adicionar inner shadow sutil
- Border: `1px GlassBorderSubtle` em rest, `1px AccentBrush` em focus
- Hover: border intermediário (`Surface2Brush`)
- Adicionar focus ring: outer glow sutil via DropShadowEffect com accent color
- CornerRadius: usar `RadiusBase`

#### Step 2.4: Refatorar DarkComboBox (select)

Mudanças:
- Popup dropdown: adicionar DropShadowEffect elevation 2
- Items hover: usar `HoverBgBrush`
- CornerRadius dropdown: `RadiusMedium`

#### Step 2.5: Refatorar Card style

Mudanças:
- Background: manter `GlassBgBrush` (será melhorado nos tokens)
- Adicionar inner Border para GlassHighlight (simula inner glow no topo)
- Shadow: usar elevation system tokens
- CornerRadius: `RadiusMedium`

#### Step 2.6: Refatorar ListBox / ListBoxItem

Mudanças:
- Hover: `HoverBgBrush` com transição
- Selected: borda esquerda accent + background `Surface0Brush` com opacity sutil
- CornerRadius: `RadiusBase`

#### Step 2.7: Refatorar ContextMenu e MenuItem

Mudanças:
- ContextMenu border: usar `GlassBorderBrush` com CornerRadius `RadiusMedium`
- Shadow: elevation 2
- MenuItem hover: `HoverBgBrush` com transição
- CornerRadius item: `RadiusSmall`

#### Step 2.8: Refatorar ToolTip

Mudanças:
- Background: `GlassFrostBrush` (simula frosted glass)
- Border: `GlassBorderBrush` 
- Shadow: elevation 1
- CornerRadius: `RadiusBase`

#### Step 2.9: Refatorar ScrollBar

Mudanças:
- Thumb: CornerRadius mais arredondado (4→5)
- Track: mais fino (8px→6px) com expand on hover para 8px
- Opacity rest: 0.4 (mais sutil quando idle)
- Hover: opacity 0.7 + accent tint sutil

#### Step 2.10: Refatorar Tab Items (TerminalTabItem)

Mudanças:
- Selected: bottom border accent + glass background sutil
- Hover: `HoverBgBrush`
- Transição suave entre estados

---

### Fase 3 — Melhorias em Views/Controls (estimativa: 3-4h)

#### Step 3.1: MainWindow — Toolbar e Status Bar

**Arquivo:** `src/CommandDeck/Views/MainWindow.xaml`

Mudanças:
- Toolbar: manter `GlassToolbarBrush` mas reduzir bottom border opacity para 0.5
- Status bar: mesmo approach, top border opacity 0.5
- Separadores verticais na toolbar: usar `Surface1Brush` com Opacity 0.4

#### Step 3.2: MainWindow — Sidebar

Mudanças:
- Right border: usar `GlassBorderSubtle` em vez de `GlassBorderBrush` (mais sutil)
- Sidebar header "Command Deck": adicionar letter-spacing sutil (CharacterSpacing)
- Project avatars collapsed: adicionar hover glow sutil

#### Step 3.3: CanvasCardControl — Glass Card Premium

**Arquivo:** `src/CommandDeck/Controls/CanvasCardControl.xaml`

Mudanças:
- Card border: manter `GlassBgBrush` + `GlassBorderBrush`
- Inner highlight border: `GlassHighlightBrush` (já existe, será melhorado via tokens)
- Shadow hover: usar elevation system (rest=1, hover=2)
- Glow border: reduzir `BlurEffect Radius` de 5 para 4, accent opacity de 0.45 para 0.35 (mais sutil e elegante)
- Title bar: `GlassTitleBarBrush` com separator line mais sutil (Opacity 0.3)
- Entrance animation: manter 200ms, já está bom

#### Step 3.4: DashboardView — Cards

**Arquivo:** `src/CommandDeck/Views/DashboardView.xaml`

Mudanças:
- Os Cards já usam `Style="{StaticResource Card}"` — serão melhorados via Step 2.5
- Branch badge: usar `HoverBgBrush` background + accent text

#### Step 3.5: SettingsView — Form Cards

**Arquivo:** `src/CommandDeck/Views/SettingsView.xaml`

Mudanças:
- Cards de settings: já usam Card style
- Inputs dentro dos cards: já usam DarkTextBox/DarkComboBox — melhorados via Fase 2

#### Step 3.6: ProjectEditView — Modal Dialog

**Arquivo:** `src/CommandDeck/Views/ProjectEditView.xaml`

Mudanças:
- Backdrop: `#80000000` → `#60000000` (menos opaco, mais glass-like)
- Dialog border: usar elevation 3 shadow
- CornerRadius: `RadiusLarge`

#### Step 3.7: CommandPaletteOverlay

**Arquivo:** `src/CommandDeck/Views/CommandPaletteOverlay.xaml`

Mudanças:
- Backdrop: glassmorphism feel (usar GlassFrost)
- Search input: premium glass input
- Results list: usar HoverBgBrush para items

#### Step 3.8: NotificationToast

**Arquivo:** `src/CommandDeck/Controls/NotificationToast.xaml`

Mudanças:
- Background: `GlassBgBrush` com `GlassBorderBrush`
- Shadow: elevation 2
- CornerRadius: `RadiusMedium`
- Slide-in animation: já deve existir, verificar

#### Step 3.9: AssistantPanelView — AI Panel

**Arquivo:** `src/CommandDeck/Views/AssistantPanelView.xaml`

Mudanças:
- Left border: `GlassBorderSubtle`
- Background: `GlassSidebarBrush` ou similar
- Input area: glass-tinted

---

### Fase 4 — Noise Texture e Polimento Final (estimativa: 1-2h)

#### Step 4.1: Criar Noise Texture

Criar uma imagem PNG 200x200 de noise gaussiano sutil (2-4% opacity, grayscale).

**Arquivo novo:** `src/CommandDeck/Resources/Assets/noise-texture.png`

#### Step 4.2: Adicionar Noise Overlay em Styles.xaml

Criar um `VisualBrush` ou `ImageBrush` tileable que pode ser aplicado como segundo layer em cards glass.

Alternativa se performance for concern: aplicar noise apenas no Card style e no Sidebar, não globalmente.

#### Step 4.3: Ajustar Motion Tokens

Padronizar durações:
- Hover in: 150ms (já definido como DurationFast)
- Hover out: 200ms (ligeiramente mais lento para suavidade)
- Focus: 200ms
- Panel slide: 280ms (DurationNormal)
- Easing: CubicEase EaseOut para hovers, CubicEase EaseInOut para panels

#### Step 4.4: Revisar Contraste WCAG

Verificar que todas as combinações texto/fundo atendem WCAG AA (4.5:1 para texto normal, 3:1 para texto grande):

Light:
- TextColor `#1D1D1F` on BaseBg `#F0F0F5` → ~15:1 ✓
- SubtextColor `#48484A` on Surface0 `#FAFBFF` → ~6:1 ✓
- CaptionText (Overlay0) → verificar se `#6E6E7A` on white atinge 4.5:1

Dark:
- TextColor `#F0F0F8` on BaseBg `#161626` → ~14:1 ✓
- SubtextColor `#A8A8C0` on Surface0 `#22223A` → ~5.5:1 ✓
- CaptionText (Overlay0) → verificar se `#68688C` on dark bg atinge 4.5:1

---

## Arquivos que Serão Modificados

| Arquivo | Tipo de Mudança |
|---------|----------------|
| `Resources/Themes/LiquidGlass.xaml` | Recalibrar cores + adicionar 15+ novos tokens |
| `Resources/Themes/LiquidGlassDark.xaml` | Recalibrar cores + adicionar 15+ novos tokens |
| `Resources/Themes/CatppuccinMocha.xaml` | Adicionar fallbacks para novos tokens |
| `Resources/Themes/Dracula.xaml` | Adicionar fallbacks para novos tokens |
| `Resources/Themes/VSCodeDark.xaml` | Adicionar fallbacks para novos tokens |
| `Resources/Themes/VSCodeLight.xaml` | Adicionar fallbacks para novos tokens |
| `Resources/Styles.xaml` | Refatorar ~12 estilos + adicionar radius/spacing tokens |
| `Controls/CanvasCardControl.xaml` | Ajustar shadow, glow, highlight |
| `Views/MainWindow.xaml` | Ajustar toolbar, sidebar, status bar borders |
| `Views/CommandPaletteOverlay.xaml` | Glass backdrop |
| `Views/ProjectEditView.xaml` | Modal shadow + backdrop |
| `Controls/NotificationToast.xaml` | Glass + shadow |
| `Views/AssistantPanelView.xaml` | Glass panel |
| `Resources/Assets/noise-texture.png` | **NOVO** — noise texture |

---

## Validação

1. **Build test**: `dotnet build` deve compilar sem erros
2. **Theme switch test**: Alternar entre todos os 6 temas em Settings sem crash
3. **Visual regression**: Verificar cada view com ambos os temas glass:
   - Dashboard com projeto selecionado
   - Terminal Canvas com 3+ cards
   - Settings page
   - Command Palette overlay
   - Context menu
   - Notification toast
   - Project edit modal
4. **Contraste**: Verificar legibilidade de todos os textos (heading, body, caption, monospace)
5. **Hover states**: Verificar que todos os botões, inputs e list items respondem a hover/press/focus

---

## Riscos e Tradeoffs

1. **Performance** — Noise texture + DropShadowEffect em muitos elementos pode impactar performance. Mitigação: usar `BitmapCache` nos cards (já existe), limitar noise a poucos elementos.

2. **Sem backdrop-blur real** — WPF não suporta `backdrop-filter: blur()`. A simulação via `GlassFrost` (high-alpha BaseBg) é a melhor alternativa possível, mas nunca será idêntico a macOS/CSS. Tradeoff aceito.

3. **Compatibilidade com temas não-glass** — Adicionar fallback brushes em todos os temas existentes aumenta o boilerplate. Alternativa seria usar `TryFindResource` no code-behind, mas isso quebra o pattern XAML-only. Manter fallbacks explícitos é mais seguro.

4. **Radius tokens como CornerRadius** — WPF `CornerRadius` não é `double`, então não pode ser usado diretamente em ControlTemplates que esperam `CornerRadius`. Funciona como `StaticResource` em Styles e Borders.

5. **Mudança de cores base** — Alterar `BaseBg` e `Surface0` pode afetar componentes que usam essas cores diretamente (hardcoded). Mitigação: todas as referências usam `DynamicResource`, então a troca é automática.

---

## Open Questions

1. **Noise texture em WPF** — Precisa ser um PNG embeddado como Resource. Qual tamanho ideal? 200x200 a 72dpi é suficiente para tile sem pattern visível?

2. **Focus ring style** — Usar DropShadowEffect com accent color (macOS style) ou border duplo (Windows 11 style)?

3. **Scrollbar expand on hover** — Implementar com animation (6px→8px) ou manter largura fixa? Animation requer Storyboard no template.

4. **Glass intensity** — O nível de transparência proposto é baseado em análise visual. Pode precisar de ajuste fino após ver o resultado real na app.

---

## Ordem de Execução Recomendada

```
Fase 1 (tokens) → build test → Fase 2 (styles) → build test → Fase 3 (views) → build test + visual test → Fase 4 (polish) → final validation
```

Cada fase pode ser executada com subagents paralelos:
- Step 1.1 + 1.2 em paralelo (Light + Dark tokens)
- Step 1.3 + 1.4 em paralelo (Styles radius + fallbacks)
- Fase 2 steps são sequenciais (todos no mesmo arquivo)
- Fase 3 steps podem ser paralelos (cada view é independente)

Total estimado: **~10-13h de trabalho de implementação**.
