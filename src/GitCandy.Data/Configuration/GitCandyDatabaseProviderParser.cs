namespace GitCandy.Data.Configuration;

/// <summary>
/// 数据库 provider 配置值解析器。
/// </summary>
public static class GitCandyDatabaseProviderParser
{
    /// <summary>
    /// 解析数据库 provider 配置值。
    /// </summary>
    /// <param name="value">配置中的 provider 名称。</param>
    /// <param name="provider">解析后的 provider。</param>
    /// <returns>配置值受支持时返回 true。</returns>
    public static bool TryParse(string? value, out GitCandyDatabaseProvider provider)
    {
        provider = GitCandyDatabaseProvider.Sqlite;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (string.Equals(normalized, "sqlite", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "sqlite3", StringComparison.OrdinalIgnoreCase))
        {
            provider = GitCandyDatabaseProvider.Sqlite;
            return true;
        }

        if (string.Equals(normalized, "pgsql", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "postgres", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "postgresql", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "npgsql", StringComparison.OrdinalIgnoreCase))
        {
            provider = GitCandyDatabaseProvider.PostgreSql;
            return true;
        }

        if (string.Equals(normalized, "sonnet", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "sonnetdb", StringComparison.OrdinalIgnoreCase))
        {
            provider = GitCandyDatabaseProvider.SonnetDB;
            return true;
        }

        return false;
    }
}
