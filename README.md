# Dev Workspace Hub

Terminal manager + dev dashboard nativo para Windows. Gerencia múltiplas instâncias de terminal por projeto com integração Git, monitoramento de processos e tema dark.

## Stack

- **C# / .NET 8** — WPF com MVVM (CommunityToolkit.Mvvm)
- **ConPTY** — Windows Pseudo Console API para terminais reais com VT100/ANSI
- **DI** — Microsoft.Extensions.DependencyInjection

## Features

- **Multi-terminal** — Tabs de terminais com WSL, PowerShell, CMD, Git Bash
- **Projetos** — Cadastro de projetos com path, shell padrão, startup commands
- **Git Integration** — Branch, status, último commit, ahead/behind
- **Process Monitor** — Detecta node, php, artisan, npm, python, docker com CPU/RAM
- **ANSI Parser** — Suporte completo a cores (standard, 256, true color), bold, italic, underline
- **Tema Dark** — Catppuccin Mocha com accent purple (#7C3AED)
- **Keyboard Shortcuts** — Ctrl+Shift+T (novo terminal), Ctrl+Tab (próximo), Ctrl+B (sidebar)

## Requisitos

- Windows 10 1809+ (ConPTY requer Windows 10 build 17763+)
- .NET 8 SDK
- Visual Studio 2022 ou Rider

## Build

```bash
cd DevWorkspaceHub
dotnet restore
dotnet build
dotnet run --project src/DevWorkspaceHub
```

Ou abra `DevWorkspaceHub.sln` no Visual Studio e pressione F5.

## Estrutura

```
src/DevWorkspaceHub/
├── Models/          → Entidades (Project, TerminalSession, GitInfo, ProcessInfo)
├── Services/        → Lógica de negócio (Terminal, Project, Git, ProcessMonitor)
├── ViewModels/      → MVVM ViewModels com CommunityToolkit
├── Views/           → XAML das telas
├── Controls/        → TerminalControl custom
├── Converters/      → Value converters para bindings
├── Helpers/         → ConPTY P/Invoke wrapper + ANSI parser
└── Resources/       → Styles (tema dark) + Icons (SVG paths)
```

## Configuração

Projetos e settings são salvos em:
- `%APPDATA%/DevWorkspaceHub/projects.json`
- `%APPDATA%/DevWorkspaceHub/settings.json`
# dev-workspace-hub
