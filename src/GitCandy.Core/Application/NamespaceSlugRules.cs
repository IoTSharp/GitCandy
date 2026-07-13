using System.Text.RegularExpressions;

namespace GitCandy.Application;

/// <summary>namespace/repository slug 的共享校验和系统保留名称。</summary>
public static partial class NamespaceSlugRules
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "account",
        "api",
        "assets",
        "git",
        "health",
        "home",
        "identity",
        "legacy",
        "me",
        "notifications",
        "todos",
        "explore",
        "repository",
        "setting",
        "signin-oidc",
        "signout-callback-oidc",
        "team"
    };

    /// <summary>系统保留的根路由 slug。</summary>
    public static IReadOnlyCollection<string> ReservedSlugs => Reserved;

    /// <summary>返回适合唯一索引的大小写不敏感值。</summary>
    public static string Normalize(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return slug.Trim().ToUpperInvariant();
    }

    /// <summary>验证 namespace slug。</summary>
    public static bool IsValidNamespaceSlug(string? slug)
    {
        return slug is not null
            && slug.Length <= 50
            && SlugPattern().IsMatch(slug)
            && !slug.EndsWith(".git", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>验证 repository slug。</summary>
    public static bool IsValidRepositorySlug(string? slug)
    {
        return slug is not null
            && slug.Length <= 50
            && SlugPattern().IsMatch(slug)
            && !slug.EndsWith(".git", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>判断 slug 是否由系统路由保留。</summary>
    public static bool IsReserved(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return Reserved.Contains(slug.Trim());
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9._-]+$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SlugPattern();
}
