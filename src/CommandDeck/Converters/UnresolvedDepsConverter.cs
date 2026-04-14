using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using CommandDeck.ViewModels;

namespace CommandDeck.Converters;

/// <summary>
/// IMultiValueConverter that receives (List&lt;string&gt; cardRefs, ObservableCollection&lt;KanbanColumnViewModel&gt; columns)
/// and returns a formatted string listing blocking card titles, or empty string if unblocked.
/// </summary>
public class UnresolvedDepsConverter : IMultiValueConverter
{
    // Column id suffixes that count as "done"
    private static readonly string[] DoneMarkers = ["done", "completo", "concluído", "finished", "complete"];

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        bool asBool = string.Equals(parameter?.ToString(), "bool", StringComparison.OrdinalIgnoreCase);
        if (values.Length < 2)
            return asBool ? (object)true : "Executar tarefa com agente AI";
        if (values[0] is not List<string> cardRefs || cardRefs.Count == 0)
            return asBool ? (object)true : "Executar tarefa com agente AI";
        if (values[1] is not ObservableCollection<KanbanColumnViewModel> columns)
            return asBool ? (object)true : "Executar tarefa com agente AI";

        // Build id→title + id→columnId maps
        var titleMap = new Dictionary<string, string>();
        var colMap   = new Dictionary<string, string>();
        foreach (var col in columns)
            foreach (var card in col.Cards)
            {
                titleMap[card.Id] = card.Title;
                colMap[card.Id]   = col.Id.ToLowerInvariant();
            }

        var blocked = new List<string>();
        foreach (var refId in cardRefs)
        {
            if (!colMap.TryGetValue(refId, out var colId)) continue;
            bool isDone = DoneMarkers.Any(m => colId.Contains(m));
            if (!isDone)
                blocked.Add(titleMap.TryGetValue(refId, out var title) ? title : refId);
        }

        if (asBool)
            return (object)(blocked.Count == 0);   // true = enabled, false = blocked

        return blocked.Count == 0
            ? "Executar tarefa com agente AI"
            : $"Bloqueado por: {string.Join(", ", blocked)}";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
