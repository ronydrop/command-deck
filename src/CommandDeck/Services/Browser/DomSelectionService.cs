using System.IO;
using System.Reflection;
using System.Text.Json;
using CommandDeck.Models.Browser;

namespace CommandDeck.Services.Browser;

public class DomSelectionService : IDomSelectionService
{
    private IBrowserRuntimeService? _browserRuntime;
    private string? _pickerScript;
    private bool _isPickerActive;

    public bool IsPickerActive => _isPickerActive;

    public event Action<ElementCaptureData>? ElementSelected;
    public event Action? PickerActivated;
    public event Action? PickerDeactivated;
    public event Action? PickerCancelled;

    public void Initialize(IBrowserRuntimeService browserRuntime)
    {
        _browserRuntime = browserRuntime;

        if (browserRuntime is BrowserRuntimeService brs)
        {
            brs.WebMessageReceived += OnWebMessageReceived;
            brs.StateChanged += state =>
            {
                if (state == BrowserSessionState.Loading && _isPickerActive)
                {
                    _isPickerActive = false;
                    PickerDeactivated?.Invoke();
                }
            };
        }
    }

    public async Task ActivatePickerAsync()
    {
        if (_browserRuntime == null || !_browserRuntime.IsInitialized || _isPickerActive)
            return;

        var script = await GetPickerScriptAsync();
        await _browserRuntime.ExecuteScriptAsync(script);
        await _browserRuntime.ExecuteScriptAsync("EPActivate()");

        _isPickerActive = true;
        PickerActivated?.Invoke();
    }

    public async Task DeactivatePickerAsync()
    {
        if (_browserRuntime == null || !_isPickerActive)
            return;

        await _browserRuntime.ExecuteScriptAsync("if(window.EPDeactivate)EPDeactivate()");
        _isPickerActive = false;
        PickerDeactivated?.Invoke();
    }

    public async Task TogglePickerAsync()
    {
        if (_isPickerActive)
            await DeactivatePickerAsync();
        else
            await ActivatePickerAsync();
    }

    private void OnWebMessageReceived(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
                return;

            var type = typeProp.GetString();
            switch (type)
            {
                case "elementSelected":
                    if (root.TryGetProperty("data", out var dataEl))
                    {
                        var rawJson = dataEl.GetRawText();
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };
                        var data = JsonSerializer.Deserialize<ElementCaptureData>(rawJson, options);
                        if (data != null)
                        {
                            ElementSanitizer.Sanitize(data);
                            _isPickerActive = false;
                            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                            {
                                ElementSelected?.Invoke(data);
                                PickerDeactivated?.Invoke();
                            });
                        }
                    }
                    break;

                case "pickerCancelled":
                    _isPickerActive = false;
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        PickerCancelled?.Invoke();
                        PickerDeactivated?.Invoke();
                    });
                    break;

                case "pickerActivated":
                    break;

                case "pickerDeactivated":
                    _isPickerActive = false;
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        PickerDeactivated?.Invoke();
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DomSelectionService] Error parsing message: {ex.Message}");
        }
    }

    private async Task<string> GetPickerScriptAsync()
    {
        if (_pickerScript != null) return _pickerScript;

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "CommandDeck.Resources.Scripts.element-picker.js";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException($"Embedded resource '{resourceName}' not found");

        using var reader = new StreamReader(stream);
        _pickerScript = await reader.ReadToEndAsync();
        return _pickerScript;
    }
}
