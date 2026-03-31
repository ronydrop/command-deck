# CLAUDE.md — DevWorkspaceHub

Terminal manager e dashboard de desenvolvimento para Windows. Gerencia múltiplas sessões de terminal, projetos, Git e processos em um único painel desktop.

## Stack
C# 12 + .NET 8 + WPF (net8.0-windows) | MVVM via CommunityToolkit.Mvvm 8.2.2 | DI via Microsoft.Extensions.DependencyInjection | ConPTY (P/Invoke) | System.Management (WMI)

## Comandos

```bash
dotnet restore
dotnet build
dotnet run --project src/DevWorkspaceHub

# Ou pelo script Windows:
Iniciar.bat    # valida .NET 8, restaura dependências, roda o projeto
```

## Arquitetura (MVVM)

```
Views (XAML) ↔ ViewModels (ObservableObject, RelayCommand) → Services → Models/Helpers
```

**DI setup em `App.xaml.cs`** — todos os serviços registrados como Singleton, `ProjectEditViewModel` como Transient.

**Estrutura de pastas:**
- `src/DevWorkspaceHub/Views/` — XAML das telas
- `src/DevWorkspaceHub/ViewModels/` — lógica de apresentação
- `src/DevWorkspaceHub/Services/` — regras de negócio (interfaces + implementações)
- `src/DevWorkspaceHub/Models/` — entidades
- `src/DevWorkspaceHub/Helpers/` — ConPtyHelper (P/Invoke), AnsiParser (state machine VT100)
- `src/DevWorkspaceHub/Converters/` — value converters para XAML
- `src/DevWorkspaceHub/Resources/` — Styles.xaml (tema Catppuccin Mocha), Icons.xaml

**Serviços principais:**
- `TerminalService` — orquestra sessões ConPTY (CreateSession, Write, Resize, Close). Eventos: OutputReceived, SessionExited, TitleChanged
- `ProjectService` — CRUD + auto-scan de projetos. Persistência JSON em `%APPDATA%/DevWorkspaceHub/projects.json`
- `GitService` — spawna `git.exe` e parseia saída (branch, status, ahead/behind, diffs)
- `ProcessMonitorService` — WMI para monitorar node, php, artisan, npm, python, docker
- `SettingsService` — preferências em `%APPDATA%/DevWorkspaceHub/settings.json`

## Persistência

JSON em `%APPDATA%/DevWorkspaceHub/` — sem banco de dados.
- Thread-safe com `SemaphoreSlim`
- Serialização com `JsonStringEnumConverter` + `camelCase`
- Schema em `Project` e `AppSettings`

## Convenções C#

- `[ObservableProperty]` e `[RelayCommand]` do CommunityToolkit (source generators)
- Campos privados: prefixo `_` + camelCase (ex: `_terminalService`)
- Interfaces para todos os serviços (`ITerminalService`, `IProjectService`, etc.)
- `async/await` em toda a camada de serviços
- XML doc comments em tipos e métodos públicos
- Strings de UI em português, comentários de código em inglês/português

## Terminal Canvas (TerminalCanvasView)

Novo layout (30/03/2026): canvas draggável com cards de terminal.
- Drag para navegar, Ctrl+Scroll para zoom (25%–200%)
- Double-click para modo foco (terminal único)
- `TerminalView.xaml` mantido como backup — não deletar

## Nunca Fazer

- Não instalar pacotes NuGet sem verificar compatibilidade com `net8.0-windows`
- Não acessar `ConPtyHelper` ou APIs de terminal fora de `TerminalService`
- Não serializar enums sem `JsonStringEnumConverter`
- Não remover `TerminalView.xaml` (é o backup do layout legado)
- Não usar `Process.Start` para Git fora de `GitService`
- Não quebrar o padrão de DI — novos serviços devem ter interface e ser registrados em `App.xaml.cs`
