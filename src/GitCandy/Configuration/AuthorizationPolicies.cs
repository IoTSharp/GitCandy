namespace GitCandy.Configuration;

/// <summary>
/// ASP.NET Core 授权策略名称。
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>
    /// 系统管理员策略占位，用于后续迁移旧管理员过滤器。
    /// </summary>
    public const string Administrator = "GitCandy.Administrator";

    /// <summary>
    /// 读取仓库资源的策略。
    /// </summary>
    public const string RepositoryRead = "GitCandy.Repository.Read";

    /// <summary>
    /// 写入仓库资源的策略。
    /// </summary>
    public const string RepositoryWrite = "GitCandy.Repository.Write";

    /// <summary>
    /// 仓库 owner 或系统管理员策略。
    /// </summary>
    public const string RepositoryOwner = "GitCandy.Repository.Owner";

    /// <summary>
    /// 团队管理员或系统管理员策略。
    /// </summary>
    public const string TeamAdministrator = "GitCandy.Team.Administrator";

    /// <summary>
    /// 当前用户自身或系统管理员策略。
    /// </summary>
    public const string CurrentUser = "GitCandy.CurrentUser";
}
