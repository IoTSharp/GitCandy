namespace GitCandy.Governance;

/// <summary>保护分支允许操作的最低仓库权限。</summary>
public enum BranchAccessLevel
{
    RepositoryWrite = 0,
    RepositoryOwner = 1,
    Nobody = 2
}

/// <summary>保护分支规则的可编辑值。</summary>
public sealed record BranchProtectionEdit(
    long? Id,
    string Pattern,
    BranchAccessLevel PushAccess,
    BranchAccessLevel MergeAccess,
    bool AllowForcePushes,
    bool AllowDeletions,
    bool AllowAdministratorBypass,
    IReadOnlyList<string>? RequiredChecks = null,
    int RequiredApprovals = 0,
    bool RequireCodeOwnerReviews = false,
    bool DismissStaleApprovals = true);

/// <summary>保护分支规则摘要。</summary>
public sealed record BranchProtectionSummary(
    long Id,
    string Pattern,
    BranchAccessLevel PushAccess,
    BranchAccessLevel MergeAccess,
    bool AllowForcePushes,
    bool AllowDeletions,
    bool AllowAdministratorBypass,
    IReadOnlyList<string> RequiredChecks,
    int RequiredApprovals,
    bool RequireCodeOwnerReviews,
    bool DismissStaleApprovals,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>保护分支规则管理边界。</summary>
public interface IBranchProtectionService
{
    Task<IReadOnlyList<BranchProtectionSummary>> GetForRepositoryAsync(
        long repositoryId,
        CancellationToken cancellationToken = default);

    Task<BranchProtectionSummary?> SaveAsync(
        long repositoryId,
        string actorUserId,
        BranchProtectionEdit edit,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        long repositoryId,
        long ruleId,
        string actorUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>push gate 的调用来源。</summary>
public enum GitRefOperation
{
    Push,
    Merge,
    WebDelete
}

/// <summary>push 或 Web merge 的用户/机器操作者。</summary>
public sealed record GitRefActor(
    string Name,
    string? UserId = null,
    long? DeployKeyId = null);

/// <summary>单个 Git ref 更新命令。</summary>
public sealed record GitRefUpdate(
    string OldObjectId,
    string NewObjectId,
    string ReferenceName,
    bool IsForceUpdate = false)
{
    public bool IsDelete => NewObjectId.All(static character => character == '0');
}

/// <summary>统一 push/merge gate 输入。</summary>
public sealed record GitPushGateRequest(
    long RepositoryId,
    GitRefActor Actor,
    GitRefOperation Operation,
    IReadOnlyList<GitRefUpdate> Updates,
    GitPushReviewContext? Review = null,
    bool RecordAudit = true,
    bool EvaluateAccess = true);

/// <summary>Web merge 提供给统一 gate 的不可变 PR 与 changed-path owner 上下文。</summary>
public sealed record GitPushReviewContext(
    long PullRequestNumber,
    CodeOwnersSnapshot CodeOwners);

/// <summary>统一 gate 计算出的有效批准与 CODEOWNERS 状态。</summary>
public sealed record GitReviewGateStatus(
    int EffectiveApprovals,
    int RequiredApprovals,
    bool CodeOwnerReviewsRequired,
    bool CodeOwnerReviewsSatisfied);

/// <summary>push gate 结果；Reasons 可直接作为 Git remote 错误，但不包含私有路径或 secret。</summary>
public sealed record GitPushGateResult(
    bool Allowed,
    IReadOnlyList<string> Reasons,
    bool RequiredChecksSatisfied = true,
    GitReviewGateStatus? Review = null)
{
    public static GitPushGateResult Allow { get; } = new(true, []);
}

/// <summary>Git HTTP、SSH 与 Web merge 共享的保护分支判定边界。</summary>
public interface IGitPushGate
{
    Task<GitPushGateResult> EvaluateAsync(
        GitPushGateRequest request,
        CancellationToken cancellationToken = default);
}
