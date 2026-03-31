# Plano: Persistência de Preferências IA + Histórico de Comandos + IsAvailable Dinâmico

Data: 2026-03-30
Projeto: CommandDeck — WPF .NET 8 / MVVM / CommunityToolkit.Mvvm
Base: /mnt/c/Users/ronyo/OneDrive/Downloads/CommandDeck/src/CommandDeck/

---

## Objetivo

Implementar três melhorias incrementais sem quebrar a arquitetura atual:

1. **Passo 3** — Persistir a escolha de provider de IA no SQLite e restaurá-la na inicialização
2. **Passo 4** — Registrar no SQLite cada comando enviado ao terminal
3. **Passo 5** — Tornar `IsAvailable` dos providers reativo/dinâmico via polling, para o badge do painel IA atualizar automaticamente

---

## Contexto atual

### AssistantService.cs
- `SwitchProvider(type)` já muda `_active` e atualiza `_settings.ActiveProvider` em memória
- Não persiste nada em disco
- Constructor carrega o provider baseado em `_settings.ActiveProvider` (padrão Ollama) — não lê SQLite

### IDatabaseService / DatabaseService
- Já existem: `SaveAssistantPreferencesAsync(providerName, model, apiKey?)` e `GetAssistantPreferencesAsync()`
- Tabela `AssistantPreferences` com row singleton `Id=1` (INSERT OR REPLACE)
- Já existe: `AddCommandHistoryAsync(sessionId, command, success)`

### AssistantPanelViewModel.cs
- `SwitchProviderCommand` chama `_assistant.SwitchProvider(type)` e `RefreshProviderInfo()`
- Não tem acesso a `IDatabaseService`
- `RefreshProviderInfo()` chama `_assistant.ActiveProvider.IsAvailable` de forma síncrona
- `IsProviderAvailable` é `[ObservableProperty] bool`

### IAssistantProvider / OllamaProvider
- `IsAvailable` é uma propriedade `bool` síncrona que faz HTTP GET a cada leitura (blocking!)
- Não é observável — é uma propriedade comum, não `[ObservableProperty]`

### TerminalViewModel.cs
- `SendKeyDataAsync(string data)` envia dados brutos para o ConPTY — inclui sequências de controle, caracteres individuais e comandos completos
- `ExecuteCommandAsync(string command)` envia `command + "\r"` — sempre um comando completo
- `SendInput()` (RelayCommand) envia `InputText + "\r"` — também um comando completo
- Session tem `.Id` disponível

---

## Passo 3 — Persistir preferência de provider no SQLite

### Problema
`SwitchProvider()` muda em memória mas perde ao reiniciar. `GetAssistantPreferencesAsync()` já existe mas nunca é chamado.

### Solução

**A. Injetar `IDatabaseService` em `AssistantService`**

Modificar o construtor de `AssistantService`:
```csharp
public AssistantService(
    IEnumerable<IAssistantProvider> providers,
    AssistantSettings settings,
    IDatabaseService db)
```

**B. Restaurar preferência no construtor (async-safe)**

No construtor, após resolver o provider padrão, adicionar inicialização assíncrona via método separado chamado externamente:
```csharp
// Opção preferida: método público de init, chamado no App.OnStartup
public async Task RestorePreferencesAsync()
{
    var prefs = await _db.GetAssistantPreferencesAsync();
    if (prefs is not null)
    {
        // Restaurar modelo e provider
        _settings.OllamaModel = prefs.Value.model;
        var found = ResolveProvider(
            prefs.Value.providerName == "OpenAI"
                ? AssistantProviderType.OpenAI
                : AssistantProviderType.Ollama);
        if (found is not null) _active = found;
    }
}
```

Ou mais simples: no construtor, fazer Task.Run().GetAwaiter().GetResult() com timeout curto. Mas o pattern do projeto usa `Task.Run` no OnStartup — preferir o método público.

**C. Persistir ao trocar**

Em `SwitchProvider()`:
```csharp
public void SwitchProvider(AssistantProviderType type)
{
    var found = ResolveProvider(type);
    if (found is not null)
    {
        _active = found;
        _settings.ActiveProvider = type;
        // Persistir de forma fire-and-forget
        _ = _db.SaveAssistantPreferencesAsync(
            found.ProviderName,
            type == AssistantProviderType.Ollama
                ? _settings.OllamaModel
                : _settings.OpenAIModel);
    }
}
```

**D. Chamar RestorePreferencesAsync no App.xaml.cs**

Em `OnStartup`, após `db.InitializeAsync()`:
```csharp
var assistantService = _serviceProvider!.GetRequiredService<IAssistantService>();
if (assistantService is AssistantService concreteService)
    await concreteService.RestorePreferencesAsync();
```

Ou expor `RestorePreferencesAsync` na interface `IAssistantService` para evitar cast.

### Arquivos modificados
- `Services/AssistantService.cs` — injetar IDatabaseService, adicionar RestorePreferencesAsync, modificar SwitchProvider
- `Services/IAssistantService.cs` — opcional: adicionar `Task RestorePreferencesAsync()`
- `App.xaml.cs` — chamar RestorePreferencesAsync no OnStartup

---

## Passo 4 — Histórico de comandos no SQLite

### Problema
`IDatabaseService.AddCommandHistoryAsync` existe mas nunca é chamado. `SendKeyDataAsync` recebe dados brutos (incluindo setas, backspace, chars individuais) — não é adequado para histórico. O ponto correto é registrar apenas comandos completos.

### Decisão de design
Registrar somente quando o usuário pressiona Enter num comando completo, não em cada keystroke. Os dois pontos exatos:
1. `ExecuteCommandAsync(command)` — sempre um comando completo + `\r`
2. `SendInput()` — InputText + `\r` (RelayCommand do campo de texto)

**NÃO** registrar em `SendKeyDataAsync` — recebe sequências VT100, chars individuais, setas etc.

### Solução

**A. Injetar `IDatabaseService` em `TerminalViewModel`**

`TerminalViewModel` é Transient no DI. Injetar `IDatabaseService` que é Singleton — OK.

Modificar construtor:
```csharp
public TerminalViewModel(ITerminalService terminalService, IDatabaseService db)
{
    _terminalService = terminalService;
    _db = db;
    ...
}
```

**B. Registrar em ExecuteCommandAsync**
```csharp
public async Task ExecuteCommandAsync(string command)
{
    if (Session == null) return;
    await _terminalService.WriteAsync(Session.Id, command + "\r");
    if (!string.IsNullOrWhiteSpace(command))
        _ = _db.AddCommandHistoryAsync(Session.Id, command.Trim());
}
```

**C. Registrar em SendInput (RelayCommand)**
```csharp
[RelayCommand]
private async Task SendInput()
{
    if (Session == null || string.IsNullOrEmpty(InputText)) return;
    var cmd = InputText;
    await _terminalService.WriteAsync(Session.Id, cmd + "\r");
    InputText = string.Empty;
    if (!string.IsNullOrWhiteSpace(cmd))
        _ = _db.AddCommandHistoryAsync(Session.Id, cmd.Trim());
}
```

**D. Atualizar o factory de Transient no App.xaml.cs**

`TerminalViewModel` é registrado como Transient via `Func<TerminalViewModel>`. O DI já vai injetar `IDatabaseService` automaticamente no construtor — nenhuma mudança extra no App.xaml.cs, apenas garantir que `TerminalViewModel` recebe `IDatabaseService` no construtor.

### Arquivos modificados
- `ViewModels/TerminalViewModel.cs` — injetar IDatabaseService, modificar ExecuteCommandAsync e SendInput

---

## Passo 5 — IsAvailable dinâmico via polling

### Problema
`OllamaProvider.IsAvailable` faz `GetAsync().GetAwaiter().GetResult()` bloqueando a thread a cada leitura. O badge do painel IA mostra o estado no momento em que o painel abre, mas não atualiza se o Ollama cair ou subir enquanto o app está aberto.

### Solução

**A. Adicionar `IsAvailableChanged` ao `IAssistantProvider`**

Alternativa 1: evento na interface
```csharp
event Action<bool>? AvailabilityChanged;
```

Alternativa 2 (mais simples, sem mudar a interface): o polling fica no `AssistantPanelViewModel` usando `DispatcherTimer`.

**Decisão: Alternativa 2** — Não alterar `IAssistantProvider` (já existe código dependente). Fazer polling no ViewModel.

**B. Adicionar DispatcherTimer em AssistantPanelViewModel**

```csharp
private readonly DispatcherTimer _availabilityTimer;

// No construtor, após RefreshProviderInfo():
_availabilityTimer = new DispatcherTimer
{
    Interval = TimeSpan.FromSeconds(15) // polling a cada 15s
};
_availabilityTimer.Tick += (_, _) => RefreshProviderInfo();
_availabilityTimer.Start();
```

**C. Tornar RefreshProviderInfo não-bloqueante**

O problema atual: `IsAvailable` do OllamaProvider faz HTTP blocking na UI thread quando chamado de `RefreshProviderInfo()`.

Solução: mover a verificação para Task.Run:
```csharp
private async void RefreshProviderInfoAsync()
{
    var providerName = _assistant.ActiveProvider.ProviderName;
    // Executar IsAvailable fora da UI thread
    var available = await Task.Run(() => _assistant.ActiveProvider.IsAvailable);

    // Voltar para UI thread para atualizar propriedades
    IsProviderAvailable = available;
    ProviderInfo = available
        ? $"{providerName} • Online"
        : $"{providerName} • Indisponível";
    StatusText = available ? "Pronto" : $"Provider indisponível. Verifique se {providerName} está em execução.";
}
```

E substituir todas as chamadas de `RefreshProviderInfo()` por `RefreshProviderInfoAsync()`.

**D. Dispose do timer**

Implementar `IDisposable` em `AssistantPanelViewModel` para parar o timer:
```csharp
public void Dispose()
{
    _availabilityTimer?.Stop();
    _cts?.Cancel();
    _cts?.Dispose();
}
```

E registrar no App.xaml.cs o dispose (o ServiceProvider já chama Dispose em singletons disposable no OnExit).

### Arquivos modificados
- `ViewModels/AssistantPanelViewModel.cs` — DispatcherTimer, RefreshProviderInfoAsync (async), IDisposable

---

## Arquivos que mudam por passo

| Arquivo | Passo 3 | Passo 4 | Passo 5 |
|---|---|---|---|
| `Services/AssistantService.cs` | ✅ | — | — |
| `Services/IAssistantService.cs` | opcional | — | — |
| `App.xaml.cs` | ✅ | — | — |
| `ViewModels/TerminalViewModel.cs` | — | ✅ | — |
| `ViewModels/AssistantPanelViewModel.cs` | — | — | ✅ |

---

## Ordem de implementação recomendada

```
1. Passo 5 primeiro — mais isolado, só toca AssistantPanelViewModel
   Sem dependência de outras mudanças. Risco: zero (não muda interfaces).

2. Passo 4 — injetar IDatabaseService em TerminalViewModel
   Simples, isolado. Só toca TerminalViewModel.

3. Passo 3 — persistência de preferences
   Toca mais arquivos (AssistantService, App.xaml.cs).
   Fazer por último para não bloquear os outros.
```

---

## Riscos e pontos de atenção

### Passo 3
- **Cast de `IAssistantService` para `AssistantService` concreto** no App.xaml.cs é frágil. Preferir expor `RestorePreferencesAsync()` na interface.
- **Inicialização assíncrona no construtor** não é suportada em C#. Usar método separado chamado externamente, como o padrão já usado no projeto (`InitializeAsync` em outros serviços).

### Passo 4
- **`SendKeyDataAsync` NÃO deve registrar histórico** — recebe bytes crus (setas, backspace, VT100). Só registrar em `ExecuteCommandAsync` e `SendInput`.
- **Comandos muito curtos ou espaços** — filtrar com `IsNullOrWhiteSpace` antes de gravar.
- **Session pode ser null** — verificar `Session?.Id` antes de chamar AddCommandHistoryAsync.

### Passo 5
- **`IsAvailable` do OllamaProvider é bloqueante** (GetAwaiter().GetResult()). **Obrigatório** usar `Task.Run` para não travar a UI thread no timer.
- **Frequência do polling** — 15 segundos é razoável. Muito mais rápido pode sobrecarregar o Ollama.
- **RefreshProviderInfo chamado em ExecuteWithProviderGuardAsync** — após tornar a versão async, garantir que `ExecuteWithProviderGuardAsync` aguarda o refresh antes de verificar `IsProviderAvailable`.

---

## Validação após implementação

1. **Passo 3**: Trocar provider para OpenAI, reiniciar o app, abrir painel IA → deve mostrar OpenAI selecionado.
2. **Passo 4**: Executar `ls -la` num terminal, verificar no DB com `sqlite3 devworkspace.db "SELECT * FROM CommandHistory LIMIT 5;"`.
3. **Passo 5**: Iniciar o app com Ollama offline → badge mostra "Indisponível". Subir o Ollama → em até 15s o badge muda para "Online".
