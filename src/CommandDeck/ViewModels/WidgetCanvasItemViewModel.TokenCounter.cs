using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommandDeck.Helpers;
using CommandDeck.Services;

namespace CommandDeck.ViewModels;

/// <summary>
/// Partial class — Token Counter widget logic for <see cref="WidgetCanvasItemViewModel"/>.
/// Subscribes to <see cref="IClaudeUsageService.UsageUpdated"/> to reflect live session stats,
/// and provides a manual text estimator with on-demand token/cost preview.
/// </summary>
public partial class WidgetCanvasItemViewModel
{
    // ─── Live session stats (from IClaudeUsageService) ───────────────────────

    [ObservableProperty] private string _displayInputTokens  = "0";
    [ObservableProperty] private string _displayOutputTokens = "0";
    [ObservableProperty] private string _displayTotalTokens  = "0";
    [ObservableProperty] private string _displayCostUsd      = "$0.00";
    [ObservableProperty] private string _displayCostBrl      = "R$0.00";
    [ObservableProperty] private string _currentModelDisplay = "—";

    // ─── Manual estimator ────────────────────────────────────────────────────

    [ObservableProperty] private string _estimatorInputText   = string.Empty;
    [ObservableProperty] private string _estimatedTokenCount  = "0";
    [ObservableProperty] private string _estimatedCostDisplay = "$0.00";

    // ─── Init / Cleanup ───────────────────────────────────────────────────────

    private void InitTokenCounter()
    {
        if (_claudeUsageService is null) return;
        _claudeUsageService.UsageUpdated += OnUsageUpdated;
        RefreshTokenCounterDisplay();
    }

    private void StopTokenCounter()
    {
        if (_claudeUsageService is not null)
            _claudeUsageService.UsageUpdated -= OnUsageUpdated;
    }

    // ─── Event handler ────────────────────────────────────────────────────────

    private void OnUsageUpdated()
    {
        // Marshal to UI thread — the event may fire from a background task.
        Application.Current?.Dispatcher.InvokeAsync(RefreshTokenCounterDisplay);
    }

    private void RefreshTokenCounterDisplay()
    {
        if (_claudeUsageService is null) return;

        DisplayInputTokens  = TokenEstimator.FormatTokens(_claudeUsageService.SessionInputTokens);
        DisplayOutputTokens = TokenEstimator.FormatTokens(_claudeUsageService.SessionOutputTokens);
        DisplayTotalTokens  = TokenEstimator.FormatTokens(_claudeUsageService.SessionTotalTokens);
        DisplayCostUsd      = TokenEstimator.FormatUsd(_claudeUsageService.SessionCostUsd);
        DisplayCostBrl      = TokenEstimator.FormatBrl(_claudeUsageService.SessionCostBrl);
        CurrentModelDisplay = _claudeUsageService.CurrentModel ?? "—";
    }

    // ─── Manual estimator ────────────────────────────────────────────────────

    /// <summary>
    /// Called by the control's code-behind (debounced) when the estimator TextBox changes.
    /// </summary>
    public void EstimateTokens(string text)
    {
        var tokens = TokenEstimator.EstimateTokenCount(text);
        EstimatedTokenCount = TokenEstimator.FormatTokens(tokens);

        var model = _claudeUsageService?.CurrentModel;
        var costUsd = TokenEstimator.EstimateCost(tokens, model, isInput: true);
        EstimatedCostDisplay = TokenEstimator.FormatUsd(costUsd);
    }
}
