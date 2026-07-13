using GitCandy.Configuration;
using GitCandy.Integrations;
using Microsoft.Extensions.Options;

namespace GitCandy.Schedules;

/// <summary>从持久化 outbox 投递 webhook，并按有界退避重试。</summary>
public sealed class WebhookDeliveryJob(
    IWebhookService webhookService,
    IWebhookSender sender,
    IOptions<WebhookOptions> options,
    TimeProvider timeProvider,
    ILogger<WebhookDeliveryJob> logger) : ISchedulerJob
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2)
    ];
    private readonly IWebhookService _webhookService = webhookService;
    private readonly IWebhookSender _sender = sender;
    private readonly WebhookOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<WebhookDeliveryJob> _logger = logger;

    public string Name => "webhook-delivery";

    public SchedulerJobType JobType => SchedulerJobType.RealTime;

    public async ValueTask ExecuteAsync(
        SchedulerJobContext context,
        CancellationToken cancellationToken = default)
    {
        var workItems = await _webhookService.ClaimDueDeliveriesAsync(
            _options.DeliveryBatchSize,
            _options.RequestTimeout + TimeSpan.FromSeconds(15),
            cancellationToken);
        foreach (var workItem in workItems)
        {
            var result = await _sender.SendAsync(workItem, cancellationToken);
            var nextAttemptAt = !result.Succeeded && workItem.AttemptCount < _options.MaxAttempts
                ? _timeProvider.GetUtcNow().Add(GetRetryDelay(workItem.AttemptCount))
                : (DateTimeOffset?)null;
            await _webhookService.CompleteDeliveryAttemptAsync(
                workItem.DeliveryId,
                result,
                nextAttemptAt,
                cancellationToken);
            if (!result.Succeeded)
            {
                _logger.LogWarning(
                    "Webhook delivery {DeliveryId} attempt {AttemptCount} failed with {ErrorCode}; retry scheduled: {RetryScheduled}.",
                    workItem.DeliveryId,
                    workItem.AttemptCount,
                    result.ErrorCode,
                    nextAttemptAt is not null);
            }
        }
    }

    public TimeSpan GetNextInterval(SchedulerJobContext context) => TimeSpan.FromSeconds(5);

    private static TimeSpan GetRetryDelay(int attemptCount)
    {
        var index = Math.Clamp(attemptCount - 1, 0, RetryDelays.Length - 1);
        return RetryDelays[index];
    }
}
