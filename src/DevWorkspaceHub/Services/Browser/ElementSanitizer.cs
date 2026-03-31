using System.Text.RegularExpressions;
using DevWorkspaceHub.Models.Browser;

namespace DevWorkspaceHub.Services.Browser;

public static partial class ElementSanitizer
{
    public static ElementCaptureData Sanitize(ElementCaptureData data)
    {
        if (data.Attributes.TryGetValue("type", out var type) &&
            type.Equals("password", StringComparison.OrdinalIgnoreCase))
        {
            data.Attributes.Remove("value");
        }

        RedactSecrets(data.Attributes);

        if (data.OuterHtml != null)
        {
            data.OuterHtml = StripScriptTags(data.OuterHtml);
            data.OuterHtml = RedactTokensInText(data.OuterHtml);
            if (data.OuterHtml.Length > 50_000)
                data.OuterHtml = data.OuterHtml[..50_000] + "\n<!-- truncated -->";
        }

        if (data.InnerHtml != null)
        {
            data.InnerHtml = StripScriptTags(data.InnerHtml);
            data.InnerHtml = RedactTokensInText(data.InnerHtml);
            if (data.InnerHtml.Length > 30_000)
                data.InnerHtml = data.InnerHtml[..30_000] + "\n<!-- truncated -->";
        }

        if (data.TextContent != null && data.TextContent.Length > 2000)
            data.TextContent = data.TextContent[..2000];

        return data;
    }

    private static void RedactSecrets(Dictionary<string, string> attrs)
    {
        var sensitiveKeys = new[] { "data-token", "data-secret", "data-api-key", "authorization" };
        foreach (var key in sensitiveKeys)
        {
            if (attrs.ContainsKey(key))
                attrs[key] = "[REDACTED]";
        }

        foreach (var (k, v) in attrs.ToList())
        {
            if (JwtPattern().IsMatch(v) || ApiKeyPattern().IsMatch(v))
                attrs[k] = "[REDACTED]";
        }
    }

    private static string RedactTokensInText(string text)
    {
        text = JwtPattern().Replace(text, "[REDACTED_JWT]");
        text = ApiKeyPattern().Replace(text, "[REDACTED_KEY]");
        return text;
    }

    private static string StripScriptTags(string html) =>
        ScriptTagPattern().Replace(html, "");

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}", RegexOptions.Compiled)]
    private static partial Regex JwtPattern();

    [GeneratedRegex(@"(sk|pk|api|key|token|secret)[_-][A-Za-z0-9]{16,}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ApiKeyPattern();

    [GeneratedRegex(@"<script\b[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ScriptTagPattern();
}
