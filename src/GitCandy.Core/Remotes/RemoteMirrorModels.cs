namespace GitCandy.Remotes;

/// <summary>Mirror 应用服务使用的稳定错误码。</summary>
public static class RemoteMirrorErrorCodes
{
    public const string NotFound = "mirror_not_found";
    public const string AccessDenied = "mirror_access_denied";
    public const string Disabled = "mirror_disabled";
    public const string InvalidConfiguration = "mirror_invalid_configuration";
    public const string ProviderUnavailable = "mirror_provider_unavailable";
    public const string CredentialUnavailable = "mirror_credential_unavailable";
    public const string ScopeMissing = "mirror_scope_missing";
    public const string Diverged = "mirror_ref_diverged";
    public const string Canceled = "mirror_canceled";
    public const string LeaseExpired = "mirror_lease_expired";
}

/// <summary>触发持久化 mirror job 的原因；同一 mirror 的重复触发会合并。</summary>
[Flags]
public enum RemoteMirrorJobTrigger
{
    None = 0,
    Initial = 1,
    Schedule = 2,
    Push = 4,
    Webhook = 8,
    Manual = 16,
    Recovery = 32
}

/// <summary>持久化 mirror job 的生命周期状态。</summary>
public enum RemoteMirrorJobState
{
    Pending,
    Leased,
    Succeeded,
    Failed,
    Canceled
}

/// <summary>一次由 worker 持有的 mirror job 租约。</summary>
public sealed record RemoteMirrorJobLease(
    long JobId,
    long MirrorId,
    long RequestedGeneration,
    int AttemptCount,
    RemoteMirrorJobTrigger Triggers,
    DateTimeOffset LeaseExpiresAt);

/// <summary>可供仓库 owner 运维查看的 mirror job 脱敏投影。</summary>
public sealed record RemoteMirrorJobSummary(
    long JobId,
    long MirrorId,
    RemoteMirrorJobState State,
    RemoteMirrorJobTrigger Triggers,
    int AttemptCount,
    DateTimeOffset AvailableAt,
    DateTimeOffset? LeaseExpiresAt,
    bool CancellationRequested,
    string? LastErrorCode,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt,
    DateTimeOffset UpdatedAt);

/// <summary>worker 将一次租约执行结果提交给持久化队列时使用的结果。</summary>
public sealed record RemoteMirrorJobCompletion(
    long RequestedGeneration,
    bool Succeeded,
    bool Retry,
    bool Canceled,
    string? ErrorCode,
    TimeSpan RetryDelay);

/// <summary>EF 持久化 mirror job 队列；Quartz 只负责唤醒消费。</summary>
public interface IRemoteMirrorJobQueue
{
    Task EnqueueAsync(
        long mirrorId,
        RemoteMirrorJobTrigger trigger,
        DateTimeOffset? availableAt = null,
        CancellationToken cancellationToken = default);

    Task<int> EnqueueDuePullMirrorsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<int> EnqueueRecoveryCandidatesAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemoteMirrorJobLease>> AcquireAsync(
        string leaseOwner,
        int limit,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        long jobId,
        string leaseOwner,
        RemoteMirrorJobCompletion completion,
        CancellationToken cancellationToken = default);

    Task ReleaseAsync(
        long jobId,
        string leaseOwner,
        CancellationToken cancellationToken = default);

    Task<bool> RequestCancellationAsync(
        long jobId,
        CancellationToken cancellationToken = default);

    Task<bool> RetryAsync(
        long jobId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemoteMirrorJobSummary>> GetForRepositoryAsync(
        long repositoryId,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>同进程 mirror worker 的持久化队列消费边界。</summary>
public interface IRemoteMirrorJobDispatcher
{
    Task<IReadOnlyList<RemoteMirrorOperationResult>> RunReadyAsync(
        CancellationToken cancellationToken = default);

    bool CancelActive(long jobId);
}

/// <summary>注册单向仓库 mirror 的结构化配置。</summary>
public sealed record RemoteMirrorRegistration(
    long RepositoryId,
    long ConnectionId,
    RemoteRepositoryProfile RemoteRepository,
    RemoteMirrorDirection Direction,
    RemoteMirrorRefFilterKind RefFilterKind,
    string? RefFilterPattern,
    int? ScheduleIntervalMinutes,
    string? ScheduleTimeZone,
    bool ScheduleEnabled,
    RemoteMirrorDivergencePolicy DivergencePolicy,
    bool PropagateDeletes,
    bool IsEnabled = true);

/// <summary>仓库 mirror 的可运维脱敏投影。</summary>
public sealed record RemoteMirrorSummary(
    long Id,
    long RepositoryId,
    long ConnectionId,
    RemoteProviderKind Provider,
    string RemoteRepositoryId,
    string RemoteFullName,
    Uri RemoteGitUrl,
    RemoteMirrorDirection Direction,
    RemoteMirrorRefFilterKind RefFilterKind,
    string? RefFilterPattern,
    int? ScheduleIntervalMinutes,
    bool ScheduleEnabled,
    RemoteMirrorDivergencePolicy DivergencePolicy,
    bool Prune,
    bool IsEnabled,
    RemoteMirrorStatus Status,
    string? LastErrorCode,
    DateTimeOffset? LastAttemptedAt,
    DateTimeOffset? LastSucceededAt,
    DateTimeOffset UpdatedAt);

/// <summary>仓库 owner 使用的 mirror 注册、暂停、重试、取消和删除边界。</summary>
public interface IRemoteMirrorManagementService
{
    Task<IReadOnlyList<RemoteMirrorSummary>> GetForRepositoryAsync(
        long repositoryId,
        CancellationToken cancellationToken = default);

    Task<bool> SetPausedAsync(
        long repositoryId,
        long mirrorId,
        string actorUserId,
        bool paused,
        CancellationToken cancellationToken = default);

    Task<bool> EnqueueAsync(
        long repositoryId,
        long mirrorId,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<bool> CancelAsync(
        long repositoryId,
        long jobId,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<bool> RetryAsync(
        long repositoryId,
        long jobId,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        long repositoryId,
        long mirrorId,
        string actorUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>一次 mirror 注册或同步操作的安全结果。</summary>
public sealed record RemoteMirrorOperationResult(
    long? MirrorId,
    bool Succeeded,
    RemoteMirrorStatus Status,
    string? ErrorCode = null,
    int UpdatedReferenceCount = 0,
    int SkippedReferenceCount = 0);

/// <summary>成功 receive-pack 产生的单个 ref 更新。</summary>
public sealed record RemoteMirrorRefEvent(
    string OldObjectId,
    string NewObjectId,
    string ReferenceName)
{
    public bool IsDelete => NewObjectId.All(static character => character == '0');
}

/// <summary>成功 push 后只负责持久化和合并 ref 事件的短路径边界。</summary>
public interface IRemoteMirrorPushEventSink
{
    Task EnqueueAsync(
        long repositoryId,
        IReadOnlyList<RemoteMirrorRefEvent> updates,
        CancellationToken cancellationToken = default);
}

/// <summary>Pull/Push mirror 注册、执行和远端可变资料更新的应用边界。</summary>
public interface IRemoteMirrorService
{
    Task<RemoteMirrorOperationResult> RegisterAsync(
        string actorUserId,
        RemoteMirrorRegistration registration,
        CancellationToken cancellationToken = default);

    Task<RemoteMirrorOperationResult> SynchronizeAsync(
        long mirrorId,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateRemoteProfileAsync(
        long mirrorId,
        RemoteRepositoryProfile remoteRepository,
        CancellationToken cancellationToken = default);
}
