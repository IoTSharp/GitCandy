using System.IO.Enumeration;
using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Permissions;
using GitCandy.Governance;
using GitCandy.Integrations;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed class BranchProtectionService(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    TimeProvider timeProvider) : IBranchProtectionService, IGitPushGate
{
    private const string BranchPrefix = "refs/heads/";
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<IReadOnlyList<BranchProtectionSummary>> GetForRepositoryAsync(
        long repositoryId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rules = await dbContext.BranchProtectionRules.AsNoTracking()
            .Include(rule => rule.RequiredChecks)
            .Where(rule => rule.RepositoryId == repositoryId)
            .OrderBy(rule => rule.Pattern)
            .ToArrayAsync(cancellationToken);
        return rules.Select(ToSummary).ToArray();
    }

    public async Task<BranchProtectionSummary?> SaveAsync(
        long repositoryId,
        string actorUserId,
        BranchProtectionEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        ArgumentNullException.ThrowIfNull(edit);
        var pattern = NormalizePattern(edit.Pattern);
        var requiredChecks = NormalizeRequiredChecks(edit.RequiredChecks);
        if (pattern is null
            || requiredChecks is null
            || !Enum.IsDefined(edit.PushAccess)
            || !Enum.IsDefined(edit.MergeAccess))
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        GitCandyBranchProtectionRule? rule = null;
        if (edit.Id is long id)
        {
            rule = await dbContext.BranchProtectionRules
                .Include(item => item.RequiredChecks)
                .SingleOrDefaultAsync(
                item => item.Id == id && item.RepositoryId == repositoryId,
                cancellationToken);
            if (rule is null)
            {
                return null;
            }
        }
        else
        {
            if (!await dbContext.Repositories.AsNoTracking().AnyAsync(item => item.Id == repositoryId, cancellationToken)
                || await dbContext.BranchProtectionRules.AsNoTracking().AnyAsync(
                    item => item.RepositoryId == repositoryId && item.Pattern == pattern,
                    cancellationToken))
            {
                return null;
            }

            rule = new GitCandyBranchProtectionRule
            {
                RepositoryId = repositoryId,
                CreatedAtUtc = now.UtcDateTime
            };
            dbContext.BranchProtectionRules.Add(rule);
        }

        rule.Pattern = pattern;
        rule.PushAccess = (int)edit.PushAccess;
        rule.MergeAccess = (int)edit.MergeAccess;
        rule.AllowForcePushes = edit.AllowForcePushes;
        rule.AllowDeletions = edit.AllowDeletions;
        rule.AllowAdministratorBypass = edit.AllowAdministratorBypass;
        rule.UpdatedAtUtc = now.UtcDateTime;
        var desiredContexts = requiredChecks.ToHashSet(StringComparer.Ordinal);
        foreach (var existing in rule.RequiredChecks.Where(
            item => !desiredContexts.Contains(item.Context)).ToArray())
        {
            dbContext.BranchProtectionRequiredChecks.Remove(existing);
        }
        var existingContexts = rule.RequiredChecks.Select(item => item.Context).ToHashSet(StringComparer.Ordinal);
        foreach (var context in requiredChecks.Where(context => !existingContexts.Contains(context)))
        {
            rule.RequiredChecks.Add(new GitCandyBranchProtectionRequiredCheck { Context = context });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        AddAudit(dbContext, repositoryId, actorUserId, null, "rule.save", "success", string.Empty, pattern, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSummary(rule);
    }

    public async Task<bool> DeleteAsync(
        long repositoryId,
        long ruleId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        var now = _timeProvider.GetUtcNow();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rule = await dbContext.BranchProtectionRules.SingleOrDefaultAsync(
            item => item.Id == ruleId && item.RepositoryId == repositoryId,
            cancellationToken);
        if (rule is null)
        {
            return false;
        }

        dbContext.BranchProtectionRules.Remove(rule);
        AddAudit(dbContext, repositoryId, actorUserId, null, "rule.delete", "success", string.Empty, rule.Pattern, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<GitPushGateResult> EvaluateAsync(
        GitPushGateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Updates.Count == 0)
        {
            return GitPushGateResult.Allow;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rules = await dbContext.BranchProtectionRules.AsNoTracking()
            .Include(rule => rule.RequiredChecks)
            .Where(rule => rule.RepositoryId == request.RepositoryId)
            .ToArrayAsync(cancellationToken);
        if (rules.Length == 0)
        {
            return GitPushGateResult.Allow;
        }

        var requiredContexts = rules.SelectMany(rule => rule.RequiredChecks)
            .Select(item => item.Context)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var targetShas = request.Updates
            .Where(update => !update.IsDelete)
            .Select(update => update.NewObjectId.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var checkRows = requiredContexts.Length == 0 || targetShas.Length == 0
            ? []
            : await dbContext.CommitChecks.AsNoTracking()
                .Where(check => check.RepositoryId == request.RepositoryId
                    && targetShas.Contains(check.Sha)
                    && requiredContexts.Contains(check.Context))
                .ToArrayAsync(cancellationToken);
        var latestChecks = checkRows
            .GroupBy(check => (check.Sha, check.Context))
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(check => check.UpdatedAtUtc).First(),
                EqualityComparer<(string Sha, string Context)>.Default);

        var isAdministrator = await IsAdministratorAsync(dbContext, request.Actor.UserId, cancellationToken);
        var permissionQuery = new GitCandyRepositoryPermissionQuery(dbContext);
        var isOwner = request.Actor.UserId is not null
            && await permissionQuery.IsRepositoryOwnerAsync(
                request.RepositoryId,
                request.Actor.UserId,
                isAdministrator,
                cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var reasons = new List<string>();
        foreach (var update in request.Updates)
        {
            if (!update.ReferenceName.StartsWith(BranchPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var branch = update.ReferenceName[BranchPrefix.Length..];
            foreach (var rule in rules.Where(rule => Matches(rule.Pattern, branch)))
            {
                if (isAdministrator && rule.AllowAdministratorBypass)
                {
                    AddAudit(dbContext, request.RepositoryId, request.Actor.UserId, request.Actor.DeployKeyId,
                        "gate.bypass", "success", update.ReferenceName, rule.Pattern, now);
                    continue;
                }

                var blocker = GetBlocker(rule, request.Operation, update, isOwner);
                if (blocker is not null)
                {
                    reasons.Add($"{update.ReferenceName}: {blocker}");
                    AddAudit(dbContext, request.RepositoryId, request.Actor.UserId, request.Actor.DeployKeyId,
                        "gate.reject", "denied", update.ReferenceName, blocker, now);
                    continue;
                }
                if (update.IsDelete) continue;
                foreach (var requiredCheck in rule.RequiredChecks.OrderBy(item => item.Context, StringComparer.Ordinal))
                {
                    var targetSha = update.NewObjectId.ToLowerInvariant();
                    if (latestChecks.TryGetValue((targetSha, requiredCheck.Context), out var check)
                        && check.State == CommitCheckState.Success)
                    {
                        continue;
                    }
                    var checkBlocker = latestChecks.ContainsKey((targetSha, requiredCheck.Context))
                        ? $"required check '{requiredCheck.Context}' has not succeeded"
                        : $"required check '{requiredCheck.Context}' is missing";
                    reasons.Add($"{update.ReferenceName}: {checkBlocker}");
                    AddAudit(dbContext, request.RepositoryId, request.Actor.UserId, request.Actor.DeployKeyId,
                        "gate.reject", "denied", update.ReferenceName, checkBlocker, now);
                }
            }
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return reasons.Count == 0
            ? GitPushGateResult.Allow
            : new GitPushGateResult(false, reasons.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string? GetBlocker(
        GitCandyBranchProtectionRule rule,
        GitRefOperation operation,
        GitRefUpdate update,
        bool isOwner)
    {
        if (update.IsDelete && !rule.AllowDeletions)
        {
            return "the protected branch cannot be deleted";
        }
        if (update.IsForceUpdate && !rule.AllowForcePushes)
        {
            return "the protected branch does not allow non-fast-forward updates";
        }

        var access = operation == GitRefOperation.Merge
            ? (BranchAccessLevel)rule.MergeAccess
            : (BranchAccessLevel)rule.PushAccess;
        return access switch
        {
            BranchAccessLevel.RepositoryWrite => null,
            BranchAccessLevel.RepositoryOwner when isOwner => null,
            BranchAccessLevel.RepositoryOwner => "repository owner permission is required",
            BranchAccessLevel.Nobody => operation == GitRefOperation.Merge
                ? "merges are disabled for the protected branch"
                : "direct pushes are disabled for the protected branch",
            _ => "the branch protection rule is invalid"
        };
    }

    private static async Task<bool> IsAdministratorAsync(
        GitCandyDbContext dbContext,
        string? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return false;
        }

        var roleName = RoleNames.Administrator.ToUpperInvariant();
        return await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == userId && role.NormalizedName == roleName
            select userRole)
            .AnyAsync(cancellationToken);
    }

    private static string? NormalizePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        var normalized = pattern.Trim();
        if (normalized.StartsWith(BranchPrefix, StringComparison.Ordinal))
        {
            normalized = normalized[BranchPrefix.Length..];
        }
        if (normalized.Length == 0
            || normalized.Length > SchemaLimits.GitRefName
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.EndsWith("/", StringComparison.Ordinal)
            || normalized.Contains("..", StringComparison.Ordinal)
            || normalized.Contains("//", StringComparison.Ordinal)
            || normalized.Any(static value => char.IsControl(value) || value is ' ' or '~' or '^' or ':' or '[' or '\\'))
        {
            return null;
        }
        return normalized;
    }

    private static bool Matches(string pattern, string branch)
    {
        return FileSystemName.MatchesSimpleExpression(pattern, branch, ignoreCase: false);
    }

    private static IReadOnlyList<string>? NormalizeRequiredChecks(IReadOnlyList<string>? requiredChecks)
    {
        if (requiredChecks is null || requiredChecks.Count == 0) return [];
        if (requiredChecks.Count > 20) return null;
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in requiredChecks)
        {
            var context = value.Trim();
            if (context.Length is 0 or > SchemaLimits.CheckContext
                || context.Any(static character => !char.IsLetterOrDigit(character)
                    && character is not '-' and not '_' and not '.' and not '/' and not ':'))
            {
                return null;
            }
            normalized.Add(context);
        }
        return normalized.OrderBy(static context => context, StringComparer.Ordinal).ToArray();
    }

    private static BranchProtectionSummary ToSummary(GitCandyBranchProtectionRule rule)
    {
        return new BranchProtectionSummary(
            rule.Id,
            rule.Pattern,
            (BranchAccessLevel)rule.PushAccess,
            (BranchAccessLevel)rule.MergeAccess,
            rule.AllowForcePushes,
            rule.AllowDeletions,
            rule.AllowAdministratorBypass,
            rule.RequiredChecks.OrderBy(item => item.Context, StringComparer.Ordinal)
                .Select(item => item.Context).ToArray(),
            ToDateTimeOffset(rule.CreatedAtUtc),
            ToDateTimeOffset(rule.UpdatedAtUtc));
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static void AddAudit(
        GitCandyDbContext dbContext,
        long repositoryId,
        string? actorUserId,
        long? deployKeyId,
        string action,
        string outcome,
        string referenceName,
        string detail,
        DateTimeOffset occurredAt)
    {
        dbContext.GovernanceAuditEvents.Add(new GitCandyGovernanceAuditEvent
        {
            RepositoryId = repositoryId,
            ActorUserId = actorUserId,
            DeployKeyId = deployKeyId,
            Action = action,
            Outcome = outcome,
            ReferenceName = referenceName,
            Detail = detail,
            OccurredAtUtc = occurredAt.UtcDateTime
        });
    }
}
