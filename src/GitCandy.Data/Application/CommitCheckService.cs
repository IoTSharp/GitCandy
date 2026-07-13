using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Integrations;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed class CommitCheckService(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    IOutboundTargetPolicy targetPolicy,
    TimeProvider timeProvider) : ICommitCheckService
{
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly IOutboundTargetPolicy _targetPolicy = targetPolicy;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<CommitCheckSummary?> UpsertAsync(
        long repositoryId,
        string actorUserId,
        long? credentialId,
        CommitCheckUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        ArgumentNullException.ThrowIfNull(update);
        var sha = NormalizeSha(update.Sha);
        var context = NormalizeContext(update.Context);
        var description = update.Description.Trim();
        Uri? target = null;
        if (update.TargetUrl is not null
            && (!Uri.TryCreate(update.TargetUrl, UriKind.Absolute, out target)
                || !await _targetPolicy.IsAllowedAsync(target, cancellationToken)))
        {
            return null;
        }
        if (sha is null
            || context is null
            || description.Length > SchemaLimits.CheckDescription
            || update.ExternalId?.Length > SchemaLimits.CheckExternalId
            || !Enum.IsDefined(update.Kind)
            || !Enum.IsDefined(update.State))
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await dbContext.Repositories.AsNoTracking().AnyAsync(item => item.Id == repositoryId, cancellationToken))
        {
            return null;
        }
        var check = await dbContext.CommitChecks.SingleOrDefaultAsync(
            item => item.RepositoryId == repositoryId
                && item.Sha == sha
                && item.Kind == update.Kind
                && item.Context == context,
            cancellationToken);
        if (check is null)
        {
            check = new GitCandyCommitCheck
            {
                RepositoryId = repositoryId,
                Sha = sha,
                Kind = update.Kind,
                Context = context,
                CreatedAtUtc = now.UtcDateTime
            };
            dbContext.CommitChecks.Add(check);
        }
        check.State = update.State;
        check.Description = description;
        check.TargetUrl = target?.AbsoluteUri;
        check.ExternalId = update.ExternalId?.Trim();
        check.ActorUserId = actorUserId;
        check.CredentialId = credentialId;
        check.UpdatedAtUtc = now.UtcDateTime;
        var summary = ToSummary(check);
        await IntegrationEventPublisher.AddCheckUpdatedAsync(
            dbContext,
            repositoryId,
            actorUserId,
            summary,
            cancellationToken);
        dbContext.GovernanceAuditEvents.Add(new GitCandyGovernanceAuditEvent
        {
            RepositoryId = repositoryId,
            ActorUserId = actorUserId,
            Action = "check.update",
            Outcome = "success",
            ReferenceName = sha,
            Detail = $"{context}:{update.State}",
            OccurredAtUtc = now.UtcDateTime
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSummary(check);
    }

    public async Task<IReadOnlyList<CommitCheckSummary>> GetForCommitAsync(
        long repositoryId,
        string sha,
        CancellationToken cancellationToken = default)
    {
        var normalizedSha = NormalizeSha(sha);
        if (normalizedSha is null) return [];
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var checks = await dbContext.CommitChecks.AsNoTracking()
            .Where(item => item.RepositoryId == repositoryId && item.Sha == normalizedSha)
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.Context)
            .ToArrayAsync(cancellationToken);
        return checks.Select(ToSummary).ToArray();
    }

    private static string? NormalizeSha(string sha)
    {
        var value = sha.Trim().ToLowerInvariant();
        return value.Length is 40 or 64 && value.All(Uri.IsHexDigit) ? value : null;
    }

    private static string? NormalizeContext(string context)
    {
        var value = context.Trim();
        return value.Length is > 0 and <= SchemaLimits.CheckContext
            && value.All(static character => char.IsLetterOrDigit(character)
                || character is '-' or '_' or '.' or '/' or ':')
                ? value
                : null;
    }

    private static CommitCheckSummary ToSummary(GitCandyCommitCheck check) =>
        new(
            check.Id,
            check.Sha,
            check.Kind,
            check.Context,
            check.State,
            check.Description,
            check.TargetUrl,
            check.ExternalId,
            ToDateTimeOffset(check.CreatedAtUtc),
            ToDateTimeOffset(check.UpdatedAtUtc));

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
