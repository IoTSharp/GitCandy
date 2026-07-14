using System.IO.Enumeration;
using System.Text.RegularExpressions;
using GitCandy.Remotes;

namespace GitCandy.Web.Remotes;

internal sealed class RemoteMirrorRefFilter
{
    private const int MaxAllowListPatterns = 128;
    private readonly Func<string, bool> _matches;

    private RemoteMirrorRefFilter(Func<string, bool> matches)
    {
        _matches = matches;
    }

    public bool Matches(string referenceName) => IsPublicReference(referenceName) && _matches(referenceName);

    public static bool TryCreate(
        RemoteMirrorRefFilterKind kind,
        string? pattern,
        IReadOnlyList<string> protectedBranchPatterns,
        out RemoteMirrorRefFilter? filter)
    {
        ArgumentNullException.ThrowIfNull(protectedBranchPatterns);
        switch (kind)
        {
            case RemoteMirrorRefFilterKind.AllRefs when pattern is null:
                filter = new RemoteMirrorRefFilter(static _ => true);
                return true;
            case RemoteMirrorRefFilterKind.ProtectedBranches when pattern is null:
                var branchPatterns = protectedBranchPatterns
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                filter = new RemoteMirrorRefFilter(referenceName =>
                {
                    if (!referenceName.StartsWith("refs/heads/", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var branchName = referenceName["refs/heads/".Length..];
                    return branchPatterns.Any(item => FileSystemName.MatchesSimpleExpression(
                        item,
                        branchName,
                        ignoreCase: false));
                });
                return true;
            case RemoteMirrorRefFilterKind.AllowList when TryParseAllowList(pattern, out var allowList):
                filter = new RemoteMirrorRefFilter(referenceName => allowList.Any(item =>
                    FileSystemName.MatchesSimpleExpression(item, referenceName, ignoreCase: false)));
                return true;
            case RemoteMirrorRefFilterKind.RegularExpression when TryCreateRegularExpression(pattern, out var expression):
                filter = new RemoteMirrorRefFilter(expression.IsMatch);
                return true;
            default:
                filter = null;
                return false;
        }
    }

    private static bool TryParseAllowList(string? value, out string[] patterns)
    {
        patterns = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parsed = value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static item => item.TrimEnd('\r'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (parsed.Length is 0 or > MaxAllowListPatterns || parsed.Any(static item => !IsValidPattern(item)))
        {
            return false;
        }

        patterns = parsed;
        return true;
    }

    private static bool TryCreateRegularExpression(string? value, out Regex expression)
    {
        expression = null!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1000)
        {
            return false;
        }

        try
        {
            expression = new Regex(
                $"\\A(?:{value})\\z",
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                TimeSpan.FromMilliseconds(100));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsValidPattern(string value)
    {
        var wildcardCount = value.Count(static character => character == '*');
        var validationValue = wildcardCount == 1
            ? value.Replace("*", "gitcandy-pattern", StringComparison.Ordinal)
            : value;
        return wildcardCount <= 1
            && IsPublicReference(validationValue)
            && !value.Any(static character => char.IsControl(character) || char.IsWhiteSpace(character));
    }

    private static bool IsPublicReference(string referenceName)
    {
        return referenceName.Length is > 11 and <= 255
            && (referenceName.StartsWith("refs/heads/", StringComparison.Ordinal)
                || referenceName.StartsWith("refs/tags/", StringComparison.Ordinal))
            && !referenceName.Contains("..", StringComparison.Ordinal)
            && !referenceName.Contains("@{", StringComparison.Ordinal)
            && !referenceName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
            && !referenceName.EndsWith('/')
            && !referenceName.Contains("//", StringComparison.Ordinal)
            && !referenceName.Any(static character => char.IsControl(character)
                || char.IsWhiteSpace(character)
                || character is '~' or '^' or ':' or '?' or '[' or '\\');
    }
}
