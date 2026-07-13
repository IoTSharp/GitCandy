using System.Security.Cryptography;
using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitCandy.Application;

internal sealed class WebhookService(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    IWebhookSecretProtector secretProtector,
    IOutboundTargetPolicy targetPolicy,
    IOptions<WebhookOptions> options,
    TimeProvider timeProvider) : IWebhookService
{
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly IWebhookSecretProtector _secretProtector = secretProtector;
    private readonly IOutboundTargetPolicy _targetPolicy = targetPolicy;
    private readonly WebhookOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<IReadOnlyList<WebhookSubscriptionSummary>> GetSubscriptionsAsync(
        long repositoryId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var subscriptions = await dbContext.WebhookSubscriptions.AsNoTracking()
            .Where(item => item.RepositoryId == repositoryId)
            .OrderBy(item => item.Name)
            .ToArrayAsync(cancellationToken);
        return subscriptions.Select(ToSummary).ToArray();
    }

    public async Task<CreatedWebhookSubscription?> CreateSubscriptionAsync(
        long repositoryId,
        string actorUserId,
        CreateWebhookSubscription command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        ArgumentNullException.ThrowIfNull(command);
        var name = command.Name.Trim();
        var normalizedName = name.ToUpperInvariant();
        if (name.Length is 0 or > SchemaLimits.IntegrationName
            || command.Events == WebhookEventTypes.None
            || (command.Events & ~WebhookEventTypes.All) != 0
            || !Uri.TryCreate(command.TargetUrl, UriKind.Absolute, out var target)
            || !await _targetPolicy.IsAllowedAsync(target, cancellationToken))
        {
            return null;
        }

        var secret = CreateSecret();
        var now = _timeProvider.GetUtcNow();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await dbContext.Repositories.AsNoTracking().AnyAsync(item => item.Id == repositoryId, cancellationToken)
            || await dbContext.WebhookSubscriptions.AsNoTracking().CountAsync(
                item => item.RepositoryId == repositoryId,
                cancellationToken) >= _options.MaxSubscriptionsPerRepository
            || await dbContext.WebhookSubscriptions.AsNoTracking().AnyAsync(
                item => item.RepositoryId == repositoryId && item.NormalizedName == normalizedName,
                cancellationToken))
        {
            return null;
        }

        var subscription = new GitCandyWebhookSubscription
        {
            RepositoryId = repositoryId,
            Name = name,
            NormalizedName = normalizedName,
            TargetUrl = target.AbsoluteUri,
            ProtectedSecret = _secretProtector.Protect(secret),
            Events = command.Events,
            IsActive = true,
            CreatedAtUtc = now.UtcDateTime,
            UpdatedAtUtc = now.UtcDateTime
        };
        dbContext.WebhookSubscriptions.Add(subscription);
        AddAudit(dbContext, repositoryId, actorUserId, "webhook.create", "success", target.Host, now);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return null;
        }
        return new CreatedWebhookSubscription(ToSummary(subscription), secret);
    }

    public async Task<bool> SetSubscriptionActiveAsync(
        long repositoryId,
        long subscriptionId,
        string actorUserId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        var now = _timeProvider.GetUtcNow();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var subscription = await dbContext.WebhookSubscriptions.SingleOrDefaultAsync(
            item => item.Id == subscriptionId && item.RepositoryId == repositoryId,
            cancellationToken);
        if (subscription is null) return false;
        subscription.IsActive = isActive;
        subscription.UpdatedAtUtc = now.UtcDateTime;
        AddAudit(dbContext, repositoryId, actorUserId, isActive ? "webhook.enable" : "webhook.disable", "success", string.Empty, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<WebhookDeliverySummary>> GetDeliveriesAsync(
        long repositoryId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = Math.Clamp(limit, 1, 200);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await (
            from delivery in dbContext.WebhookDeliveries.AsNoTracking()
            join subscription in dbContext.WebhookSubscriptions.AsNoTracking()
                on delivery.SubscriptionId equals subscription.Id
            join integrationEvent in dbContext.IntegrationEvents.AsNoTracking()
                on delivery.EventId equals integrationEvent.Id
            where subscription.RepositoryId == repositoryId
            orderby delivery.CreatedAtUtc descending
            select new WebhookDeliverySummary(
                delivery.Id,
                delivery.EventId,
                integrationEvent.Type,
                delivery.State,
                delivery.AttemptCount,
                ToDateTimeOffset(delivery.NextAttemptAtUtc),
                delivery.ResponseStatusCode,
                delivery.ErrorCode,
                ToDateTimeOffset(delivery.CreatedAtUtc)!.Value,
                ToDateTimeOffset(delivery.CompletedAtUtc),
                delivery.ReplayOfDeliveryId))
            .Take(boundedLimit)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<string?> ReplayDeliveryAsync(
        long repositoryId,
        string deliveryId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        var now = _timeProvider.GetUtcNow();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var original = await dbContext.WebhookDeliveries.AsNoTracking()
            .Include(item => item.Subscription)
            .SingleOrDefaultAsync(item => item.Id == deliveryId
                && item.Subscription!.RepositoryId == repositoryId
                && item.Subscription.IsActive,
                cancellationToken);
        if (original is null) return null;
        var replayId = Guid.NewGuid().ToString("N");
        dbContext.WebhookDeliveries.Add(new GitCandyWebhookDelivery
        {
            Id = replayId,
            SubscriptionId = original.SubscriptionId,
            EventId = original.EventId,
            State = WebhookDeliveryState.Pending,
            NextAttemptAtUtc = now.UtcDateTime,
            CreatedAtUtc = now.UtcDateTime,
            ReplayOfDeliveryId = original.Id
        });
        AddAudit(dbContext, repositoryId, actorUserId, "webhook.replay", "success", replayId, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return replayId;
    }

    public async Task<IReadOnlyList<WebhookDeliveryWorkItem>> ClaimDueDeliveriesAsync(
        int limit,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0 || leaseDuration <= TimeSpan.Zero) return [];
        var now = _timeProvider.GetUtcNow();
        var leaseUntil = now.Add(leaseDuration).UtcDateTime;
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await (
            from delivery in dbContext.WebhookDeliveries.AsNoTracking()
            join subscription in dbContext.WebhookSubscriptions.AsNoTracking()
                on delivery.SubscriptionId equals subscription.Id
            where subscription.IsActive
                && ((delivery.State == WebhookDeliveryState.Pending
                        && delivery.NextAttemptAtUtc <= now.UtcDateTime)
                    || (delivery.State == WebhookDeliveryState.InProgress
                        && delivery.LeaseExpiresAtUtc <= now.UtcDateTime))
            orderby delivery.NextAttemptAtUtc, delivery.CreatedAtUtc
            select delivery.Id)
            .Take(Math.Clamp(limit, 1, 200))
            .ToArrayAsync(cancellationToken);

        var claimed = new List<string>(candidates.Length);
        foreach (var id in candidates)
        {
            var updated = await dbContext.WebhookDeliveries
                .Where(item => item.Id == id
                    && ((item.State == WebhookDeliveryState.Pending
                            && item.NextAttemptAtUtc <= now.UtcDateTime)
                        || (item.State == WebhookDeliveryState.InProgress
                            && item.LeaseExpiresAtUtc <= now.UtcDateTime)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.State, WebhookDeliveryState.InProgress)
                    .SetProperty(item => item.LeaseExpiresAtUtc, leaseUntil)
                    .SetProperty(item => item.LastAttemptAtUtc, now.UtcDateTime)
                    .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1),
                    cancellationToken);
            if (updated == 1) claimed.Add(id);
        }
        if (claimed.Count == 0) return [];

        return await (
            from delivery in dbContext.WebhookDeliveries.AsNoTracking()
            join subscription in dbContext.WebhookSubscriptions.AsNoTracking()
                on delivery.SubscriptionId equals subscription.Id
            join integrationEvent in dbContext.IntegrationEvents.AsNoTracking()
                on delivery.EventId equals integrationEvent.Id
            where claimed.Contains(delivery.Id)
            select new WebhookDeliveryWorkItem(
                delivery.Id,
                integrationEvent.Id,
                integrationEvent.Type,
                new Uri(subscription.TargetUrl),
                subscription.ProtectedSecret,
                integrationEvent.PayloadJson,
                delivery.AttemptCount))
            .ToArrayAsync(cancellationToken);
    }

    public async Task CompleteDeliveryAttemptAsync(
        string deliveryId,
        WebhookSendResult result,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);
        ArgumentNullException.ThrowIfNull(result);
        var now = _timeProvider.GetUtcNow();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var delivery = await dbContext.WebhookDeliveries.SingleOrDefaultAsync(
            item => item.Id == deliveryId && item.State == WebhookDeliveryState.InProgress,
            cancellationToken);
        if (delivery is null) return;
        delivery.ResponseStatusCode = result.ResponseStatusCode;
        delivery.ErrorCode = result.ErrorCode;
        delivery.LeaseExpiresAtUtc = null;
        if (result.Succeeded)
        {
            delivery.State = WebhookDeliveryState.Succeeded;
            delivery.NextAttemptAtUtc = null;
            delivery.CompletedAtUtc = now.UtcDateTime;
        }
        else if (nextAttemptAt is null)
        {
            delivery.State = WebhookDeliveryState.Failed;
            delivery.NextAttemptAtUtc = null;
            delivery.CompletedAtUtc = now.UtcDateTime;
        }
        else
        {
            delivery.State = WebhookDeliveryState.Pending;
            delivery.NextAttemptAtUtc = nextAttemptAt.Value.UtcDateTime;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static WebhookSubscriptionSummary ToSummary(GitCandyWebhookSubscription subscription) =>
        new(
            subscription.Id,
            subscription.Name,
            subscription.TargetUrl,
            subscription.Events,
            subscription.IsActive,
            ToDateTimeOffset(subscription.CreatedAtUtc)!.Value,
            ToDateTimeOffset(subscription.UpdatedAtUtc)!.Value);

    private static string CreateSecret()
    {
        return "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value) => value is null
        ? null
        : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

    private static void AddAudit(
        GitCandyDbContext dbContext,
        long repositoryId,
        string actorUserId,
        string action,
        string outcome,
        string detail,
        DateTimeOffset occurredAt)
    {
        dbContext.GovernanceAuditEvents.Add(new GitCandyGovernanceAuditEvent
        {
            RepositoryId = repositoryId,
            ActorUserId = actorUserId,
            Action = action,
            Outcome = outcome,
            ReferenceName = string.Empty,
            Detail = detail.Length <= SchemaLimits.AuditDetail ? detail : detail[..SchemaLimits.AuditDetail],
            OccurredAtUtc = occurredAt.UtcDateTime
        });
    }
}
