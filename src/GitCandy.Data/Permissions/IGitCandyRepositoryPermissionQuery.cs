namespace GitCandy.Data.Permissions;

/// <summary>
/// GitCandy 仓库权限查询入口。
/// </summary>
public interface IGitCandyRepositoryPermissionQuery
{
    /// <summary>
    /// 判断用户是否可以读取仓库。
    /// </summary>
    /// <param name="repositoryName">仓库名称。</param>
    /// <param name="userId">Identity 用户主键；匿名用户传入 <see langword="null" />。</param>
    /// <param name="isAdministrator">调用方根据 Identity role 或 policy 得出的管理员标记。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>若允许读取则为 <see langword="true" />。</returns>
    Task<bool> CanReadRepositoryAsync(
        string repositoryName,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断用户是否可以写入仓库。
    /// </summary>
    /// <param name="repositoryName">仓库名称。</param>
    /// <param name="userId">Identity 用户主键；匿名用户传入 <see langword="null" />。</param>
    /// <param name="isAdministrator">调用方根据 Identity role 或 policy 得出的管理员标记。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>若允许写入则为 <see langword="true" />。</returns>
    Task<bool> CanWriteRepositoryAsync(
        string repositoryName,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);
}
