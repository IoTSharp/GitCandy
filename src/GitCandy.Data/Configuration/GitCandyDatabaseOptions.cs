namespace GitCandy.Data.Configuration;

/// <summary>
/// GitCandy 数据库配置。
/// </summary>
public sealed class GitCandyDatabaseOptions
{
    /// <summary>
    /// 标准配置节名称。
    /// </summary>
    public const string SectionName = "GitCandy:Database";

    /// <summary>
    /// 默认连接串名称。
    /// </summary>
    public const string DefaultConnectionStringName = "GitCandy";

    /// <summary>
    /// 旧 EF6 连接串名称，仅作为配置迁移兜底读取。
    /// </summary>
    public const string LegacyConnectionStringName = "GitCandyContext";

    /// <summary>
    /// 数据库 provider。
    /// </summary>
    public GitCandyDatabaseProvider Provider { get; set; } = GitCandyDatabaseProvider.Sqlite;

    /// <summary>
    /// 从 ConnectionStrings 读取的连接串名称。
    /// </summary>
    public string ConnectionStringName { get; set; } = DefaultConnectionStringName;

    /// <summary>
    /// 最终使用的连接串。
    /// </summary>
    public string ConnectionString { get; set; } = "Data Source=App_Data/GitCandy.db";

    /// <summary>
    /// DbContext pool 大小。
    /// </summary>
    public int DbContextPoolSize { get; set; } = 128;

    /// <summary>
    /// 是否启用 EF Core 敏感数据日志。生产环境默认保持关闭。
    /// </summary>
    public bool EnableSensitiveDataLogging { get; set; }
}
