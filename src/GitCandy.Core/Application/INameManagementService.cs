namespace GitCandy.Application;

/// <summary>
/// 用户、团队和仓库 slug 的原子改名及 alias 生命周期入口。
/// </summary>
public interface INameManagementService
{
    /// <summary>读取 namespace 改名配额和历史 alias。</summary>
    Task<NameManagementSnapshot?> GetNamespaceSnapshotAsync(
        string namespaceSlug,
        CancellationToken cancellationToken = default);

    /// <summary>读取仓库历史 alias。</summary>
    Task<NameManagementSnapshot?> GetRepositorySnapshotAsync(
        long repositoryId,
        CancellationToken cancellationToken = default);

    /// <summary>原子修改用户或团队 namespace slug。</summary>
    Task<NameChangeResult> RenameNamespaceAsync(
        long namespaceId,
        string newSlug,
        string actorUserId,
        NameChangeOverride? changeOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>原子修改仓库 slug。</summary>
    Task<NameChangeResult> RenameRepositoryAsync(
        long repositoryId,
        string newSlug,
        string actorUserId,
        NameChangeOverride? changeOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>延长一个历史 alias 的保留时间。</summary>
    Task<bool> ExtendAliasAsync(
        NameSubjectType subjectType,
        long aliasId,
        DateTime expiresAtUtc,
        string actorUserId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>释放所有已到期 alias；操作可重复执行。</summary>
    Task<int> ReleaseExpiredAliasesAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}

/// <summary>改名主体类型。</summary>
public enum NameSubjectType
{
    Namespace = 1,
    Repository = 2
}

/// <summary>灾难恢复改名覆盖参数。</summary>
public sealed record NameChangeOverride(string Reason, bool Confirmed);

/// <summary>改名结果状态。</summary>
public enum NameChangeStatus
{
    Succeeded,
    NotFound,
    InvalidSlug,
    Reserved,
    Occupied,
    RateLimited,
    ConfirmationRequired,
    Conflict
}

/// <summary>原子改名结果。</summary>
public sealed record NameChangeResult(
    NameChangeStatus Status,
    string? CanonicalSlug = null,
    DateTime? AliasExpiresAtUtc = null,
    int? RemainingRenames = null);

/// <summary>管理页展示的当前名称、配额和 alias 快照。</summary>
public sealed record NameManagementSnapshot(
    long SubjectId,
    string CurrentSlug,
    int? RemainingRenames,
    DateTime? RenameWindowStartsAtUtc,
    IReadOnlyList<NameAliasSummary> Aliases);

/// <summary>历史 alias 摘要。</summary>
public sealed record NameAliasSummary(
    long Id,
    string Slug,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? ReleasedAtUtc);
