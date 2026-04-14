using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace CommandDeck.Converters;

/// <summary>Returns the list of models available for the given agent CLI name.</summary>
[ValueConversion(typeof(string), typeof(IEnumerable<string>))]
public class AgentToModelsConverter : IValueConverter
{
    private static readonly Dictionary<string, string[]> Models = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = ["sonnet", "opus", "haiku"],
        ["codex"]  = ["gpt-5-codex", "gpt-5"],
        ["aider"]  = ["sonnet", "gpt-4o"],
        ["gemini"] = ["gemini-2.5-pro", "gemini-2.5-flash"],
        ["shell"]  = [],
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string agent && Models.TryGetValue(agent, out var list) ? list : Array.Empty<string>();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
