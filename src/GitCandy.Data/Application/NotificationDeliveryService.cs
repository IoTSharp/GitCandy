using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Integrations;
using GitCandy.Notifications;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed class NotificationDeliveryService(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    IWebhookSecretProtector secretProtector,
    IOutboundTargetPolicy targetPolicy,
    TimeProvider timeProvider) : INotificationDeliveryService
{
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly IWebhookSecretProtector _secretProtector = secretProtector;
    private readonly IOutboundTargetPolicy _targetPolicy = targetPolicy;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<IReadOnlyList<NotificationPreference>> GetPreferencesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var stored = await dbContext.NotificationPreferences.AsNoTracking()
            .Where(item => item.UserId == userId)
            .ToDictionaryAsync(item => item.EventType, cancellationToken);
        return Enum.GetValues<Workspace.WorkspaceNotificationEventType>()
            .Select(eventType => stored.TryGetValue(eventType, out var item)
                ? new NotificationPreference(
                    eventType,
                    item.EmailEnabled,
                    item.WebhookEnabled,
                    item.WebhookUrl,
                    ToDateTimeOffset(item.UpdatedAtUtc))
                : new NotificationPreference(eventType, false, false, null, DateTimeOffset.MinValue))
            .ToArray();
    }

    public async Task<bool> SavePreferenceAsync(
        string userId,
        NotificationPreferenceEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(edit);
        Uri? target = null;
        if (edit.WebhookEnabled
            && (!Uri.TryCreate(edit.WebhookUrl, UriKind.Absolute, out target)
                || target.AbsoluteUri.Length > SchemaLimits.TargetUrl
                || edit.WebhookSecret?.Trim().Length > 512
                || !await _targetPolicy.IsAllowedAsync(target, cancellationToken)))
        {
            return false;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await dbContext.Users.AsNoTracking().AnyAsync(item => item.Id == userId, cancellationToken))
        {
            return false;
        }
        var item = await dbContext.NotificationPreferences.SingleOrDefaultAsync(
            value => value.UserId == userId && value.EventType == edit.EventType,
            cancellationToken);
        if (edit.WebhookEnabled && string.IsNullOrWhiteSpace(edit.WebhookSecret)
            && string.IsNullOrWhiteSpace(item?.ProtectedWebhookSecret))
        {
            return false;
        }
        item ??= new GitCandyNotificationPreference
        {
            UserId = userId,
            EventType = edit.EventType
        };
        if (dbContext.Entry(item).State == EntityState.Detached)
        {
            dbContext.NotificationPreferences.Add(item);
        }
        item.EmailEnabled = edit.EmailEnabled;
        item.WebhookEnabled = edit.WebhookEnabled;
        item.WebhookUrl = edit.WebhookEnabled ? target!.AbsoluteUri : null;
        item.ProtectedWebhookSecret = edit.WebhookEnabled
            ? string.IsNullOrWhiteSpace(edit.WebhookSecret)
                ? item.ProtectedWebhookSecret
                : _secretProtector.Protect(edit.WebhookSecret.Trim())
            : null;
        item.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<NotificationDeliveryDiagnostic>> GetDiagnosticsAsync(
        string userId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await (
            from delivery in dbContext.NotificationDeliveries.AsNoTracking()
            join notification in dbContext.Notifications.AsNoTracking()
                on delivery.NotificationId equals notification.Id
            where notification.UserId == userId
            orderby delivery.CreatedAtUtc descending
            select new NotificationDeliveryDiagnostic(
                delivery.Id,
                notification.Id,
                notification.EventType,
                delivery.Channel,
                delivery.State,
                delivery.AttemptCount,
                delivery.ResponseStatusCode,
                delivery.ErrorCode,
                ToDateTimeOffset(delivery.CreatedAtUtc),
                delivery.CompletedAtUtc == null ? null : ToDateTimeOffset(delivery.CompletedAtUtc.Value)))
            .Take(Math.Clamp(limit, 1, 200))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationDeliveryWorkItem>> ClaimDueAsync(
        int limit,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0 || leaseDuration <= TimeSpan.Zero) return [];
        var now = _timeProvider.GetUtcNow();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await (
            from delivery in dbContext.NotificationDeliveries.AsNoTracking()
            join notification in dbContext.Notifications.AsNoTracking()
                on delivery.NotificationId equals notification.Id
            where (delivery.State == NotificationDeliveryState.Pending
                    && delivery.NextAttemptAtUtc <= now.UtcDateTime)
                || (delivery.State == NotificationDeliveryState.InProgress
                    && delivery.LeaseExpiresAtUtc <= now.UtcDateTime)
            orderby delivery.NextAttemptAtUtc, delivery.CreatedAtUtc
            select new
            {
                delivery.Id,
                notification.UserId,
                notification.RepositoryId,
                notification.TeamId
            })
            .Take(Math.Clamp(limit, 1, 200))
            .ToArrayAsync(cancellationToken);

        var claimed = new List<string>(candidates.Length);
        foreach (var candidate in candidates)
        {
            if (!await CanStillReadAsync(
                    dbContext,
                    candidate.UserId,
                    candidate.RepositoryId,
                    candidate.TeamId,
                    cancellationToken))
            {
                await dbContext.NotificationDeliveries.Where(item => item.Id == candidate.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.State, NotificationDeliveryState.Failed)
                        .SetProperty(item => item.ErrorCode, "permission_revoked")
                        .SetProperty(item => item.CompletedAtUtc, now.UtcDateTime)
                        .SetProperty(item => item.NextAttemptAtUtc, (DateTime?)null)
                        .SetProperty(item => item.LeaseExpiresAtUtc, (DateTime?)null),
                        cancellationToken);
                continue;
            }

            var updated = await dbContext.NotificationDeliveries
                .Where(item => item.Id == candidate.Id
                    && ((item.State == NotificationDeliveryState.Pending
                            && item.NextAttemptAtUtc <= now.UtcDateTime)
                        || (item.State == NotificationDeliveryState.InProgress
                            && item.LeaseExpiresAtUtc <= now.UtcDateTime)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.State, NotificationDeliveryState.InProgress)
                    .SetProperty(item => item.LeaseExpiresAtUtc, now.Add(leaseDuration).UtcDateTime)
                    .SetProperty(item => item.LastAttemptAtUtc, now.UtcDateTime)
                    .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1),
                    cancellationToken);
            if (updated == 1) claimed.Add(candidate.Id);
        }
        if (claimed.Count == 0) return [];

        return await (
            from delivery in dbContext.NotificationDeliveries.AsNoTracking()
            join notification in dbContext.Notifications.AsNoTracking()
                on delivery.NotificationId equals notification.Id
            where claimed.Contains(delivery.Id)
            select new NotificationDeliveryWorkItem(
                delivery.Id,
                delivery.Channel,
                delivery.Recipient,
                delivery.ProtectedSecret,
                notification.EventType,
                notification.Title,
                notification.Url,
                delivery.AttemptCount))
            .ToArrayAsync(cancellationToken);
    }

    public async Task CompleteAttemptAsync(
        string deliveryId,
        NotificationDeliveryResult result,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);
        ArgumentNullException.ThrowIfNull(result);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var delivery = await dbContext.NotificationDeliveries.SingleOrDefaultAsync(
            item => item.Id == deliveryId && item.State == NotificationDeliveryState.InProgress,
            cancellationToken);
        if (delivery is null) return;
        delivery.ResponseStatusCode = result.ResponseStatusCode;
        delivery.ErrorCode = result.ErrorCode;
        delivery.LeaseExpiresAtUtc = null;
        if (result.Succeeded)
        {
            delivery.State = NotificationDeliveryState.Succeeded;
            delivery.NextAttemptAtUtc = null;
            delivery.CompletedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }
        else if (nextAttemptAt is null)
        {
            delivery.State = NotificationDeliveryState.Failed;
            delivery.NextAttemptAtUtc = null;
            delivery.CompletedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }
        else
        {
            delivery.State = NotificationDeliveryState.Pending;
            delivery.NextAttemptAtUtc = nextAttemptAt.Value.UtcDateTime;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<bool> CanStillReadAsync(
        GitCandyDbContext dbContext,
        string userId,
        long? repositoryId,
        long? teamId,
        CancellationToken cancellationToken)
    {
        if (teamId is long requiredTeamId
            && !await dbContext.UserTeamRoles.AsNoTracking().AnyAsync(
                item => item.TeamId == requiredTeamId && item.UserId == userId,
                cancellationToken))
        {
            return false;
        }
        if (repositoryId is not long requiredRepositoryId) return true;
        var administratorRole = RoleNames.Administrator.ToUpperInvariant();
        var isAdministrator = await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == userId && role.NormalizedName == administratorRole
            select userRole).AnyAsync(cancellationToken);
        if (isAdministrator) return true;
        return await dbContext.Repositories.AsNoTracking().AnyAsync(repository =>
            repository.Id == requiredRepositoryId
            && ((!repository.IsPrivate && repository.AllowAnonymousRead)
                || dbContext.UserRepositoryRoles.Any(role => role.RepositoryId == repository.Id
                    && role.UserId == userId && role.AllowRead)
                || dbContext.TeamRepositoryRoles.Any(role => role.RepositoryId == repository.Id
                    && role.AllowRead
                    && dbContext.UserTeamRoles.Any(member => member.TeamId == role.TeamId
                        && member.UserId == userId))),
            cancellationToken);
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
