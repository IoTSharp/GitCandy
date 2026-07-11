namespace GitCandy.Application;

/// <summary>
/// GitCandy 仓库领域查询和权限判断的应用服务入口。
/// </summary>
public interface IRepositoryService
{
    /// <summary>
    /// 按名称查找仓库摘要。
    /// </summary>
    /// <param name="repositoryName">仓库名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>仓库摘要；不存在时返回 <see langword="null" />。</returns>
    Task<RepositorySummary?> FindRepositoryAsync(
        string repositoryName,
        CancellationToken cancellationToken = default);

    /// <summary>按稳定 ID 查找仓库摘要。</summary>
    Task<RepositorySummary?> FindRepositoryAsync(
        long repositoryId,
        CancellationToken cancellationToken = default);

    /// <summary>按稳定 ID 判断用户是否可以读取仓库。</summary>
    Task<bool> CanReadRepositoryAsync(
        long repositoryId,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出当前用户可读取的仓库。
    /// </summary>
    /// <param name="userId">Identity 用户主键；匿名用户传入 <see langword="null" />。</param>
    /// <param name="isAdministrator">调用方根据 Identity role 或 policy 得出的管理员标记。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前用户可读取的仓库摘要。</returns>
    Task<IReadOnlyList<RepositorySummary>> GetVisibleRepositoriesAsync(
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);

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

    /// <summary>按稳定 ID 判断用户是否可以写入仓库。</summary>
    Task<bool> CanWriteRepositoryAsync(
        long repositoryId,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断用户是否具有仓库 owner 级权限。
    /// </summary>
    /// <param name="repositoryName">仓库名称。</param>
    /// <param name="userId">Identity 用户主键；匿名用户传入 <see langword="null" />。</param>
    /// <param name="isAdministrator">调用方根据 Identity role 或 policy 得出的管理员标记。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>若具有 owner 级权限则为 <see langword="true" />。</returns>
    Task<bool> IsRepositoryOwnerAsync(
        string repositoryName,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);

    /// <summary>按稳定 ID 判断用户是否具有 owner 级权限。</summary>
    Task<bool> IsRepositoryOwnerAsync(
        long repositoryId,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);
}
