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

    Task<IReadOnlyList<RemoteMirrorOperationResult>> SynchronizeDuePullMirrorsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemoteMirrorOperationResult>> ProcessPendingPushMirrorsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateRemoteProfileAsync(
        long mirrorId,
        RemoteRepositoryProfile remoteRepository,
        CancellationToken cancellationToken = default);
}
