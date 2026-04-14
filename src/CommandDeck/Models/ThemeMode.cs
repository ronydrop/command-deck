using System.Text.Json.Serialization;

namespace CommandDeck.Models;

/// <summary>
/// Controls which variant of themes are available and how the system mode is resolved.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThemeMode
{
    /// <summary>Use a dark theme from the dark palette.</summary>
    Dark,

    /// <summary>Use a light theme from the light palette.</summary>
    Light,

    /// <summary>Follow the OS appearance setting (Windows AppsUseLightTheme).</summary>
    System
}
