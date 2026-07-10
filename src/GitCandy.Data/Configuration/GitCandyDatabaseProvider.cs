namespace GitCandy.Data.Configuration;

/// <summary>
/// GitCandy 支持的 EF Core 数据库 provider。
/// </summary>
public enum GitCandyDatabaseProvider
{
    /// <summary>
    /// SQLite provider，作为默认本地数据库。
    /// </summary>
    Sqlite,

    /// <summary>
    /// SQL Server provider，用于生成和部署 SQL Server schema。
    /// </summary>
    SqlServer,

    /// <summary>
    /// PostgreSQL provider，用于 pgsql 部署。
    /// </summary>
    PostgreSql,

    /// <summary>
    /// SonnetDB provider，用于 SonnetDB 单依赖部署。
    /// </summary>
    SonnetDB
}
