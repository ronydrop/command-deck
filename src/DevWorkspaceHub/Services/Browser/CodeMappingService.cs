using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DevWorkspaceHub.Models.Browser;

namespace DevWorkspaceHub.Services.Browser;

public partial class CodeMappingService : ICodeMappingService
{
    private static readonly string[] SearchExtensions = ["*.tsx", "*.jsx", "*.vue", "*.svelte"];
    private static readonly string[] IgnoredDirs = ["node_modules", "dist", "build", ".next"];

    public async Task<CodeMappingResult> MapElementToCodeAsync(ElementCaptureData element, string projectPath)
    {
        var candidates = new List<CodeMappingCandidate>();

        var result = TryReactFiber(element, projectPath, candidates);
        if (result is not null) return result;

        result = TryDataTestId(element, projectPath, candidates);
        if (result is not null) return result;

        result = TryClassNameHeuristic(element, projectPath, candidates);
        if (result is not null) return result;

        result = TryFileSearch(element, projectPath, candidates);
        if (result is not null) return result;

        return await Task.FromResult(new CodeMappingResult
        {
            Strategy = CodeMappingStrategy.None,
            Confidence = 0,
            Candidates = candidates
        });
    }

    private static CodeMappingResult? TryReactFiber(
        ElementCaptureData element, string projectPath, List<CodeMappingCandidate> candidates)
    {
        var fw = element.FrameworkInfo;
        if (fw is null
            || !string.Equals(fw.Framework, "react", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(fw.ComponentName))
            return null;

        var files = FindProjectFiles(projectPath);
        var matches = files
            .Where(f => FileNameMatchesComponent(f, fw.ComponentName))
            .ToList();

        foreach (var m in matches)
        {
            candidates.Add(new CodeMappingCandidate
            {
                FilePath = m,
                Confidence = 0.95,
                Reason = $"React Fiber component name '{fw.ComponentName}'"
            });
        }

        if (matches.Count > 0)
        {
            return new CodeMappingResult
            {
                FilePath = matches[0],
                ComponentName = fw.ComponentName,
                Confidence = 0.95,
                Strategy = CodeMappingStrategy.ReactFiber,
                Candidates = candidates
            };
        }

        return null;
    }

    private static CodeMappingResult? TryDataTestId(
        ElementCaptureData element, string projectPath, List<CodeMappingCandidate> candidates)
    {
        if (element.Attributes is null
            || !element.Attributes.TryGetValue("data-testid", out var testId)
            || string.IsNullOrWhiteSpace(testId))
            return null;

        var componentName = ToPascalCase(testId);
        var files = FindProjectFiles(projectPath);
        var matches = files
            .Where(f => FileNameMatchesComponent(f, componentName))
            .ToList();

        foreach (var m in matches)
        {
            candidates.Add(new CodeMappingCandidate
            {
                FilePath = m,
                Confidence = 0.8,
                Reason = $"data-testid '{testId}' -> '{componentName}'"
            });
        }

        if (matches.Count > 0)
        {
            return new CodeMappingResult
            {
                FilePath = matches[0],
                ComponentName = componentName,
                Confidence = 0.8,
                Strategy = CodeMappingStrategy.DataTestId,
                Candidates = candidates
            };
        }

        return null;
    }

    private static CodeMappingResult? TryClassNameHeuristic(
        ElementCaptureData element, string projectPath, List<CodeMappingCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(element.ClassName))
            return null;

        var classes = element.ClassName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var files = FindProjectFiles(projectPath);

        foreach (var cls in classes)
        {
            var componentName = BemToComponentName(cls);
            if (string.IsNullOrWhiteSpace(componentName))
                continue;

            var matches = files
                .Where(f => FileNameMatchesComponent(f, componentName))
                .ToList();

            foreach (var m in matches)
            {
                candidates.Add(new CodeMappingCandidate
                {
                    FilePath = m,
                    Confidence = 0.6,
                    Reason = $"BEM class '{cls}' -> '{componentName}'"
                });
            }

            if (matches.Count > 0)
            {
                return new CodeMappingResult
                {
                    FilePath = matches[0],
                    ComponentName = componentName,
                    Confidence = 0.6,
                    Strategy = CodeMappingStrategy.ClassNameHeuristic,
                    Candidates = candidates
                };
            }
        }

        return null;
    }

    private static CodeMappingResult? TryFileSearch(
        ElementCaptureData element, string projectPath, List<CodeMappingCandidate> candidates)
    {
        var tag = element.TagName;
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var componentName = ToPascalCase(tag);
        var files = FindProjectFiles(projectPath);
        var matches = files
            .Where(f => FileNameMatchesComponent(f, componentName))
            .ToList();

        foreach (var m in matches)
        {
            candidates.Add(new CodeMappingCandidate
            {
                FilePath = m,
                Confidence = 0.3,
                Reason = $"Tag name '{tag}' file search"
            });
        }

        if (matches.Count > 0)
        {
            return new CodeMappingResult
            {
                FilePath = matches[0],
                ComponentName = componentName,
                Confidence = 0.3,
                Strategy = CodeMappingStrategy.FileSearch,
                Candidates = candidates
            };
        }

        return null;
    }

    private static List<string> FindProjectFiles(string projectPath)
    {
        var results = new List<string>();

        if (!Directory.Exists(projectPath))
            return results;

        foreach (var ext in SearchExtensions)
        {
            try
            {
                var files = Directory.GetFiles(projectPath, ext, SearchOption.AllDirectories);
                results.AddRange(files.Where(f => !IsIgnoredPath(f)));
            }
            catch
            {
                // skip inaccessible directories
            }
        }

        return results;
    }

    private static bool IsIgnoredPath(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');
        return IgnoredDirs.Any(dir =>
            normalized.Contains($"/{dir}/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool FileNameMatchesComponent(string filePath, string componentName)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return string.Equals(fileName, componentName, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var parts = PascalCaseSplitter().Split(input)
            .Where(s => !string.IsNullOrWhiteSpace(s));

        return string.Concat(parts.Select(p =>
            char.ToUpper(p[0], CultureInfo.InvariantCulture) +
            p[1..].ToLower(CultureInfo.InvariantCulture)));
    }

    private static string BemToComponentName(string bemClass)
    {
        var block = bemClass.Split("__")[0];
        block = block.Split("--")[0];
        return ToPascalCase(block);
    }

    [GeneratedRegex(@"[-_\s]+")]
    private static partial Regex PascalCaseSplitter();
}
