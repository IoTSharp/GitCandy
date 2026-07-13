namespace GitCandy.Configuration;

/// <summary>webhook delivery、重试和 SSRF 策略。</summary>
public sealed class WebhookOptions
{
    public const string SectionName = "GitCandy:Webhooks";

    public bool AllowHttpTargets { get; set; }
    public bool AllowPrivateNetworkTargets { get; set; }
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxAttempts { get; set; } = 6;
    public int DeliveryBatchSize { get; set; } = 20;
    public int MaxResponseBytes { get; set; } = 4096;
    public int MaxSubscriptionsPerRepository { get; set; } = 20;
}
