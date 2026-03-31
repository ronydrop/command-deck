# Plan: Fix MC3088 in CanvasCardControl.xaml

## Goal
Fix XAML build error MC3088 at line 286 of the Windows file.

## Root Cause Analysis

Error MC3088: "property elements cannot be in the middle of element content"

In WPF XAML, a **property element** (e.g., `<Grid.ContextMenu>`) must appear either ALL before
or ALL after the **content children** (e.g., `<Rectangle>`, `<DockPanel>`).

The current TitleBar `<Grid>` mixes them:

```
<Grid x:Name="TitleBar">                  ← opens
    <Grid.Background>...</Grid.Background> ← property element ✅ (before content)
    <Grid.Clip>...</Grid.Clip>             ← property element ✅ (before content)
    <Rectangle ... />                      ← CONTENT child (line 228)
    <Grid.ContextMenu>...</Grid.ContextMenu> ← property element ❌ AFTER content!
    <DockPanel ... />                      ← CONTENT child (line 286) ← ERROR HERE
</Grid>
```

The `<Rectangle>` on line 228 is a content child. Then `<Grid.ContextMenu>` on line 234
is a property element AFTER a content child. WPF requires all property elements to be
grouped together, either all before or all after content children.

## Fix

Move `<Grid.ContextMenu>` block (lines 234-284) BEFORE the `<Rectangle>` (line 228),
grouping it with the other property elements (`<Grid.Background>`, `<Grid.Clip>`).

The order should be:
1. `<Grid.Background>` (property element)
2. `<Grid.Clip>` (property element) 
3. `<Grid.ContextMenu>` (property element) ← moved here
4. `<Rectangle>` (content child)
5. `<DockPanel>` (content child)

## Files to Change
- `/mnt/c/Users/ronyo/Desktop/Rony/Projetos/CommandDeck/src/CommandDeck/Controls/CanvasCardControl.xaml`

## Validation
- Build via .bat should pass this error
