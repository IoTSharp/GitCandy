namespace GitCandy.Data.Domain;

/// <summary>
/// GitCandy 领域名称规范化工具，用于跨数据库保持大小写不敏感语义。
/// </summary>
public static class GitCandyNameNormalizer
{
    /// <summary>
    /// 将仓库名称规范化为可比较值。
    /// </summary>
    /// <param name="name">原始仓库名称。</param>
    /// <returns>规范化后的仓库名称。</returns>
    public static string NormalizeRepositoryName(string name)
    {
        return Normalize(name);
    }

    /// <summary>
    /// 将团队名称规范化为可比较值。
    /// </summary>
    /// <param name="name">原始团队名称。</param>
    /// <returns>规范化后的团队名称。</returns>
    public static string NormalizeTeamName(string name)
    {
        return Normalize(name);
    }

    /// <summary>
    /// 将仓库、团队等领域名称规范化为可比较值。
    /// </summary>
    /// <param name="name">原始名称。</param>
    /// <returns>规范化后的名称。</returns>
    public static string Normalize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return name.ToUpperInvariant();
    }
}
