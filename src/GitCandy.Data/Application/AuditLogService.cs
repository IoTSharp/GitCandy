using GitCandy.Audit;
using GitCandy.Data;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed class AuditLogService(GitCandyDbContext dbContext) : IAuditLogService
{
    private readonly GitCandyDbContext _dbContext = dbContext;

    public async Task<IReadOnlyList<AuditEvent>> GetRepositoryEventsAsync(
        long repositoryId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = Math.Clamp(limit, 1, 500);
        var governance = await _dbContext.GovernanceAuditEvents.AsNoTracking()
            .Where(item => item.RepositoryId == repositoryId)
            .OrderByDescending(item => item.OccurredAtUtc)
            .Take(boundedLimit)
            .ToArrayAsync(cancellationToken);
        var credentials = await _dbContext.CredentialAuditEvents.AsNoTracking()
            .Where(item => item.RepositoryId == repositoryId)
            .OrderByDescending(item => item.OccurredAtUtc)
            .Take(boundedLimit)
            .ToArrayAsync(cancellationToken);
        var userIds = governance.Select(item => item.ActorUserId)
            .Concat(credentials.Select(item => item.ActorUserId))
            .Where(static item => item is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var users = await _dbContext.Users.AsNoTracking()
            .Where(item => userIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.UserName ?? item.Id, cancellationToken);
        var events = governance.Select(item => new AuditEvent(
                item.Id,
                "governance",
                item.ActorUserId is not null ? AuditActorType.User
                    : item.DeployKeyId is not null ? AuditActorType.DeployKey : AuditActorType.System,
                item.ActorUserId is not null ? users.GetValueOrDefault(item.ActorUserId, "deleted-user")
                    : item.DeployKeyId is long deployKeyId ? $"deploy-key:{deployKeyId}" : "system",
                item.Action,
                item.Outcome,
                item.ReferenceName,
                item.Detail,
                ToDateTimeOffset(item.OccurredAtUtc)))
            .Concat(credentials.Select(item => new AuditEvent(
                item.Id,
                "credential",
                item.ActorUserId is not null ? AuditActorType.User : AuditActorType.Credential,
                item.ActorUserId is not null ? users.GetValueOrDefault(item.ActorUserId, "deleted-user")
                    : $"{item.CredentialKind}:{item.CredentialId}",
                item.Action,
                item.Outcome,
                $"{item.CredentialKind}:{item.CredentialId}",
                item.Detail,
                ToDateTimeOffset(item.OccurredAtUtc))))
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id)
            .Take(boundedLimit)
            .ToArray();
        return events;
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
