using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CommandDeck.Models;

/// <summary>
/// A dynamic field inside a <see cref="PromptTemplate"/> that the user fills in
/// before sending the prompt to the AI.
/// </summary>
public class PromptTemplateField
{
    /// <summary>Machine key used as <c>{{key}}</c> placeholder in the template text.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Human-readable label shown next to the input.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Placeholder text shown inside the input field.</summary>
    public string Placeholder { get; init; } = string.Empty;

    /// <summary>Default value pre-filled in the input.</summary>
    public string DefaultValue { get; init; } = string.Empty;

    /// <summary>Whether this field is required (blocks sending if empty).</summary>
    public bool IsRequired { get; init; } = true;
}

/// <summary>
/// A reusable AI prompt template with optional dynamic fields.
/// The user fills in the fields, the placeholders are replaced, and the result is sent to chat.
/// </summary>
public class PromptTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Display name in the template picker.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Short description of what this template does.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Emoji icon for quick visual identification.</summary>
    public string Icon { get; set; } = "📝";

    /// <summary>Accent color hex (Catppuccin Mocha palette).</summary>
    public string AccentColor { get; set; } = "#cba6f7";

    /// <summary>Category tag for grouping (e.g. "Código", "Revisão", "Git").</summary>
    public string Category { get; set; } = "Geral";

    /// <summary>
    /// The template text. Use <c>{{fieldKey}}</c> as placeholders for dynamic fields.
    /// Example: "Revise este código {{language}}:\n\n```\n{{code}}\n```"
    /// </summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>Dynamic fields that the user fills before sending.</summary>
    public List<PromptTemplateField> Fields { get; set; } = new();

    /// <summary>Whether this is a built-in (non-deletable) template.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Whether to send the prompt immediately (true) or just inject it into the input box.</summary>
    public bool AutoSend { get; set; }

    /// <summary>Replaces all <c>{{key}}</c> placeholders with the given values.</summary>
    public string Render(Dictionary<string, string> fieldValues)
    {
        var result = Template;
        foreach (var (key, value) in fieldValues)
            result = result.Replace($"{{{{{key}}}}}", value, StringComparison.OrdinalIgnoreCase);
        return result;
    }
}

/// <summary>
/// An Agent Mode that applies a specific system prompt and behavior to a chat tile.
/// </summary>
public class AgentMode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Display name (e.g., "Code Review", "Debug Assistant").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Short description of the mode's purpose.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Emoji icon for the mode selector.</summary>
    public string Icon { get; set; } = "🤖";

    /// <summary>Accent color hex.</summary>
    public string AccentColor { get; set; } = "#cba6f7";

    /// <summary>The system prompt injected when this mode is active.</summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>Whether this is a built-in (non-deletable) mode.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Suggested temperature override (-1 = use provider default).</summary>
    [JsonIgnore]
    public double Temperature { get; set; } = -1;

    /// <summary>Greeting message shown when mode is activated.</summary>
    public string WelcomeMessage { get; set; } = string.Empty;
}
