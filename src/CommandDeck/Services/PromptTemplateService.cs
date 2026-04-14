using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CommandDeck.Models;

namespace CommandDeck.Services;

/// <summary>
/// Registry of prompt templates and agent modes with JSON persistence.
/// Ships with 10 built-in templates and 6 built-in agent modes.
/// </summary>
public sealed class PromptTemplateService : IPromptTemplateService
{
    private static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CommandDeck");

    private static readonly string TemplatesPath = Path.Combine(DataDir, "prompt-templates.json");
    private static readonly string ModesPath = Path.Combine(DataDir, "agent-modes.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly List<PromptTemplate> _templates = new();
    private readonly List<AgentMode> _modes = new();

    public event Action? DataChanged;

    public IReadOnlyList<PromptTemplate> Templates => _templates;
    public IReadOnlyList<AgentMode> Modes => _modes;

    public PromptTemplateService()
    {
        _templates.AddRange(BuiltInTemplates());
        _modes.AddRange(BuiltInModes());
        _ = LoadAsync();
    }

    // ─── Templates CRUD ───────────────────────────────────────────────────────

    public IReadOnlyList<PromptTemplate> GetByCategory(string category)
        => _templates.Where(t => t.Category == category).ToList();

    public PromptTemplate? GetTemplate(string id)
        => _templates.FirstOrDefault(t => t.Id == id);

    public void AddTemplate(PromptTemplate template)
    {
        _templates.Add(template);
        DataChanged?.Invoke();
        _ = SaveAsync();
    }

    public void UpdateTemplate(PromptTemplate template)
    {
        var idx = _templates.FindIndex(t => t.Id == template.Id);
        if (idx >= 0) _templates[idx] = template;
        DataChanged?.Invoke();
        _ = SaveAsync();
    }

    public void DeleteTemplate(string id)
    {
        var t = _templates.FirstOrDefault(t => t.Id == id && !t.IsBuiltIn);
        if (t is not null) _templates.Remove(t);
        DataChanged?.Invoke();
        _ = SaveAsync();
    }

    // ─── Modes CRUD ───────────────────────────────────────────────────────────

    public AgentMode? GetMode(string id)
        => _modes.FirstOrDefault(m => m.Id == id);

    public void AddMode(AgentMode mode)
    {
        _modes.Add(mode);
        DataChanged?.Invoke();
        _ = SaveAsync();
    }

    public void UpdateMode(AgentMode mode)
    {
        var idx = _modes.FindIndex(m => m.Id == mode.Id);
        if (idx >= 0) _modes[idx] = mode;
        DataChanged?.Invoke();
        _ = SaveAsync();
    }

    public void DeleteMode(string id)
    {
        var m = _modes.FirstOrDefault(m => m.Id == id && !m.IsBuiltIn);
        if (m is not null) _modes.Remove(m);
        DataChanged?.Invoke();
        _ = SaveAsync();
    }

    // ─── Persistence ──────────────────────────────────────────────────────────

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(DataDir);

        var customTemplates = _templates.Where(t => !t.IsBuiltIn).ToList();
        var customModes = _modes.Where(m => !m.IsBuiltIn).ToList();

        await File.WriteAllTextAsync(TemplatesPath,
            JsonSerializer.Serialize(customTemplates, JsonOpts));
        await File.WriteAllTextAsync(ModesPath,
            JsonSerializer.Serialize(customModes, JsonOpts));
    }

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(TemplatesPath))
            {
                var json = await File.ReadAllTextAsync(TemplatesPath);
                var custom = JsonSerializer.Deserialize<List<PromptTemplate>>(json, JsonOpts);
                if (custom is not null)
                {
                    foreach (var t in custom.Where(t => _templates.All(b => b.Id != t.Id)))
                        _templates.Add(t);
                }
            }

            if (File.Exists(ModesPath))
            {
                var json = await File.ReadAllTextAsync(ModesPath);
                var custom = JsonSerializer.Deserialize<List<AgentMode>>(json, JsonOpts);
                if (custom is not null)
                {
                    foreach (var m in custom.Where(m => _modes.All(b => b.Id != m.Id)))
                        _modes.Add(m);
                }
            }
        }
        catch { /* ignore corrupt file */ }
    }

    // ─── Built-in templates (10 pré-criados) ─────────────────────────────────

    private static PromptTemplate[] BuiltInTemplates() =>
    [
        new()
        {
            Id = "builtin-code-review",
            Title = "Code Review",
            Description = "Revisão detalhada de código com sugestões de melhoria, bugs e boas práticas.",
            Icon = "🔍",
            AccentColor = "#89b4fa",
            Category = "Código",
            IsBuiltIn = true,
            AutoSend = true,
            Template = """
Faça uma code review completa do código abaixo. Analise:
- Bugs e problemas potenciais
- Performance e otimizações possíveis  
- Legibilidade e boas práticas de {{language}}
- Segurança (se aplicável)
- Sugestões de refatoração

**Linguagem:** {{language}}

```{{language}}
{{code}}
```

Seja específico e forneça exemplos de como corrigir cada problema encontrado.
""",
            Fields =
            [
                new() { Key = "language", Label = "Linguagem", Placeholder = "C#, JavaScript, Python...", DefaultValue = "C#" },
                new() { Key = "code", Label = "Código", Placeholder = "Cole o código aqui...", IsRequired = true },
            ]
        },
        new()
        {
            Id = "builtin-explain-code",
            Title = "Explicar Código",
            Description = "Explica o que um trecho de código faz em linguagem clara.",
            Icon = "📖",
            AccentColor = "#a6e3a1",
            Category = "Código",
            IsBuiltIn = true,
            AutoSend = true,
            Template = """
Explique o seguinte código {{language}} de forma clara e didática:

1. O que ele faz (visão geral)
2. Como funciona passo a passo
3. Quais são os pontos mais importantes
4. Possíveis edge cases ou comportamentos inesperados

```{{language}}
{{code}}
```
""",
            Fields =
            [
                new() { Key = "language", Label = "Linguagem", Placeholder = "C#, JS...", DefaultValue = "C#" },
                new() { Key = "code", Label = "Código", Placeholder = "Cole o código...", IsRequired = true },
            ]
        },
        new()
        {
            Id = "builtin-fix-bug",
            Title = "Corrigir Bug",
            Description = "Identifica e corrige um bug específico com explicação da causa raiz.",
            Icon = "🐛",
            AccentColor = "#f38ba8",
            Category = "Debug",
            IsBuiltIn = true,
            AutoSend = true,
            Template = """
Preciso de ajuda para corrigir um bug.

**Problema:** {{problem}}

**Código com bug:**
```{{language}}
{{code}}
```

**Erro/Comportamento atual:**
{{error}}

Por favor:
1. Identifique a causa raiz do problema
2. Forneça o código corrigido
3. Explique por que a correção funciona
""",
            Fields =
            [
                new() { Key = "problem", Label = "Problema", Placeholder = "Descreva o que está errado...", IsRequired = true },
                new() { Key = "language", Label = "Linguagem", Placeholder = "C#, JS...", DefaultValue = "C#" },
                new() { Key = "code", Label = "Código", Placeholder = "Cole o código com bug...", IsRequired = true },
                new() { Key = "error", Label = "Erro / Comportamento", Placeholder = "Stack trace ou comportamento observado...", IsRequired = false },
            ]
        },
        new()
        {
            Id = "builtin-refactor",
            Title = "Refatorar Código",
            Description = "Refatora código mantendo o comportamento, melhorando legibilidade e manutenibilidade.",
            Icon = "✨",
            AccentColor = "#cba6f7",
            Category = "Código",
            IsBuiltIn = true,
            AutoSend = true,
            Template = """
Refatore o seguinte código {{language}} seguindo estas diretrizes:
{{guidelines}}

**Objetivo da refatoração:** {{goal}}

**Código original:**
```{{language}}
{{code}}
```

Forneça o código refatorado com comentários explicando as principais mudanças.
""",
            Fields =
            [
                new() { Key = "language", Label = "Linguagem", DefaultValue = "C#" },
                new() { Key = "goal", Label = "Objetivo", Placeholder = "Ex: melhorar legibilidade, reduzir complexidade...", IsRequired = true },
                new() { Key = "guidelines", Label = "Diretrizes", Placeholder = "Ex: SOLID, DRY, extrair métodos...", DefaultValue = "SOLID, DRY, Clean Code" },
                new() { Key = "code", Label = "Código", Placeholder = "Cole o código...", IsRequired = true },
            ]
        },
        new()
        {
            Id = "builtin-write-tests",
            Title = "Escrever Testes",
            Description = "Gera testes unitários cobrindo casos principais e edge cases.",
            Icon = "✅",
            AccentColor = "#a6e3a1",
            Category = "Código",
            IsBuiltIn = true,
            AutoSend = true,
            Template = """
Escreva testes {{framework}} para o seguinte código {{language}}. 

Cubra:
- Casos de sucesso (happy path)
- Edge cases e valores limite
- Cenários de erro e exceções
- Mock de dependências externas se necessário

**Código a testar:**
```{{language}}
{{code}}
```
""",
            Fields =
            [
                new() { Key = "language", Label = "Linguagem", DefaultValue = "C#" },
                new() { Key = "framework", Label = "Framework de Testes", DefaultValue = "xUnit", Placeholder = "xUnit, NUnit, Jest, pytest..." },
                new() { Key = "code", Label = "Código", Placeholder = "Cole o código...", IsRequired = true },
            ]
        },
        new()
        {
            Id = "builtin-git-commit",
            Title = "Mensagem de Commit",
            Description = "Gera uma mensagem de commit semântica baseada nas mudanças.",
            Icon = "💾",
            AccentColor = "#fab387",
            Category = "Git",
            IsBuiltIn = true,
            AutoSend = true,
            Template = """
Gere uma mensagem de commit seguindo o padrão Conventional Commits para as seguintes mudanças:

**Mudanças realizadas:**
{{changes}}

**Tipo de mudança:** {{type}}

Formato esperado: `<type>(<scope>): <description>`

Forneça:
1. Mensagem principal (1 linha, máx 72 chars)
2. Corpo opcional com detalhes
3. Breaking changes se houver
""",
            Fields =
            [
                new() { Key = "changes", Label = "Mudanças", Placeholder = "O que foi alterado/adicionado/removido...", IsRequired = true },
                new() { Key = "type", Label = "Tipo", DefaultValue = "feat", Placeholder = "feat, fix, docs, refactor, test, chore..." },
            ]
        },
        new()
        {
            Id = "builtin-sql-query",
            Title = "Escrever SQL",
            Description = "Escreve ou otimiza queries SQL para o banco de dados especificado.",
            Icon = "🗄",
            AccentColor = "#89dceb",
            Category = "Database",
            IsBuiltIn = true,
            AutoSend = true,
            Template = """
{{action}} uma query SQL para {{database}}.

**Objetivo:** {{objective}}

{{#schema}}
**Schema relevante:**
```sql
{{schema}}
```
{{/schema}}

Forneça:
1. A query otimizada
2. Índices recomendados (se aplicável)
3. Explicação do que a query faz
""",
            Fields =
            [
                new() { Key = "action", Label = "Ação", DefaultValue = "Escreva", Placeholder = "Escreva, Otimize, Corrija..." },
                new() { Key = "database", Label = "Banco de Dados", DefaultValue = "SQL Server", Placeholder = "PostgreSQL, MySQL, SQLite..." },
                new() { Key = "objective", Label = "Objetivo", Placeholder = "O que a query deve fazer...", IsRequired = true },
                new() { Key = "schema", Label = "Schema (opcional)", Placeholder = "CREATE TABLE...", IsRequired = false },
            ]
        },
        new()
        {
            Id = "builtin-api-docs",
            Title = "Documentar API",
            Description = "Gera documentação OpenAPI / Swagger ou doc comments para endpoints.",
            Icon = "📋",
            AccentColor = "#b4befe",
            Category = "Documentação",
            IsBuiltIn = true,
            AutoSend = true,
            Template = """
Gere documentação {{format}} para o seguinte endpoint/função {{language}}:

```{{language}}
{{code}}
```

A documentação deve incluir:
- Descrição clara do que faz
- Parâmetros com tipo e descrição
- Exemplos de request/response
- Códigos de erro possíveis
- Notas de segurança (se aplicável)
""",
            Fields =
            [
                new() { Key = "format", Label = "Formato", DefaultValue = "OpenAPI/Swagger", Placeholder = "OpenAPI, XML doc, JSDoc, Docstring..." },
                new() { Key = "language", Label = "Linguagem", DefaultValue = "C#" },
                new() { Key = "code", Label = "Código", Placeholder = "Cole o endpoint/função...", IsRequired = true },
            ]
        },
        new()
        {
            Id = "builtin-code-translate",
            Title = "Traduzir Linguagem",
            Description = "Converte código de uma linguagem para outra mantendo lógica e idiomas.",
            Icon = "🔄",
            AccentColor = "#94e2d5",
            Category = "Código",
            IsBuiltIn = true,
            AutoSend = true,
            Template = """
Converta o seguinte código de {{from}} para {{to}}:

```{{from}}
{{code}}
```

Instruções:
- Mantenha a mesma lógica e comportamento
- Use os idiomas e convenções idiomáticas de {{to}}
- Adapte bibliotecas para equivalentes em {{to}}
- Adicione comentários explicando adaptações não-óbvias
""",
            Fields =
            [
                new() { Key = "from", Label = "De (linguagem)", DefaultValue = "JavaScript", IsRequired = true },
                new() { Key = "to", Label = "Para (linguagem)", DefaultValue = "C#", IsRequired = true },
                new() { Key = "code", Label = "Código", Placeholder = "Cole o código original...", IsRequired = true },
            ]
        },
        new()
        {
            Id = "builtin-regex",
            Title = "Criar Regex",
            Description = "Gera uma expressão regular com explicação e exemplos de teste.",
            Icon = "🔠",
            AccentColor = "#f5c2e7",
            Category = "Utilitários",
            IsBuiltIn = true,
            AutoSend = true,
            Template = """
Crie uma expressão regular para {{language}} que corresponda a:

**Padrão desejado:** {{pattern}}

**Exemplos que DEVEM corresponder:**
{{match_examples}}

**Exemplos que NÃO devem corresponder:**
{{no_match_examples}}

Forneça:
1. A regex final
2. Explicação de cada parte
3. Snippet de código {{language}} usando a regex
""",
            Fields =
            [
                new() { Key = "language", Label = "Linguagem", DefaultValue = "C#" },
                new() { Key = "pattern", Label = "Padrão", Placeholder = "Ex: email válido, número de telefone...", IsRequired = true },
                new() { Key = "match_examples", Label = "Deve corresponder", Placeholder = "user@example.com\ntest@test.org", IsRequired = true },
                new() { Key = "no_match_examples", Label = "Não deve corresponder", Placeholder = "invalidemail\n@nodomain", IsRequired = false },
            ]
        },
    ];

    // ─── Built-in agent modes (6 pré-criados) ────────────────────────────────

    private static AgentMode[] BuiltInModes() =>
    [
        new()
        {
            Id = "builtin-default",
            Name = "Assistente Geral",
            Description = "Modo padrão. Assistente equilibrado para qualquer tarefa de desenvolvimento.",
            Icon = "🤖",
            AccentColor = "#cba6f7",
            IsBuiltIn = true,
            Temperature = 0.7,
            WelcomeMessage = "Olá! Sou seu assistente de desenvolvimento. Como posso ajudar?",
            SystemPrompt = """
You are a helpful developer assistant integrated into CommandDeck, a terminal management and development dashboard for Windows. 
Be concise, practical, and focus on development tasks. Use code blocks for code snippets. 
Respond in the same language as the user's message.
"""
        },
        new()
        {
            Id = "builtin-code-review",
            Name = "Code Reviewer",
            Description = "Especialista em revisão de código. Analisa qualidade, segurança e boas práticas.",
            Icon = "🔍",
            AccentColor = "#89b4fa",
            IsBuiltIn = true,
            Temperature = 0.3,
            WelcomeMessage = "Modo Code Reviewer ativado. Cole o código que deseja revisar.",
            SystemPrompt = """
You are an expert code reviewer with deep knowledge of software engineering principles, design patterns, and security best practices.

When reviewing code:
- Be specific about issues (mention line numbers when possible)
- Categorize findings: 🔴 Critical, 🟡 Warning, 🟢 Suggestion
- Check for: bugs, security vulnerabilities, performance issues, code smells, SOLID violations
- Provide corrected code examples for each issue
- Be constructive and educational in tone
- Prioritize the most impactful issues

Format your review with clear sections: Summary, Critical Issues, Warnings, Suggestions, Positive Aspects.
Respond in the same language as the user.
"""
        },
        new()
        {
            Id = "builtin-debug",
            Name = "Debug Assistant",
            Description = "Especialista em debugging. Identifica causas raiz e propõe correções passo a passo.",
            Icon = "🐛",
            AccentColor = "#f38ba8",
            IsBuiltIn = true,
            Temperature = 0.2,
            WelcomeMessage = "Modo Debug ativado. Descreva o problema, cole o erro e o código relevante.",
            SystemPrompt = """
You are an expert debugging assistant. Your goal is to systematically identify and fix bugs.

When debugging:
1. Analyze the error message and stack trace carefully
2. Identify the root cause (not just symptoms)
3. Explain WHY the bug occurs
4. Provide a minimal, targeted fix
5. Suggest how to prevent similar bugs in the future
6. Add defensive code if appropriate

Use systematic debugging methodology: hypothesize → verify → fix.
Always show the corrected code, not just describe the fix.
Respond in the same language as the user.
"""
        },
        new()
        {
            Id = "builtin-explain",
            Name = "Explain Mode",
            Description = "Explica conceitos, código e arquitetura de forma clara e didática.",
            Icon = "📖",
            AccentColor = "#a6e3a1",
            IsBuiltIn = true,
            Temperature = 0.7,
            WelcomeMessage = "Modo Explicação ativado. O que você gostaria de entender melhor?",
            SystemPrompt = """
You are a patient and clear technical educator. Your goal is to make complex concepts accessible.

When explaining:
- Start with the big picture, then dive into details
- Use analogies and real-world examples
- Break complex topics into digestible steps
- Include code examples to illustrate concepts
- Anticipate follow-up questions and address them proactively
- Adjust explanation depth based on the user's apparent level
- Use diagrams described in text (ASCII art) when helpful

Always check: "Would a junior developer understand this?"
Respond in the same language as the user.
"""
        },
        new()
        {
            Id = "builtin-refactor",
            Name = "Refactor Mode",
            Description = "Especialista em refatoração. Melhora código sem alterar comportamento.",
            Icon = "✨",
            AccentColor = "#cba6f7",
            IsBuiltIn = true,
            Temperature = 0.4,
            WelcomeMessage = "Modo Refatoração ativado. Qual código você deseja melhorar?",
            SystemPrompt = """
You are an expert software craftsman specializing in code refactoring and clean code principles.

When refactoring:
- Preserve existing behavior (TDD: tests should still pass)
- Apply SOLID, DRY, YAGNI, KISS principles
- Extract methods/classes for single responsibility
- Improve naming for clarity
- Reduce cyclomatic complexity
- Use language-idiomatic patterns
- Show before/after comparisons
- Explain the rationale for each change

Prioritize readability and maintainability over premature optimization.
Respond in the same language as the user.
"""
        },
        new()
        {
            Id = "builtin-git",
            Name = "Git Expert",
            Description = "Especialista em Git. Ajuda com comandos, workflows, conflitos e histórico.",
            Icon = "🌿",
            AccentColor = "#fab387",
            IsBuiltIn = true,
            Temperature = 0.3,
            WelcomeMessage = "Modo Git Expert ativado. Como posso ajudar com seu repositório?",
            SystemPrompt = """
You are a Git expert with deep knowledge of version control workflows and best practices.

You help with:
- Git commands (always show exact commands with explanations)
- Branch strategies (Git Flow, trunk-based, feature branches)
- Merge vs Rebase decisions
- Conflict resolution
- History cleanup (rebase, squash, amend)
- Recovery from mistakes (reflog, reset, restore)
- Conventional Commits and semantic versioning
- CI/CD integration with Git

Always show the exact git commands to run. Warn about destructive operations.
Respond in the same language as the user.
"""
        },
    ];
}
