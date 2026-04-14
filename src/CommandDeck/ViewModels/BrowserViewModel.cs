using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandDeck.Helpers;
using CommandDeck.Models;
using CommandDeck.Models.Browser;
using CommandDeck.Services;
using CommandDeck.Services.Browser;
using Microsoft.Web.WebView2.Wpf;

namespace CommandDeck.ViewModels;

public partial class BrowserViewModel : ObservableObject, IDisposable
{
    private readonly IBrowserRuntimeService _browserRuntime;
    private readonly ILocalAppSessionService _localAppSession;
    private readonly IProjectService _projectService;
    private readonly INotificationService _notificationService;
    private readonly IDomSelectionService _domSelection;
    private readonly IAiContextRouter _contextRouter;
    private readonly ChatTileRouter _chatTileRouter;

    // ─── Per-project browser session state ────────────────────────────────
    private readonly Dictionary<string, ProjectBrowserState> _projectSessions = new();
    private string? _currentProjectId;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _addressBarText = string.Empty;

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isInitialized;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    private BrowserSessionState _sessionState = BrowserSessionState.Disconnected;

    [ObservableProperty]
    private string _statusText = "Desconectado";

    [ObservableProperty]
    private int _detectedPort;

    [ObservableProperty]
    private bool _isServerRunning;

    // ─── Element Picker State ─────────────────────────────────────────────

    [ObservableProperty]
    private bool _isPickerActive;

    [ObservableProperty]
    private bool _hasSelectedElement;

    [ObservableProperty]
    private ElementCaptureData? _selectedElement;

    [ObservableProperty]
    private string _selectedElementSummary = string.Empty;

    [ObservableProperty]
    private string _selectedElementHtml = string.Empty;

    [ObservableProperty]
    private string _selectedElementSelector = string.Empty;

    [ObservableProperty]
    private bool _isContextPanelOpen;

    [ObservableProperty]
    private bool _isAgentMenuOpen;

    public ObservableCollection<AgentTargetInfo> AvailableAgents { get; } = new();

    public BrowserViewModel(
        IBrowserRuntimeService browserRuntime,
        ILocalAppSessionService localAppSession,
        IProjectService projectService,
        INotificationService notificationService,
        IDomSelectionService domSelection,
        IAiContextRouter contextRouter,
        ChatTileRouter chatTileRouter)
    {
        _browserRuntime = browserRuntime;
        _localAppSession = localAppSession;
        _projectService = projectService;
        _notificationService = notificationService;
        _domSelection = domSelection;
        _contextRouter = contextRouter;
        _chatTileRouter = chatTileRouter;

        var runtime = (BrowserRuntimeService)_browserRuntime;
        runtime.StateChanged += OnStateChanged;
        runtime.TitleChanged += OnTitleChanged;
        runtime.UrlChanged += OnRuntimeUrlChanged;

        _domSelection.ElementSelected += OnElementSelected;
        _domSelection.PickerActivated += OnPickerActivated;
        _domSelection.PickerDeactivated += OnPickerDeactivated;
        _domSelection.PickerCancelled += OnPickerCancelled;
    }

    public void SetWebView(WebView2 webView)
    {
        ((BrowserRuntimeService)_browserRuntime).SetWebView(webView);
    }

    [ObservableProperty]
    private bool _isRuntimeMissing;

    [ObservableProperty]
    private string _runtimeErrorMessage = string.Empty;

    public async Task InitializeAsync()
    {
        if (IsInitialized) return;

        var availability = WebView2RuntimeChecker.Check();
        if (availability != WebView2Availability.Available)
        {
            IsRuntimeMissing = true;
            RuntimeErrorMessage = availability switch
            {
                WebView2Availability.NotInstalled => "WebView2 Runtime não encontrado. Instale o Microsoft Edge WebView2 Runtime.",
                WebView2Availability.OutdatedVersion => "WebView2 Runtime desatualizado. Atualize o Microsoft Edge WebView2 Runtime.",
                _ => "Erro ao verificar WebView2 Runtime."
            };
            StatusText = RuntimeErrorMessage;
            _notificationService.Notify(
                "WebView2 Runtime indisponível",
                NotificationType.Warning,
                NotificationSource.System,
                message: RuntimeErrorMessage);
            return;
        }

        try
        {
            var runtime = (BrowserRuntimeService)_browserRuntime;
            runtime.ProcessFailed += kind =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    StatusText = kind switch
                    {
                        Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedKind.BrowserProcessExited
                            => "Browser crashed — reinicie a aplicação",
                        Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedKind.RenderProcessExited
                            => "Renderer crashed — recarregue a página",
                        Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedKind.RenderProcessUnresponsive
                            => "Renderer não responde — recarregue a página",
                        _ => "Erro no processo do browser"
                    };
                    _notificationService.Notify(
                        "Erro no browser",
                        NotificationType.Error,
                        NotificationSource.System,
                        message: StatusText);
                });
            };

            await _browserRuntime.InitializeAsync(0);
            IsInitialized = true;

            _domSelection.Initialize(_browserRuntime);

            var projects = await _projectService.GetAllProjectsAsync();
            var currentProject = _currentProjectId != null
                ? projects?.FirstOrDefault(p => p.Id == _currentProjectId)
                : null;
            currentProject ??= projects?.FirstOrDefault(p => p.IsFavorite)
                               ?? projects?.FirstOrDefault();

            if (currentProject != null)
            {
                _currentProjectId = currentProject.Id;
                await DetectAndNavigateAsync(currentProject.Path, currentProject.ProjectType.ToString());
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Erro ao inicializar browser: {ex.Message}";
            _notificationService.Notify(
                "Erro ao inicializar browser",
                NotificationType.Error,
                NotificationSource.System,
                message: ex.Message);
        }
    }

    public async Task DetectAndNavigateAsync(string projectPath, string? projectType = null)
    {
        var port = await _localAppSession.DetectPortAsync(projectPath, projectType);
        if (port.HasValue)
        {
            DetectedPort = port.Value;
            IsServerRunning = true;
            var url = _localAppSession.GetLocalUrl(port.Value);
            AddressBarText = url;
            await NavigateToUrlAsync(url);
        }
        else
        {
            IsServerRunning = false;
            StatusText = "Nenhum servidor local detectado";
        }
    }

    // ─── Navigation Commands ──────────────────────────────────────────────

    [RelayCommand]
    private async Task NavigateToUrl(string? url = null)
    {
        var target = url ?? AddressBarText;
        if (string.IsNullOrWhiteSpace(target)) return;
        await NavigateToUrlAsync(target);
    }

    private async Task NavigateToUrlAsync(string url)
    {
        if (!IsInitialized) return;
        await _browserRuntime.NavigateAsync(url);
    }

    [RelayCommand]
    private async Task GoBack() => await _browserRuntime.GoBackAsync();

    [RelayCommand]
    private async Task GoForward() => await _browserRuntime.GoForwardAsync();

    [RelayCommand]
    private async Task Reload() => await _browserRuntime.ReloadAsync();

    [RelayCommand]
    private void AddressBarSubmit() => _ = NavigateToUrl();

    [RelayCommand]
    private async Task DetectServer()
    {
        var projects = await _projectService.GetAllProjectsAsync();
        var currentProject = _currentProjectId != null
            ? projects?.FirstOrDefault(p => p.Id == _currentProjectId)
            : null;
        currentProject ??= projects?.FirstOrDefault(p => p.IsFavorite)
                           ?? projects?.FirstOrDefault();
        if (currentProject != null)
            await DetectAndNavigateAsync(currentProject.Path, currentProject.ProjectType.ToString());
    }

    // ─── Element Picker Commands ──────────────────────────────────────────

    [RelayCommand]
    private async Task TogglePicker()
    {
        await _domSelection.TogglePickerAsync();
        if (IsPickerActive)
            StatusText = "🔍 Selecione um elemento...";
    }

    [RelayCommand]
    private void DismissContext()
    {
        IsContextPanelOpen = false;
    }

    [RelayCommand]
    private void CopySelector()
    {
        if (!string.IsNullOrEmpty(SelectedElementSelector))
        {
            System.Windows.Clipboard.SetText(SelectedElementSelector);
            _notificationService.Notify(
                "Seletor copiado",
                NotificationType.Success,
                NotificationSource.System);
        }
    }

    [RelayCommand]
    private void CopyHtml()
    {
        if (!string.IsNullOrEmpty(SelectedElementHtml))
        {
            System.Windows.Clipboard.SetText(SelectedElementHtml);
            _notificationService.Notify(
                "HTML copiado",
                NotificationType.Success,
                NotificationSource.System);
        }
    }

    private void OnElementSelected(ElementCaptureData data)
    {
        SelectedElement = data;
        HasSelectedElement = true;
        IsContextPanelOpen = true;

        var tag = data.TagName;
        var id = !string.IsNullOrEmpty(data.Id) ? $"#{data.Id}" : "";
        var cls = !string.IsNullOrEmpty(data.ClassName)
            ? "." + string.Join(".", data.ClassName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3))
            : "";
        SelectedElementSummary = $"<{tag}>{id}{cls}";
        SelectedElementSelector = data.CssSelector;
        SelectedElementHtml = data.OuterHtml ?? "";

        StatusText = $"Elemento capturado: {SelectedElementSummary}";

        _notificationService.Notify(
            $"Elemento selecionado: {SelectedElementSummary}",
            NotificationType.Success,
            NotificationSource.System);
    }

    // ─── Send to Agent Commands ────────────────────────────────────────

    [RelayCommand]
    private void ShowAgentMenu()
    {
        if (!HasSelectedElement) return;

        AvailableAgents.Clear();
        foreach (var target in _contextRouter.GetAvailableTargets())
            AvailableAgents.Add(target);

        IsAgentMenuOpen = true;
    }

    [RelayCommand]
    private async Task SendToAgent(AgentTargetInfo? target)
    {
        if (target == null || SelectedElement == null) return;

        IsAgentMenuOpen = false;
        var context = FormatContextForAgent();

        if (target.Type == AgentTargetType.Assistant)
        {
            _ = _chatTileRouter.RouteMessageAsync($"[🔍 Elemento capturado do browser]\n\n{context}\n\nAnalise este elemento e identifique possíveis melhorias.", autoSend: false);
            _notificationService.Notify(
                "Contexto enviado para Assistant AI",
                NotificationType.Success,
                NotificationSource.System);
        }
        else if (target.SessionId != null)
        {
            await _contextRouter.SendToTerminalAsync(target.SessionId, context + "\n");
            _notificationService.Notify(
                $"Contexto enviado para {target.DisplayName}",
                NotificationType.Success,
                NotificationSource.System);
        }
    }

    [RelayCommand]
    private async Task SendToAssistant()
    {
        if (SelectedElement == null) return;
        var context = FormatContextForAgent();
        await _chatTileRouter.RouteMessageAsync($"[🔍 Elemento capturado do browser]\n\n{context}\n\nAnalise este elemento e identifique possíveis melhorias.", autoSend: false);
        _notificationService.Notify(
            "Contexto enviado para Chat AI",
            NotificationType.Success,
            NotificationSource.System);
    }

    public string FormatContextForAgent()
    {
        if (SelectedElement == null) return string.Empty;
        var el = SelectedElement;
        var fw = el.FrameworkInfo;
        var fwLine = fw?.Framework != null
            ? $"\nFramework: {fw.Framework} | Componente: {fw.ComponentName ?? "?"}"
            : "";

        return $"""
═══ ELEMENTO SELECIONADO ═══
Tag: <{el.TagName}>{(!string.IsNullOrEmpty(el.Id) ? $" #{el.Id}" : "")}{(!string.IsNullOrEmpty(el.ClassName) ? $" .{el.ClassName.Replace(" ", ".")}" : "")}
Selector: {el.CssSelector}
XPath: {el.XPath}
Texto: "{el.TextContent?.Substring(0, Math.Min(200, el.TextContent?.Length ?? 0))}"

Atributos: {string.Join(", ", el.Attributes.Select(a => $"{a.Key}=\"{a.Value}\""))}

Hierarquia: {string.Join(" > ", el.Ancestors.Take(5).Reverse().Select(a => a.TagName + (!string.IsNullOrEmpty(a.Id) ? $"#{a.Id}" : "")))} > {el.TagName}
Filhos: {el.ChildrenSummary?.Count ?? 0}

HTML:
{el.OuterHtml}{fwLine}

URL: {el.Url}
═══════════════════════════
""";
    }

    private void OnTitleChanged(string title) => PageTitle = title;

    private void OnRuntimeUrlChanged(string url)
    {
        Url = url;
        AddressBarText = url;
        CanGoBack = _browserRuntime.CanGoBack;
        CanGoForward = _browserRuntime.CanGoForward;
    }

    private void OnPickerActivated() => IsPickerActive = true;

    private void OnPickerDeactivated() => IsPickerActive = false;

    private void OnPickerCancelled()
    {
        IsPickerActive = false;
        StatusText = IsServerRunning ? $"Conectado — localhost:{DetectedPort}" : "Desconectado";
    }

    private void OnStateChanged(BrowserSessionState state)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            SessionState = state;
            IsLoading = state == BrowserSessionState.Loading;
            if (!IsPickerActive)
            {
                StatusText = state switch
                {
                    BrowserSessionState.Disconnected => "Desconectado",
                    BrowserSessionState.Connecting => "Conectando...",
                    BrowserSessionState.Connected => $"Conectado — localhost:{DetectedPort}",
                    BrowserSessionState.Loading => "Carregando...",
                    BrowserSessionState.Error => "Erro de conexão",
                    _ => "Desconhecido"
                };
            }
            CanGoBack = _browserRuntime.CanGoBack;
            CanGoForward = _browserRuntime.CanGoForward;
        });
    }

    // ─── Per-project session management ─────────────────────────────────

    /// <summary>
    /// Switches the browser context to the given project.
    /// Saves the current project's browser state and restores the target project's state.
    /// </summary>
    public async Task SwitchToProjectAsync(Project project)
    {
        // Save current project state
        if (_currentProjectId != null)
        {
            _projectSessions[_currentProjectId] = new ProjectBrowserState
            {
                Url = Url,
                AddressBarText = AddressBarText,
                DetectedPort = DetectedPort,
                IsServerRunning = IsServerRunning,
                PageTitle = PageTitle
            };
        }

        _currentProjectId = project.Id;

        // Restore saved state or detect fresh
        if (_projectSessions.TryGetValue(project.Id, out var saved))
        {
            DetectedPort = saved.DetectedPort;
            IsServerRunning = saved.IsServerRunning;
            AddressBarText = saved.AddressBarText;
            PageTitle = saved.PageTitle;

            if (IsInitialized && !string.IsNullOrEmpty(saved.Url))
                await NavigateToUrlAsync(saved.Url);
        }
        else
        {
            // Reset state for a project with no previous session
            DetectedPort = 0;
            IsServerRunning = false;
            AddressBarText = string.Empty;
            Url = string.Empty;
            PageTitle = string.Empty;
            StatusText = "Desconectado";

            if (IsInitialized)
                await DetectAndNavigateAsync(project.Path, project.ProjectType.ToString());
        }
    }

    public void Dispose()
    {
        var runtime = (BrowserRuntimeService)_browserRuntime;
        runtime.StateChanged -= OnStateChanged;
        runtime.TitleChanged -= OnTitleChanged;
        runtime.UrlChanged -= OnRuntimeUrlChanged;

        _domSelection.ElementSelected -= OnElementSelected;
        _domSelection.PickerActivated -= OnPickerActivated;
        _domSelection.PickerDeactivated -= OnPickerDeactivated;
        _domSelection.PickerCancelled -= OnPickerCancelled;
    }
}

/// <summary>
/// Snapshot of browser state for a specific project, used to restore on project switch.
/// </summary>
internal sealed class ProjectBrowserState
{
    public string Url { get; set; } = string.Empty;
    public string AddressBarText { get; set; } = string.Empty;
    public int DetectedPort { get; set; }
    public bool IsServerRunning { get; set; }
    public string PageTitle { get; set; } = string.Empty;
}
