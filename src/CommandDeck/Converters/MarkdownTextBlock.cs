using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CommandDeck.Converters;

/// <summary>
/// Attached property that renders basic markdown into TextBlock.Inlines.
/// Supports: **bold**, *italic*, `inline code`, ```code blocks```, and - lists.
/// </summary>
public static class MarkdownTextBlock
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.RegisterAttached(
            "Markdown",
            typeof(string),
            typeof(MarkdownTextBlock),
            new PropertyMetadata(null, OnMarkdownChanged));

    public static string? GetMarkdown(DependencyObject obj) => (string?)obj.GetValue(MarkdownProperty);
    public static void SetMarkdown(DependencyObject obj, string? value) => obj.SetValue(MarkdownProperty, value);

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock) return;

        textBlock.Inlines.Clear();

        var markdown = e.NewValue as string;
        if (string.IsNullOrEmpty(markdown))
            return;

        try
        {
            var inlines = ParseMarkdown(markdown);
            foreach (var inline in inlines)
                textBlock.Inlines.Add(inline);
        }
        catch
        {
            // Fallback to plain text on any parsing error
            textBlock.Inlines.Add(new Run(markdown));
        }
    }

    private static List<Inline> ParseMarkdown(string text)
    {
        var inlines = new List<Inline>();
        var lines = text.Split('\n');
        var inCodeBlock = false;
        var codeBlockContent = new List<string>();
        var codeBlockLang = string.Empty;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Code block toggle
            if (line.TrimStart().StartsWith("```"))
            {
                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    codeBlockLang = line.TrimStart().Length > 3 ? line.TrimStart()[3..].Trim() : "";
                    codeBlockContent.Clear();
                    continue;
                }
                else
                {
                    // End code block
                    inCodeBlock = false;
                    if (inlines.Count > 0)
                        inlines.Add(new LineBreak());

                    var codeText = string.Join("\n", codeBlockContent);
                    var codeRun = new Run(codeText)
                    {
                        FontFamily = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, Courier New"),
                        FontSize = 12,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6E3A1")),
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E"))
                    };
                    inlines.Add(codeRun);
                    inlines.Add(new LineBreak());
                    continue;
                }
            }

            if (inCodeBlock)
            {
                codeBlockContent.Add(line);
                continue;
            }

            // Add line break between non-code lines (except first line)
            if (i > 0 && inlines.Count > 0)
                inlines.Add(new LineBreak());

            // List items
            if (line.TrimStart().StartsWith("- "))
            {
                inlines.Add(new Run("  \u2022 ") { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBA6F7")) });
                ParseInlineMarkdown(line.TrimStart()[2..], inlines);
                continue;
            }

            // Regular line — parse inline markdown
            ParseInlineMarkdown(line, inlines);
        }

        // Handle unclosed code block
        if (inCodeBlock && codeBlockContent.Count > 0)
        {
            inlines.Add(new LineBreak());
            var codeText = string.Join("\n", codeBlockContent);
            inlines.Add(new Run(codeText)
            {
                FontFamily = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, Courier New"),
                FontSize = 12,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6E3A1"))
            });
        }

        return inlines;
    }

    private static void ParseInlineMarkdown(string text, List<Inline> inlines)
    {
        // Pattern: **bold**, *italic*, `code`
        var pattern = @"(\*\*(.+?)\*\*)|(\*(.+?)\*)|(`(.+?)`)";
        var lastIndex = 0;

        foreach (Match match in Regex.Matches(text, pattern))
        {
            // Add text before the match
            if (match.Index > lastIndex)
                inlines.Add(new Run(text[lastIndex..match.Index]));

            if (match.Groups[1].Success) // **bold**
            {
                inlines.Add(new Bold(new Run(match.Groups[2].Value)));
            }
            else if (match.Groups[3].Success) // *italic*
            {
                inlines.Add(new Italic(new Run(match.Groups[4].Value)));
            }
            else if (match.Groups[5].Success) // `code`
            {
                inlines.Add(new Run(match.Groups[6].Value)
                {
                    FontFamily = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, Courier New"),
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5C2E7")),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2B3D"))
                });
            }

            lastIndex = match.Index + match.Length;
        }

        // Remaining text after last match
        if (lastIndex < text.Length)
            inlines.Add(new Run(text[lastIndex..]));

        // If no text was added at all (empty line)
        if (lastIndex == 0 && text.Length == 0)
            inlines.Add(new Run(" "));
    }
}
