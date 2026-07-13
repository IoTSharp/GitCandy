using System.Net;
using GitCandy.PullRequests;

namespace GitCandy.Integrations;

/// <summary>仓库 webhook 可订阅的版本化事件。</summary>
[Flags]
public enum WebhookEventTypes
{
    None = 0,
    Push = 1,
    PullRequestMerged = 2,
    CheckUpdated = 4,
    ReleasePublished = 8,
    All = Push | PullRequestMerged | CheckUpdated | ReleasePublished
}

/// <summary>持久化 webhook delivery 状态。</summary>
public enum WebhookDeliveryState
{
    Pending,
    InProgress,
    Succeeded,
    Failed
}

/// <summary>commit status 与 check run 的来源类型。</summary>
public enum CommitCheckKind
{
    Status,
    Check
}

/// <summary>required gate 使用的归一化 check 状态。</summary>
public enum CommitCheckState
{
    Pending,
    Running,
    Success,
    Failure,
    Error,
    Cancelled,
    Neutral,
    Skipped
}

/// <summary>创建 webhook subscription 的 owner 输入。</summary>
public sealed record CreateWebhookSubscription(
    string Name,
    string TargetUrl,
    WebhookEventTypes Events);

/// <summary>不包含 secret 的 webhook subscription 摘要。</summary>
public sealed record WebhookSubscriptionSummary(
    long Id,
    string Name,
    string TargetUrl,
    WebhookEventTypes Events,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>创建成功结果；Secret 只允许在当前响应显示一次。</summary>
public sealed record CreatedWebhookSubscription(
    WebhookSubscriptionSummary Subscription,
    string Secret);

/// <summary>owner 可查询的 webhook delivery 诊断摘要。</summary>
public sealed record WebhookDeliverySummary(
    string DeliveryId,
    string EventId,
    string EventType,
    WebhookDeliveryState State,
    int AttemptCount,
    DateTimeOffset? NextAttemptAt,
    int? ResponseStatusCode,
    string? ErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ReplayOfDeliveryId);

/// <summary>后台 delivery job 的持久化工作项。</summary>
public sealed record WebhookDeliveryWorkItem(
    string DeliveryId,
    string EventId,
    string EventType,
    Uri TargetUrl,
    string ProtectedSecret,
    string PayloadJson,
    int AttemptCount);

/// <summary>单次 webhook 网络发送结果。</summary>
public sealed record WebhookSendResult(
    bool Succeeded,
    int? ResponseStatusCode = null,
    string? ErrorCode = null);

/// <summary>push webhook 使用的有界 ref 快照。</summary>
public sealed record IntegrationReference(string Name, string TargetSha);

/// <summary>commit status/check 的幂等写入输入。</summary>
public sealed record CommitCheckUpdate(
    string Sha,
    CommitCheckKind Kind,
    string Context,
    CommitCheckState State,
    string Description,
    string? TargetUrl,
    string? ExternalId);

/// <summary>commit status/check 的 API 与 gate 摘要。</summary>
public sealed record CommitCheckSummary(
    long Id,
    string Sha,
    CommitCheckKind Kind,
    string Context,
    CommitCheckState State,
    string Description,
    string? TargetUrl,
    string? ExternalId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>repository owner 管理 webhook subscription 与 delivery 的边界。</summary>
public interface IWebhookService
{
    Task<IReadOnlyList<WebhookSubscriptionSummary>> GetSubscriptionsAsync(
        long repositoryId,
        CancellationToken cancellationToken = default);

    Task<CreatedWebhookSubscription?> CreateSubscriptionAsync(
        long repositoryId,
        string actorUserId,
        CreateWebhookSubscription command,
        CancellationToken cancellationToken = default);

    Task<bool> SetSubscriptionActiveAsync(
        long repositoryId,
        long subscriptionId,
        string actorUserId,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookDeliverySummary>> GetDeliveriesAsync(
        long repositoryId,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<string?> ReplayDeliveryAsync(
        long repositoryId,
        string deliveryId,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookDeliveryWorkItem>> ClaimDueDeliveriesAsync(
        int limit,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task CompleteDeliveryAttemptAsync(
        string deliveryId,
        WebhookSendResult result,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default);
}

/// <summary>成功业务操作向持久化 integration outbox 发布事件的边界。</summary>
public interface IIntegrationEventPublisher
{
    Task PublishPushAsync(
        string repositoryStorageName,
        string actorName,
        string repositoryStateId,
        IReadOnlyList<IntegrationReference> references,
        CancellationToken cancellationToken = default);

    Task PublishPullRequestMergedAsync(
        PullRequestMergedEvent mergedEvent,
        CancellationToken cancellationToken = default);

    Task PublishCheckUpdatedAsync(
        long repositoryId,
        string actorUserId,
        CommitCheckSummary check,
        CancellationToken cancellationToken = default);

    Task PublishReleasePublishedAsync(
        long repositoryId,
        string actorUserId,
        long releaseId,
        string tagName,
        string tagCommitSha,
        CancellationToken cancellationToken = default);
}

/// <summary>commit status/check 幂等写入与 required gate 查询边界。</summary>
public interface ICommitCheckService
{
    Task<CommitCheckSummary?> UpsertAsync(
        long repositoryId,
        string actorUserId,
        long? credentialId,
        CommitCheckUpdate update,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommitCheckSummary>> GetForCommitAsync(
        long repositoryId,
        string sha,
        CancellationToken cancellationToken = default);
}

/// <summary>重复 delivery 所需的可逆 webhook secret 保护边界。</summary>
public interface IWebhookSecretProtector
{
    string Protect(string secret);
    string Unprotect(string protectedSecret);
}

/// <summary>webhook 与 check target URL 共用的 SSRF 边界。</summary>
public interface IOutboundTargetPolicy
{
    ValueTask<bool> IsAllowedAsync(Uri target, CancellationToken cancellationToken = default);
    ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, CancellationToken cancellationToken = default);
}

/// <summary>受控 webhook HTTP sender。</summary>
public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(
        WebhookDeliveryWorkItem workItem,
        CancellationToken cancellationToken = default);
}
