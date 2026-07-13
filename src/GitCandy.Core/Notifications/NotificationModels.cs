using GitCandy.Workspace;

namespace GitCandy.Notifications;

/// <summary>统一通知可选的外部投递通道。</summary>
public enum NotificationDeliveryChannel
{
    Email,
    Webhook
}

/// <summary>通知外部投递的持久化状态。</summary>
public enum NotificationDeliveryState
{
    Pending,
    InProgress,
    Succeeded,
    Failed
}

/// <summary>当前用户对一种通知事件的投递偏好。</summary>
public sealed record NotificationPreference(
    WorkspaceNotificationEventType EventType,
    bool EmailEnabled,
    bool WebhookEnabled,
    string? WebhookUrl,
    DateTimeOffset UpdatedAt);

/// <summary>保存通知投递偏好的输入；webhook secret 为空时保留已有 secret。</summary>
public sealed record NotificationPreferenceEdit(
    WorkspaceNotificationEventType EventType,
    bool EmailEnabled,
    bool WebhookEnabled,
    string? WebhookUrl,
    string? WebhookSecret);

/// <summary>不暴露收件地址和 secret 的通知投递诊断。</summary>
public sealed record NotificationDeliveryDiagnostic(
    string DeliveryId,
    long NotificationId,
    WorkspaceNotificationEventType EventType,
    NotificationDeliveryChannel Channel,
    NotificationDeliveryState State,
    int AttemptCount,
    int? ResponseStatusCode,
    string? ErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>后台投递器领取的通知工作项。</summary>
public sealed record NotificationDeliveryWorkItem(
    string DeliveryId,
    NotificationDeliveryChannel Channel,
    string Recipient,
    string? ProtectedSecret,
    WorkspaceNotificationEventType EventType,
    string Title,
    string Url,
    int AttemptCount);

/// <summary>单次通知外部投递结果。</summary>
public sealed record NotificationDeliveryResult(
    bool Succeeded,
    int? ResponseStatusCode = null,
    string? ErrorCode = null);

/// <summary>用户通知偏好、持久化投递队列和失败诊断边界。</summary>
public interface INotificationDeliveryService
{
    Task<IReadOnlyList<NotificationPreference>> GetPreferencesAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<bool> SavePreferenceAsync(
        string userId,
        NotificationPreferenceEdit edit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationDeliveryDiagnostic>> GetDiagnosticsAsync(
        string userId,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationDeliveryWorkItem>> ClaimDueAsync(
        int limit,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task CompleteAttemptAsync(
        string deliveryId,
        NotificationDeliveryResult result,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default);
}

/// <summary>邮件和签名 webhook 的宿主侧通知投递器。</summary>
public interface INotificationDeliverySender
{
    Task<NotificationDeliveryResult> SendAsync(
        NotificationDeliveryWorkItem workItem,
        CancellationToken cancellationToken = default);
}
