using System.Text.RegularExpressions;

namespace GitCandy.Governance;

/// <summary>CODEOWNERS 读取与匹配使用的固定资源边界。</summary>
public static class CodeOwnersLimits
{
    public const int MaxFileBytes = 256 * 1024;
    public const int MaxRules = 2_048;
    public const int MaxLineLength = 4_096;
    public const int MaxPatternLength = 1_024;
    public const int MaxOwnersPerRule = 64;
    public const int MaxChangedPaths = 10_000;
    public const int MaxMatchEvaluations = 250_000;
}

/// <summary>目标 head 上的 CODEOWNERS 内容和 merge-base changed paths。</summary>
public sealed record CodeOwnersSnapshot(
    string? SourcePath,
    string? Content,
    IReadOnlyList<string> ChangedPaths,
    string? Error = null);

/// <summary>一条经过受控解析的 CODEOWNERS 规则。</summary>
public sealed record CodeOwnerRule(
    int LineNumber,
    string Pattern,
    IReadOnlyList<string> Owners);

/// <summary>changed path 最后命中的 CODEOWNERS 规则。</summary>
public sealed record CodeOwnerAssignment(
    string Path,
    int LineNumber,
    string Pattern,
    IReadOnlyList<string> Owners);

/// <summary>CODEOWNERS 解析与 changed-path 归属结果。</summary>
public sealed record CodeOwnersEvaluation(
    bool IsValid,
    string? SourcePath,
    IReadOnlyList<CodeOwnerRule> Rules,
    IReadOnlyList<CodeOwnerAssignment> Assignments,
    IReadOnlyList<string> Diagnostics);

/// <summary>按 GitHub CODEOWNERS 常用子集执行受限、确定性的解析与匹配。</summary>
public static class CodeOwnersParser
{
    /// <summary>解析 CODEOWNERS 并按最后匹配规则解析每个 changed path。</summary>
    public static CodeOwnersEvaluation Evaluate(CodeOwnersSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.IsNullOrWhiteSpace(snapshot.Error))
        {
            return Invalid(snapshot.SourcePath, snapshot.Error);
        }

        if (snapshot.ChangedPaths.Count > CodeOwnersLimits.MaxChangedPaths)
        {
            return Invalid(snapshot.SourcePath, $"changed paths exceed the {CodeOwnersLimits.MaxChangedPaths} path limit");
        }

        if (snapshot.Content is null || snapshot.SourcePath is null)
        {
            return Invalid(null, "CODEOWNERS was not found at .github/CODEOWNERS, CODEOWNERS, or docs/CODEOWNERS");
        }

        if (snapshot.Content.Length > CodeOwnersLimits.MaxFileBytes)
        {
            return Invalid(snapshot.SourcePath, $"CODEOWNERS exceeds the {CodeOwnersLimits.MaxFileBytes} byte limit");
        }

        var rules = new List<CodeOwnerRule>();
        var diagnostics = new List<string>();
        using var reader = new StringReader(snapshot.Content);
        var lineNumber = 0;
        while (reader.ReadLine() is string line)
        {
            lineNumber++;
            if (line.Length > CodeOwnersLimits.MaxLineLength)
            {
                diagnostics.Add($"line {lineNumber} exceeds the {CodeOwnersLimits.MaxLineLength} character limit");
                continue;
            }

            var tokens = Tokenize(line, lineNumber, diagnostics);
            if (tokens.Count == 0)
            {
                continue;
            }

            if (rules.Count >= CodeOwnersLimits.MaxRules)
            {
                diagnostics.Add($"CODEOWNERS exceeds the {CodeOwnersLimits.MaxRules} rule limit");
                break;
            }

            var pattern = tokens[0];
            if (!IsValidPattern(pattern))
            {
                diagnostics.Add($"line {lineNumber} contains an unsupported pattern");
                continue;
            }

            var owners = tokens.Skip(1)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (owners.Length == 0 || owners.Length > CodeOwnersLimits.MaxOwnersPerRule)
            {
                diagnostics.Add($"line {lineNumber} must contain 1 to {CodeOwnersLimits.MaxOwnersPerRule} owners");
                continue;
            }

            if (owners.Any(static owner => !IsValidOwner(owner)))
            {
                diagnostics.Add($"line {lineNumber} contains an unsupported owner token");
                continue;
            }

            rules.Add(new CodeOwnerRule(lineNumber, pattern, owners));
        }

        if (diagnostics.Count > 0)
        {
            return new CodeOwnersEvaluation(false, snapshot.SourcePath, rules, [], diagnostics);
        }

        var changedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in snapshot.ChangedPaths)
        {
            var normalized = NormalizePath(path);
            if (normalized is null)
            {
                return Invalid(snapshot.SourcePath, "changed paths contain an invalid repository-relative path");
            }

            changedPaths.Add(normalized);
        }

        if ((long)changedPaths.Count * rules.Count > CodeOwnersLimits.MaxMatchEvaluations)
        {
            return Invalid(
                snapshot.SourcePath,
                $"CODEOWNERS matching exceeds the {CodeOwnersLimits.MaxMatchEvaluations} evaluation limit");
        }

        var matchers = rules.Select(static rule => new CodeOwnerMatcher(rule.Pattern)).ToArray();
        var assignments = new List<CodeOwnerAssignment>();
        foreach (var path in changedPaths.OrderBy(static value => value, StringComparer.Ordinal))
        {
            for (var index = rules.Count - 1; index >= 0; index--)
            {
                if (!matchers[index].IsMatch(path))
                {
                    continue;
                }

                var rule = rules[index];
                assignments.Add(new CodeOwnerAssignment(path, rule.LineNumber, rule.Pattern, rule.Owners));
                break;
            }
        }

        return new CodeOwnersEvaluation(true, snapshot.SourcePath, rules, assignments, []);
    }

    private static CodeOwnersEvaluation Invalid(string? path, string diagnostic) =>
        new(false, path, [], [], [diagnostic]);

    private static List<string> Tokenize(string line, int lineNumber, List<string> diagnostics)
    {
        var tokens = new List<string>();
        var token = new System.Text.StringBuilder();
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (escaped)
            {
                token.Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '#')
            {
                break;
            }

            if (char.IsWhiteSpace(character))
            {
                AddToken(tokens, token);
                continue;
            }

            token.Append(character);
        }

        if (escaped)
        {
            diagnostics.Add($"line {lineNumber} ends with an incomplete escape");
        }
        AddToken(tokens, token);
        return tokens;
    }

    private static void AddToken(List<string> tokens, System.Text.StringBuilder token)
    {
        if (token.Length == 0)
        {
            return;
        }

        tokens.Add(token.ToString());
        token.Clear();
    }

    private static bool IsValidPattern(string pattern) =>
        pattern.Length is > 0 and <= CodeOwnersLimits.MaxPatternLength
        && pattern[0] != '!'
        && !pattern.Contains('[', StringComparison.Ordinal)
        && !pattern.Contains(']', StringComparison.Ordinal)
        && !pattern.Contains('\0')
        && !pattern.Contains("..", StringComparison.Ordinal)
        && !pattern.Any(char.IsControl);

    private static bool IsValidOwner(string owner)
    {
        if (owner.Length is < 2 or > 256 || owner[0] != '@')
        {
            return false;
        }

        return owner.Skip(1).All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_');
    }

    private static string? NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > CodeOwnersLimits.MaxLineLength)
        {
            return null;
        }

        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0
            || normalized.Split('/').Any(static segment => segment is "" or "." or "..")
            || normalized.Any(char.IsControl))
        {
            return null;
        }

        return normalized;
    }

    private sealed class CodeOwnerMatcher
    {
        private readonly Regex _regex;

        public CodeOwnerMatcher(string pattern)
        {
            var normalizedPattern = pattern.Replace('\\', '/');
            var rooted = normalizedPattern.StartsWith('/');
            normalizedPattern = normalizedPattern.TrimStart('/');
            if (normalizedPattern.EndsWith('/'))
            {
                normalizedPattern += "**";
            }
            if (!rooted && !normalizedPattern.Contains('/'))
            {
                normalizedPattern = "**/" + normalizedPattern;
            }

            _regex = new Regex(
                BuildExpression(normalizedPattern),
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        }

        public bool IsMatch(string path) => _regex.IsMatch(path);

        private static string BuildExpression(string pattern)
        {
            var expression = new System.Text.StringBuilder("\\A");
            for (var index = 0; index < pattern.Length; index++)
            {
                var character = pattern[index];
                if (character == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
                {
                    index++;
                    while (index + 1 < pattern.Length && pattern[index + 1] == '*')
                    {
                        index++;
                    }

                    if (index + 1 < pattern.Length && pattern[index + 1] == '/')
                    {
                        index++;
                        expression.Append("(?:.*/)?");
                    }
                    else
                    {
                        expression.Append(".*");
                    }
                }
                else if (character == '*')
                {
                    expression.Append("[^/]*");
                }
                else if (character == '?')
                {
                    expression.Append("[^/]");
                }
                else
                {
                    expression.Append(Regex.Escape(character.ToString()));
                }
            }

            return expression.Append("\\z").ToString();
        }
    }
}
