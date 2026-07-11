namespace GitCandy.Application;

/// <summary>
/// 仓库元数据 CRUD 和协作者管理入口。
/// </summary>
public interface IRepositoryManagementService
{
    /// <summary>读取仓库元数据和授权关系。</summary>
    Task<RepositoryDetails?> GetRepositoryAsync(
        string repositoryName,
        CancellationToken cancellationToken = default);

    /// <summary>按稳定 ID 读取仓库元数据和授权关系。</summary>
    Task<RepositoryDetails?> GetRepositoryAsync(
        long repositoryId,
        CancellationToken cancellationToken = default);

    /// <summary>创建仓库元数据，并将创建者设为 owner。</summary>
    Task<bool> CreateRepositoryAsync(
        RepositoryEdit command,
        string creatorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>更新仓库可见性和说明。</summary>
    Task<bool> UpdateRepositoryAsync(
        string repositoryName,
        RepositoryEdit command,
        CancellationToken cancellationToken = default);

    /// <summary>删除仓库元数据和级联授权关系。</summary>
    Task<bool> DeleteRepositoryAsync(
        string repositoryName,
        CancellationToken cancellationToken = default);

    /// <summary>添加、移除或调整用户仓库角色。</summary>
    Task<bool> SetUserRoleAsync(
        string repositoryName,
        string userName,
        RepositoryUserRoleAction action,
        bool value,
        CancellationToken cancellationToken = default);

    /// <summary>添加、移除或调整团队仓库角色。</summary>
    Task<bool> SetTeamRoleAsync(
        string repositoryName,
        string teamName,
        RepositoryTeamRoleAction action,
        bool value,
        CancellationToken cancellationToken = default);
}

/// <summary>仓库可编辑字段。</summary>
public sealed record RepositoryEdit(
    string Name,
    string Description,
    bool IsPrivate,
    bool AllowAnonymousRead,
    bool AllowAnonymousWrite,
    string? ForkedFromRepository = null,
    string? ForkNetworkRoot = null,
    string? NamespaceSlug = null,
    string? StorageName = null,
    long? ForkedFromRepositoryId = null,
    long? ForkNetworkRootRepositoryId = null);

/// <summary>用户仓库角色摘要。</summary>
public sealed record RepositoryUserRoleSummary(
    string UserName,
    bool AllowRead,
    bool AllowWrite,
    bool IsOwner);

/// <summary>团队仓库角色摘要。</summary>
public sealed record RepositoryTeamRoleSummary(string TeamName, bool AllowRead, bool AllowWrite);

/// <summary>仓库元数据和授权关系。</summary>
public sealed record RepositoryDetails(
    long Id,
    long NamespaceId,
    string NamespaceSlug,
    string Name,
    string StorageName,
    string Description,
    bool IsPrivate,
    bool AllowAnonymousRead,
    bool AllowAnonymousWrite,
    DateTime CreatedAtUtc,
    string? ForkedFromRepository,
    string? ForkNetworkRoot,
    IReadOnlyList<RepositoryUserRoleSummary> Users,
    IReadOnlyList<RepositoryTeamRoleSummary> Teams,
    long? ForkedFromRepositoryId = null,
    long? ForkNetworkRootRepositoryId = null);

/// <summary>用户仓库角色变更动作。</summary>
public enum RepositoryUserRoleAction
{
    Add,
    Remove,
    SetRead,
    SetWrite,
    SetOwner
}

/// <summary>团队仓库角色变更动作。</summary>
public enum RepositoryTeamRoleAction
{
    Add,
    Remove,
    SetRead,
    SetWrite
}
