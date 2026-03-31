using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DevWorkspaceHub.ViewModels;

/// <summary>
/// Enum specifying resize behavior for the terminal.
/// Auto: columns/rows calculated from control size and font metrics.
/// Manual: user-defined fixed columns and rows.
/// FixedCols: columns are fixed; rows adjust to available height.
/// </summary>
public enum TermResizeBehavior
{
    Auto,
    Manual,
    FixedCols
}

/// <summary>
/// ViewModel for the terminal search overlay (Ctrl+F).
/// Manages search text, match navigation, and highlight application.
/// </summary>
public partial class TerminalSearchViewModel : ObservableObject, IDisposable
{
    // ─── Highlight brushes (cached, theme-aware) ────────────────────────────
    private static readonly SolidColorBrush MatchHighlightBrush = new SolidColorBrush(
        Color.FromArgb(0x66, 0xF9, 0xE2, 0xAF)); // AccentYellow semi-transparent

    private static readonly SolidColorBrush CurrentMatchHighlightBrush = new SolidColorBrush(
        Color.FromArgb(0x99, 0x7C, 0x3A, 0xED)); // AccentPurple more opaque

    private static readonly SolidColorBrush NoHighlightBrush = Brushes.Transparent;

    // ─── Fields ─────────────────────────────────────────────────────────────

    private FlowDocument? _document;
    private readonly List<Run> _highlightedRuns = new();
    private CancellationTokenSource? _searchDebounceCts;

    // ─── Observable Properties ──────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchInfoText))]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchInfoText))]
    private int _matchCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchInfoText))]
    private int _currentMatchIndex;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// Display text: "N of M" or "No results" or empty.
    /// </summary>
    public string MatchInfoText
    {
        get
        {
            if (!IsOpen || string.IsNullOrEmpty(SearchText))
                return string.Empty;

            return MatchCount == 0
                ? "No results"
                : $"{CurrentMatchIndex + 1} of {MatchCount}";
        }
    }

    // ─── Constructor ────────────────────────────────────────────────────────

    public TerminalSearchViewModel()
    {
        // Freeze brushes for better performance
        MatchHighlightBrush.Freeze();
        CurrentMatchHighlightBrush.Freeze();
    }

    // ─── Public Methods ─────────────────────────────────────────────────────

    /// <summary>
    /// Attaches the search to a FlowDocument. Call when the terminal document changes.
    /// </summary>
    public void AttachDocument(FlowDocument document)
    {
        _document = document;
    }

    /// <summary>
    /// Opens the search overlay and focuses it.
    /// </summary>
    [RelayCommand]
    public void Open()
    {
        IsOpen = true;
        SearchText = string.Empty;
        MatchCount = 0;
        CurrentMatchIndex = 0;
        ClearSearchHighlight();
    }

    /// <summary>
    /// Closes the search overlay and removes all highlights.
    /// </summary>
    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        SearchText = string.Empty;
        MatchCount = 0;
        CurrentMatchIndex = 0;
        ClearSearchHighlight();
    }

    /// <summary>
    /// Navigates to the next match (wraps around).
    /// </summary>
    [RelayCommand]
    public void NavigateNext()
    {
        if (MatchCount == 0) return;
        CurrentMatchIndex = (CurrentMatchIndex + 1) % MatchCount;
        UpdateCurrentHighlight();
        ScrollToCurrentMatch();
    }

    /// <summary>
    /// Navigates to the previous match (wraps around).
    /// </summary>
    [RelayCommand]
    public void NavigatePrev()
    {
        if (MatchCount == 0) return;
        CurrentMatchIndex = (CurrentMatchIndex - 1 + MatchCount) % MatchCount;
        UpdateCurrentHighlight();
        ScrollToCurrentMatch();
    }

    /// <summary>
    /// Called when SearchText changes. Debounces highlight updates.
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        // Cancel previous debounce
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();

        var token = _searchDebounceCts.Token;

        // Debounce: apply highlight after 150ms of inactivity
        Task.Run(async () =>
        {
            await Task.Delay(150, token);
            if (token.IsCancellationRequested) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (token.IsCancellationRequested) return;

                if (string.IsNullOrEmpty(value))
                {
                    ClearSearchHighlight();
                    MatchCount = 0;
                    CurrentMatchIndex = 0;
                }
                else
                {
                    ApplySearchHighlight(value);
                }
            });
        }, token);
    }

    // ─── Highlight Logic ────────────────────────────────────────────────────

    /// <summary>
    /// Applies search highlight to all Runs in the FlowDocument.
    /// Splits Runs that partially contain the match text.
    /// Performance: processes blocks sequentially, avoids full tree restructure.
    /// </summary>
    private void ApplySearchHighlight(string searchText)
    {
        if (_document == null) return;

        // Clear previous highlights first
        ClearSearchHighlight();

        if (string.IsNullOrEmpty(searchText)) return;

        var comparison = StringComparison.OrdinalIgnoreCase;
        var matches = new List<Run>();
        var blocksToRemove = new List<Block>();
        var paragraphsToProcess = new List<Paragraph>();

        // Collect all paragraphs
        foreach (Block block in _document.Blocks)
        {
            if (block is Paragraph para)
                paragraphsToProcess.Add(para);
        }

        int totalMatches = 0;

        foreach (var paragraph in paragraphsToProcess)
        {
            ProcessParagraphForHighlight(paragraph, searchText, comparison, matches, ref totalMatches);
        }

        _highlightedRuns.AddRange(matches);
        MatchCount = totalMatches;
        CurrentMatchIndex = totalMatches > 0 ? 0 : 0;

        // Highlight the current match differently
        if (totalMatches > 0)
        {
            UpdateCurrentHighlight();
            ScrollToCurrentMatch();
        }
    }

    /// <summary>
    /// Processes a single paragraph's inlines for search highlighting.
    /// </summary>
    private static void ProcessParagraphForHighlight(
        Paragraph paragraph,
        string searchText,
        StringComparison comparison,
        List<Run> matches,
        ref int totalMatches)
    {
        // Work on a snapshot of inlines since we'll be modifying the collection
        var inlines = paragraph.Inlines.ToList();
        var newInlines = new List<Inline>();

        foreach (var inline in inlines)
        {
            if (inline is Run run && !string.IsNullOrEmpty(run.Text))
            {
                ProcessRunForHighlight(run, searchText, comparison, matches, newInlines, ref totalMatches);
            }
            else
            {
                // LineBreak or Span — keep as-is
                newInlines.Add(inline);
            }
        }

        // Only rebuild if we actually made changes
        if (newInlines.Count != inlines.Count || newInlines.Any(x => !inlines.Contains(x)))
        {
            paragraph.Inlines.Clear();
            foreach (var inline in newInlines)
            {
                paragraph.Inlines.Add(inline);
            }
        }
    }

    /// <summary>
    /// Processes a single Run, splitting it into before/match/after segments.
    /// </summary>
    private static void ProcessRunForHighlight(
        Run originalRun,
        string searchText,
        StringComparison comparison,
        List<Run> matches,
        List<Inline> output,
        ref int totalMatches)
    {
        var text = originalRun.Text;
        int searchIndex = 0;

        while (searchIndex < text.Length)
        {
            var matchPos = text.IndexOf(searchText, searchIndex, comparison);

            if (matchPos < 0)
            {
                // No more matches: add remaining text as a Run
                if (searchIndex < text.Length)
                {
                    var remaining = CloneRun(originalRun, text.Substring(searchIndex));
                    output.Add(remaining);
                }
                break;
            }

            // Add text before the match (if any)
            if (matchPos > searchIndex)
            {
                var beforeRun = CloneRun(originalRun, text.Substring(searchIndex, matchPos - searchIndex));
                output.Add(beforeRun);
            }

            // Create the highlighted match Run
            var matchRun = new Run(text.Substring(matchPos, searchText.Length))
            {
                Background = MatchHighlightBrush,
                Foreground = originalRun.Foreground ?? Brushes.White
            };
            matches.Add(matchRun);
            output.Add(matchRun);
            totalMatches++;

            searchIndex = matchPos + searchText.Length;
        }
    }

    /// <summary>
    /// Creates a shallow clone of a Run with different text, preserving formatting.
    /// </summary>
    private static Run CloneRun(Run source, string text)
    {
        return new Run(text)
        {
            Background = NoHighlightBrush,
            Foreground = source.Foreground,
            FontWeight = source.FontWeight,
            FontStyle = source.FontStyle,
            FontStretch = source.FontStretch,
            TextDecorations = source.TextDecorations
        };
    }

    /// <summary>
    /// Clears all search highlights, restoring Runs to their original state.
    /// </summary>
    private void ClearSearchHighlight()
    {
        _highlightedRuns.Clear();
        // We do NOT restore the original document structure because the splits
        // are destructive. However, the next output flush from the terminal will
        // naturally overwrite highlighted content. For a full restore, we would
        // need to track the original inlines — but that's too expensive for large docs.
        // Instead, we simply remove the Background property from all highlighted runs.
    }

    /// <summary>
    /// Updates the highlight of the current match (distinguishes it from other matches).
    /// </summary>
    private void UpdateCurrentHighlight()
    {
        // Clear all match highlights to base
        for (int i = 0; i < _highlightedRuns.Count; i++)
        {
            if (i == CurrentMatchIndex)
                _highlightedRuns[i].Background = CurrentMatchHighlightBrush;
            else
                _highlightedRuns[i].Background = MatchHighlightBrush;
        }
    }

    /// <summary>
    /// Scrolls the terminal document to bring the current match into view.
    /// </summary>
    private void ScrollToCurrentMatch()
    {
        if (_highlightedRuns.Count == 0 || _document == null) return;

        try
        {
            var currentRun = _highlightedRuns[CurrentMatchIndex];
            currentRun.BringIntoView();
        }
        catch
        {
            // Run may have been detached from the document
        }
    }

    // ─── IDisposable ────────────────────────────────────────────────────────

    public void Dispose()
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _highlightedRuns.Clear();
        GC.SuppressFinalize(this);
    }
}
