# AGENTS.md — CommandDeck

Terminal manager and development dashboard for Windows. Manages multiple terminal sessions, projects, Git, and processes in a single desktop panel.

## Stack
C# 12 + .NET 8 + WPF (net8.0-windows) | MVVM via CommunityToolkit.Mvvm 8.2.2 | DI via Microsoft.Extensions.DependencyInjection | ConPTY (P/Invoke) | System.Management (WMI)

## Commands

```bash
dotnet restore
dotnet build
dotnet run --project src/CommandDeck
```

## Architecture (MVVM)

```
Views (XAML) <-> ViewModels (ObservableObject, RelayCommand) -> Services -> Models/Helpers
```

**DI setup in `App.xaml.cs`** — all services registered as Singleton, `ProjectEditViewModel` as Transient.

### Folder Structure
- `src/CommandDeck/Views/` — XAML screens
- `src/CommandDeck/ViewModels/` — presentation logic
- `src/CommandDeck/Services/` — business logic (interfaces + implementations)
- `src/CommandDeck/Models/` — entities
- `src/CommandDeck/Helpers/` — ConPtyHelper (P/Invoke), AnsiParser (VT100 state machine)
- `src/CommandDeck/Converters/` — XAML value converters
- `src/CommandDeck/Resources/` — Styles.xaml (Catppuccin Mocha theme), Icons.xaml
- `src/CommandDeck/Controls/` — reusable WPF custom controls

### Key Services
- `TerminalService` — orchestrates ConPTY sessions (CreateSession, Write, Resize, Close). Events: OutputReceived, SessionExited, TitleChanged
- `ProjectService` — CRUD + auto-scan of projects. JSON persistence at `%APPDATA%/CommandDeck/projects.json`
- `GitService` — spawns `git.exe` and parses output (branch, status, ahead/behind, diffs)
- `ProcessMonitorService` — WMI to monitor node, php, artisan, npm, python, docker
- `SettingsService` — preferences at `%APPDATA%/CommandDeck/settings.json`
- `AssistantService` — AI chat integration (Ollama, OpenAI, OpenRouter providers)
- `ProjectSwitchService` — handles project switching with loading overlay and terminal lifecycle

## Persistence

JSON files at `%APPDATA%/CommandDeck/` — no database.
- Thread-safe with `SemaphoreSlim`
- Serialization with `JsonStringEnumConverter` + `camelCase`
- Schema defined in `Project` and `AppSettings` models

## C# Conventions

- `[ObservableProperty]` and `[RelayCommand]` from CommunityToolkit (source generators)
- Private fields: `_` prefix + camelCase (e.g., `_terminalService`)
- Interfaces for all services (`ITerminalService`, `IProjectService`, etc.)
- `async/await` throughout the service layer
- XML doc comments on public types and methods
- UI strings in Portuguese, code comments in English/Portuguese

## Terminal Canvas (TerminalCanvasView)

Canvas-based terminal layout with draggable cards.
- Drag to navigate, Ctrl+Scroll for zoom (25%-200%)
- Double-click for focus mode (single terminal)
- `FreeCanvasLayoutStrategy` handles 4-column grid auto-layout
- `TerminalView.xaml` kept as legacy layout backup — do NOT delete

## UI Components
- `AiOrbControl` — floating AI assistant orb with glow animation
- `DynamicIslandWindow` — macOS-style dynamic island for notifications
- `CanvasCardControl` — terminal card with rounded corners and shadow
- `RadialMenuControl` — radial context menu for quick actions
- `NumericUpDownControl` — numeric spinner for settings

## AI Integration
- Multiple providers: Ollama (local), OpenAI, OpenRouter
- `AssistantPanelView` — chat interface with streaming support
- Provider selection via settings dropdown

## Rules

- Do NOT install NuGet packages without checking `net8.0-windows` compatibility
- Do NOT access `ConPtyHelper` or terminal APIs outside of `TerminalService`
- Do NOT serialize enums without `JsonStringEnumConverter`
- Do NOT remove `TerminalView.xaml` (legacy layout backup)
- Do NOT use `Process.Start` for Git outside of `GitService`
- Do NOT break DI pattern — new services must have an interface and be registered in `App.xaml.cs`
- Do NOT add unnecessary abstractions or speculative features
- Prefer editing existing files over creating new ones
