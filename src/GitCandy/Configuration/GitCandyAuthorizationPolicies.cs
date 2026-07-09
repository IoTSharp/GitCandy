namespace GitCandy.Configuration;

/// <summary>
/// GitCandy ASP.NET Core 授权策略名称。
/// </summary>
public static class GitCandyAuthorizationPolicies
{
    /// <summary>
    /// 系统管理员策略占位，用于后续迁移旧管理员过滤器。
    /// </summary>
    public const string Administrator = "GitCandy.Administrator";
}
