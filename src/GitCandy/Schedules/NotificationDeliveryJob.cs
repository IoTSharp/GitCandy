using GitCandy.Configuration;
using GitCandy.Notifications;
using Microsoft.Extensions.Options;

namespace GitCandy.Schedules;

/// <summary>投递持久化用户通知，并使用有界退避处理临时失败。</summary>
public sealed class NotificationDeliveryJob(
    INotificationDeliveryService deliveryService,
    INotificationDeliverySender sender,
    IOptions<WebhookOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationDeliveryJob> logger) : ISchedulerJob
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromHours(1)
    ];
    private readonly INotificationDeliveryService _deliveryService = deliveryService;
    private readonly INotificationDeliverySender _sender = sender;
    private readonly WebhookOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<NotificationDeliveryJob> _logger = logger;

    public string Name => "notification-delivery";
    public SchedulerJobType JobType => SchedulerJobType.RealTime;

    public async ValueTask ExecuteAsync(SchedulerJobContext context, CancellationToken cancellationToken = default)
    {
        var items = await _deliveryService.ClaimDueAsync(
            _options.DeliveryBatchSize,
            _options.RequestTimeout + TimeSpan.FromSeconds(15),
            cancellationToken);
        foreach (var item in items)
        {
            var result = await _sender.SendAsync(item, cancellationToken);
            var nextAttempt = !result.Succeeded && item.AttemptCount < _options.MaxAttempts
                ? _timeProvider.GetUtcNow().Add(RetryDelays[Math.Clamp(item.AttemptCount - 1, 0, RetryDelays.Length - 1)])
                : (DateTimeOffset?)null;
            await _deliveryService.CompleteAttemptAsync(item.DeliveryId, result, nextAttempt, cancellationToken);
            if (!result.Succeeded)
            {
                _logger.LogWarning(
                    "Notification delivery {DeliveryId} failed with {ErrorCode}; retry scheduled: {RetryScheduled}.",
                    item.DeliveryId,
                    result.ErrorCode,
                    nextAttempt is not null);
            }
        }
    }

    public TimeSpan GetNextInterval(SchedulerJobContext context) => TimeSpan.FromSeconds(10);
}
