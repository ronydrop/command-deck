using System.Text;
using System.Text.RegularExpressions;
using DevWorkspaceHub.Models.Browser;

namespace DevWorkspaceHub.Services.Browser;

public static partial class ElementContextFormatter
{
    private const int MaxHtmlLength = 2000;
    private const int MaxTotalBytes = 8 * 1024;

    public static string FormatForTerminal(ElementCaptureData data, string? intent = null)
    {
        var sb = new StringBuilder();

        var header = string.IsNullOrEmpty(intent)
            ? "== ELEMENT CONTEXT =="
            : $"== ELEMENT CONTEXT [{intent}] ==";

        sb.AppendLine(header);
        sb.AppendLine();

        sb.AppendLine($"Tag: {data.TagName}");

        if (!string.IsNullOrEmpty(data.Id))
            sb.AppendLine($"ID: {data.Id}");

        if (!string.IsNullOrEmpty(data.ClassName))
            sb.AppendLine($"Classes: {data.ClassName}");

        sb.AppendLine($"Selector: {data.CssSelector}");

        if (!string.IsNullOrEmpty(data.Url))
            sb.AppendLine($"Page URL: {data.Url}");

        if (!string.IsNullOrEmpty(data.OuterHtml))
        {
            var html = data.OuterHtml.Length > MaxHtmlLength
                ? data.OuterHtml[..MaxHtmlLength] + "... [truncated]"
                : data.OuterHtml;
            sb.AppendLine();
            sb.AppendLine("HTML:");
            sb.AppendLine(html);
        }

        if (data.FrameworkInfo is { } fw)
        {
            sb.AppendLine();
            if (!string.IsNullOrEmpty(fw.Framework))
                sb.AppendLine($"Framework: {fw.Framework}");
            if (!string.IsNullOrEmpty(fw.ComponentName))
                sb.AppendLine($"Component: {fw.ComponentName}");
            if (fw.ComponentStack is { Count: > 0 })
                sb.AppendLine($"Component Stack: {string.Join(" > ", fw.ComponentStack)}");
        }

        if (!string.IsNullOrEmpty(intent))
        {
            sb.AppendLine();
            sb.AppendLine($"Intent: {intent}");
        }

        sb.AppendLine();
        sb.AppendLine("== END ELEMENT CONTEXT ==");

        var result = SanitizeForTerminal(sb.ToString());

        if (Encoding.UTF8.GetByteCount(result) > MaxTotalBytes)
        {
            var bytes = Encoding.UTF8.GetBytes(result);
            result = Encoding.UTF8.GetString(bytes, 0, MaxTotalBytes);
            var lastNewline = result.LastIndexOf('\n');
            if (lastNewline > 0)
                result = result[..lastNewline];
            result += "\n... [truncated to 8KB]\n== END ELEMENT CONTEXT ==\n";
        }

        return result;
    }

    public static string FormatForAssistant(ElementCaptureData data)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"🔍 **Selected Element: `<{data.TagName}>`**");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(data.Id))
            sb.AppendLine($"- **ID:** `{data.Id}`");

        if (!string.IsNullOrEmpty(data.ClassName))
            sb.AppendLine($"- **Classes:** `{data.ClassName}`");

        sb.AppendLine($"- **Selector:** `{data.CssSelector}`");

        if (!string.IsNullOrEmpty(data.Url))
            sb.AppendLine($"- **Page URL:** {data.Url}");

        if (!string.IsNullOrEmpty(data.XPath))
            sb.AppendLine($"- **XPath:** `{data.XPath}`");

        if (data.Accessibility is { } a11y)
        {
            if (!string.IsNullOrEmpty(a11y.Role))
                sb.AppendLine($"- **Role:** `{a11y.Role}`");
            if (!string.IsNullOrEmpty(a11y.AriaLabel))
                sb.AppendLine($"- **Aria Label:** `{a11y.AriaLabel}`");
        }

        if (!string.IsNullOrEmpty(data.InnerText))
        {
            var text = data.InnerText.Length > 200
                ? data.InnerText[..200] + "..."
                : data.InnerText;
            sb.AppendLine($"- **Text:** {text}");
        }

        if (!string.IsNullOrEmpty(data.OuterHtml))
        {
            sb.AppendLine();
            sb.AppendLine("**HTML:**");
            sb.AppendLine("```html");
            sb.AppendLine(data.OuterHtml.Length > MaxHtmlLength
                ? data.OuterHtml[..MaxHtmlLength] + "\n<!-- truncated -->"
                : data.OuterHtml);
            sb.AppendLine("```");
        }

        if (data.FrameworkInfo is { } fw)
        {
            sb.AppendLine();
            sb.AppendLine("**Framework Info:**");
            if (!string.IsNullOrEmpty(fw.Framework))
                sb.AppendLine($"- Framework: `{fw.Framework}`");
            if (!string.IsNullOrEmpty(fw.ComponentName))
                sb.AppendLine($"- Component: `{fw.ComponentName}`");
            if (fw.ComponentStack is { Count: > 0 })
                sb.AppendLine($"- Stack: `{string.Join(" > ", fw.ComponentStack)}`");
            if (fw.TestIds.Count > 0)
            {
                sb.AppendLine("- Test IDs:");
                foreach (var (key, value) in fw.TestIds)
                    sb.AppendLine($"  - `{key}`: `{value}`");
            }
        }

        if (data.Attributes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Attributes:**");
            foreach (var (key, value) in data.Attributes)
                sb.AppendLine($"- `{key}` = `{value}`");
        }

        return sb.ToString();
    }

    private static string SanitizeForTerminal(string text)
    {
        // Strip ANSI escape sequences
        text = AnsiEscapeRegex().Replace(text, string.Empty);

        // Remove control characters except \n (0x0A) and \t (0x09)
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsControl(c) && c != '\n' && c != '\t')
                continue;
            sb.Append(c);
        }

        // Normalize line endings
        return sb.ToString().Replace("\r\n", "\n").Replace("\r", "\n");
    }

    [GeneratedRegex(@"\x1b\[[0-9;]*[a-zA-Z]|\x1b\][^\x07]*\x07|\x1b[^[\]].?")]
    private static partial Regex AnsiEscapeRegex();
}
