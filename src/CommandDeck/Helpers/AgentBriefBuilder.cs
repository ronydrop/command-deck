using System.IO;
using System.Text;
using CommandDeck.Models;

namespace CommandDeck.Helpers;

/// <summary>
/// Builds a Markdown brief from a <see cref="KanbanCard"/> and writes it to
/// <c>%APPDATA%\CommandDeck\briefs\{cardId}.md</c>.
/// The brief is passed as stdin/argument to the AI agent CLI.
/// </summary>
public static class AgentBriefBuilder
{
    private static readonly string BriefsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CommandDeck", "briefs");

    /// <summary>Generates the brief markdown from a card and its sibling cards (for dep context).</summary>
    public static string Build(KanbanCard card, IReadOnlyList<KanbanCard> allCards)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Task: {card.Title}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(card.Description))
        {
            sb.AppendLine("## Description");
            sb.AppendLine(card.Description);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(card.Instructions))
        {
            sb.AppendLine("## Instructions");
            sb.AppendLine(card.Instructions);
            sb.AppendLine();
        }

        if (card.FileRefs.Count > 0)
        {
            sb.AppendLine("## Files");
            foreach (var f in card.FileRefs)
                sb.AppendLine($"- `{f}`");
            sb.AppendLine();
        }

        // Resolved dependency summaries
        var deps = allCards.Where(c => card.CardRefs.Contains(c.Id)).ToList();
        if (deps.Count > 0)
        {
            sb.AppendLine("## Completed Dependencies");
            foreach (var dep in deps)
                sb.AppendLine($"- **{dep.Title}** ({dep.ColumnId})");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"Card ID: `{card.Id}`");
        sb.AppendLine($"Agent: {card.Agent}");
        if (!string.IsNullOrEmpty(card.Model))
            sb.AppendLine($"Model: {card.Model}");

        return sb.ToString();
    }

    /// <summary>Writes the brief to disk and returns the absolute file path.</summary>
    public static async Task<string> WriteAsync(KanbanCard card, IReadOnlyList<KanbanCard> allCards)
    {
        Directory.CreateDirectory(BriefsDir);
        var filePath = Path.Combine(BriefsDir, $"{card.Id}.md");
        var content = Build(card, allCards);
        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8).ConfigureAwait(false);
        return filePath;
    }

    /// <summary>
    /// Converts a Windows absolute path to a WSL Unix path.
    /// E.g. C:\Users\foo → /mnt/c/Users/foo
    /// </summary>
    public static string ToWslPath(string windowsPath)
    {
        if (windowsPath.Length >= 2 && windowsPath[1] == ':')
        {
            var drive = char.ToLowerInvariant(windowsPath[0]);
            var rest  = windowsPath[2..].Replace('\\', '/');
            return $"/mnt/{drive}{rest}";
        }
        return windowsPath.Replace('\\', '/');
    }
}
