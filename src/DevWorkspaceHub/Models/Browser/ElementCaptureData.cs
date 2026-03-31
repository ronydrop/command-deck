using System.Text.Json.Serialization;

namespace DevWorkspaceHub.Models.Browser;

public class ElementCaptureData
{
    [JsonPropertyName("tagName")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("className")]
    public string? ClassName { get; set; }

    [JsonPropertyName("textContent")]
    public string? TextContent { get; set; }

    [JsonPropertyName("innerText")]
    public string? InnerText { get; set; }

    [JsonPropertyName("innerHTML")]
    public string? InnerHtml { get; set; }

    [JsonPropertyName("outerHTML")]
    public string? OuterHtml { get; set; }

    [JsonPropertyName("cssSelector")]
    public string CssSelector { get; set; } = string.Empty;

    [JsonPropertyName("xpath")]
    public string? XPath { get; set; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, string> Attributes { get; set; } = new();

    [JsonPropertyName("boundingBox")]
    public BoundingRect BoundingBox { get; set; } = new();

    [JsonPropertyName("absolutePosition")]
    public BoundingRect? AbsolutePosition { get; set; }

    [JsonPropertyName("computedStyles")]
    public Dictionary<string, string> ComputedStyles { get; set; } = new();

    [JsonPropertyName("ancestors")]
    public List<AncestorInfo> Ancestors { get; set; } = new();

    [JsonPropertyName("childrenSummary")]
    public ChildrenSummary? ChildrenSummary { get; set; }

    [JsonPropertyName("accessibility")]
    public AccessibilityInfo? Accessibility { get; set; }

    [JsonPropertyName("frameworkInfo")]
    public FrameworkInfo? FrameworkInfo { get; set; }

    [JsonPropertyName("viewport")]
    public ViewportInfo? Viewport { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}

public class BoundingRect
{
    [JsonPropertyName("x")]
    public double X { get; set; }
    [JsonPropertyName("y")]
    public double Y { get; set; }
    [JsonPropertyName("width")]
    public double Width { get; set; }
    [JsonPropertyName("height")]
    public double Height { get; set; }
}

public class AncestorInfo
{
    [JsonPropertyName("tagName")]
    public string TagName { get; set; } = string.Empty;
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    [JsonPropertyName("className")]
    public string? ClassName { get; set; }
    [JsonPropertyName("role")]
    public string? Role { get; set; }
}

public class ChildrenSummary
{
    [JsonPropertyName("count")]
    public int Count { get; set; }
    [JsonPropertyName("tags")]
    public List<ChildInfo> Tags { get; set; } = new();
    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }
}

public class ChildInfo
{
    [JsonPropertyName("tagName")]
    public string TagName { get; set; } = string.Empty;
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    [JsonPropertyName("className")]
    public string? ClassName { get; set; }
    [JsonPropertyName("textPreview")]
    public string? TextPreview { get; set; }
}

public class AccessibilityInfo
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }
    [JsonPropertyName("ariaLabel")]
    public string? AriaLabel { get; set; }
    [JsonPropertyName("tabIndex")]
    public int? TabIndex { get; set; }
    [JsonPropertyName("title")]
    public string? Title { get; set; }
    [JsonPropertyName("alt")]
    public string? Alt { get; set; }
    [JsonPropertyName("placeholder")]
    public string? Placeholder { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class FrameworkInfo
{
    [JsonPropertyName("framework")]
    public string? Framework { get; set; }
    [JsonPropertyName("componentName")]
    public string? ComponentName { get; set; }
    [JsonPropertyName("componentStack")]
    public List<string>? ComponentStack { get; set; }
    [JsonPropertyName("testIds")]
    public Dictionary<string, string> TestIds { get; set; } = new();
}

public class ViewportInfo
{
    [JsonPropertyName("width")]
    public double Width { get; set; }
    [JsonPropertyName("height")]
    public double Height { get; set; }
    [JsonPropertyName("scrollX")]
    public double ScrollX { get; set; }
    [JsonPropertyName("scrollY")]
    public double ScrollY { get; set; }
}
