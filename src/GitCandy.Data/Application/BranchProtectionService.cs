using System.IO.Enumeration;
using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Permissions;
using GitCandy.Governance;
using GitCandy.Integrations;
using GitCandy.PullRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GitCandy.Application;

internal sealed class BranchProtectionService(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    TimeProvider timeProvider,
    IOptions<GitCandyApplicationOptions> applicationOptions) : IBranchProtectionService, IGitPushGate
{
    private const string BranchPrefix = "refs/heads/";
    private const int MaxRequiredApprovals = 20;
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly GitCandyApplicationOptions _applicationOptions = applicationOptions.Value;

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
            || edit.RequiredApprovals is < 0 or > MaxRequiredApprovals
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
        rule.RequiredApprovals = edit.RequiredApprovals;
        rule.RequireCodeOwnerReviews = edit.RequireCodeOwnerReviews;
        rule.DismissStaleApprovals = edit.DismissStaleApprovals;
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
        var requiredChecksSatisfied = true;
        GitReviewGateStatus? reviewStatus = null;
        foreach (var update in request.Updates)
        {
            if (!update.ReferenceName.StartsWith(BranchPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var branch = update.ReferenceName[BranchPrefix.Length..];
            var matchingRules = rules.Where(rule => Matches(rule.Pattern, branch)).ToArray();
            var enforcedRules = new List<GitCandyBranchProtectionRule>(matchingRules.Length);
            var accessBlocked = false;
            foreach (var rule in matchingRules)
            {
                if (isAdministrator && rule.AllowAdministratorBypass)
                {
                    if (request.RecordAudit)
                    {
                        AddAudit(dbContext, request.RepositoryId, request.Actor.UserId, request.Actor.DeployKeyId,
                            "gate.bypass", "success", update.ReferenceName, rule.Pattern, now);
                    }
                    continue;
                }

                enforcedRules.Add(rule);

                var blocker = request.EvaluateAccess
                    ? GetBlocker(rule, request.Operation, update, isOwner)
                    : null;
                if (blocker is not null)
                {
                    accessBlocked = true;
                    reasons.Add($"{update.ReferenceName}: {blocker}");
                    if (request.RecordAudit)
                    {
                        AddAudit(dbContext, request.RepositoryId, request.Actor.UserId, request.Actor.DeployKeyId,
                            "gate.reject", "denied", update.ReferenceName, blocker, now);
                    }
                }
            }

            if (update.IsDelete || accessBlocked)
            {
                continue;
            }

            foreach (var requiredCheck in enforcedRules
                .SelectMany(rule => rule.RequiredChecks)
                .DistinctBy(item => item.Context, StringComparer.Ordinal)
                .OrderBy(item => item.Context, StringComparer.Ordinal))
            {
                var targetSha = update.NewObjectId.ToLowerInvariant();
                if (latestChecks.TryGetValue((targetSha, requiredCheck.Context), out var check)
                    && check.State == CommitCheckState.Success)
                {
                    continue;
                }
                requiredChecksSatisfied = false;
                var checkBlocker = latestChecks.ContainsKey((targetSha, requiredCheck.Context))
                    ? $"required check '{requiredCheck.Context}' has not succeeded"
                    : $"required check '{requiredCheck.Context}' is missing";
                reasons.Add($"{update.ReferenceName}: {checkBlocker}");
                if (request.RecordAudit)
                {
                    AddAudit(dbContext, request.RepositoryId, request.Actor.UserId, request.Actor.DeployKeyId,
                        "gate.reject", "denied", update.ReferenceName, checkBlocker, now);
                }
            }

            var review = await EvaluateRequiredReviewsAsync(
                dbContext,
                request,
                update,
                enforcedRules,
                cancellationToken);
            if (review.Status is not null)
            {
                reviewStatus = review.Status;
            }
            foreach (var reviewBlocker in review.Blockers)
            {
                reasons.Add($"{update.ReferenceName}: {reviewBlocker}");
                if (request.RecordAudit)
                {
                    AddAudit(dbContext, request.RepositoryId, request.Actor.UserId, request.Actor.DeployKeyId,
                        "gate.reject", "denied", update.ReferenceName, reviewBlocker, now);
                }
            }
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return reasons.Count == 0
            ? new GitPushGateResult(true, [], requiredChecksSatisfied, reviewStatus)
            : new GitPushGateResult(
                false,
                reasons.Distinct(StringComparer.Ordinal).ToArray(),
                requiredChecksSatisfied,
                reviewStatus);
    }

    private async Task<ReviewGateEvaluation> EvaluateRequiredReviewsAsync(
        GitCandyDbContext dbContext,
        GitPushGateRequest request,
        GitRefUpdate update,
        IReadOnlyList<GitCandyBranchProtectionRule> rules,
        CancellationToken cancellationToken)
    {
        var branchRequiredApprovals = rules.Count == 0
            ? 0
            : rules.Max(static rule => rule.RequiredApprovals);
        var requireCodeOwners = rules.Any(static rule => rule.RequireCodeOwnerReviews);
        if (request.Operation != GitRefOperation.Merge)
        {
            if (branchRequiredApprovals == 0 && !requireCodeOwners)
            {
                return ReviewGateEvaluation.Empty;
            }

            return new ReviewGateEvaluation(
                null,
                ["required reviews can only be satisfied by an approved Pull Request"]);
        }

        var requiredApprovals = Math.Max(
            Math.Max(0, _applicationOptions.RequiredPullRequestApprovals),
            branchRequiredApprovals);
        if (request.Review is null)
        {
            return new ReviewGateEvaluation(
                new GitReviewGateStatus(0, requiredApprovals, requireCodeOwners, !requireCodeOwners),
                ["Pull Request review context is missing"]);
        }

        var pullRequest = await dbContext.PullRequests.AsNoTracking()
            .Include(item => item.Reviews)
            .SingleOrDefaultAsync(
                item => item.RepositoryId == request.RepositoryId
                    && item.Number == request.Review.PullRequestNumber,
                cancellationToken);
        if (pullRequest is null
            || pullRequest.State != PullRequestState.Open
            || !string.Equals($"{BranchPrefix}{pullRequest.TargetBranch}", update.ReferenceName, StringComparison.Ordinal)
            || !string.Equals(pullRequest.CurrentBaseSha, update.OldObjectId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(pullRequest.CurrentHeadSha, update.NewObjectId, StringComparison.OrdinalIgnoreCase))
        {
            return new ReviewGateEvaluation(
                new GitReviewGateStatus(0, requiredApprovals, requireCodeOwners, !requireCodeOwners),
                ["Pull Request review context does not match the protected ref update"]);
        }

        var dismissStale = _applicationOptions.DismissStalePullRequestApprovals
            || rules.Any(static rule => rule.DismissStaleApprovals);
        var effectiveApproverIds = pullRequest.Reviews
            .Where(item => item.DismissedAtUtc is null && item.State != PullRequestReviewState.Commented)
            .GroupBy(item => item.ReviewerUserId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.SubmittedAtUtc).ThenByDescending(item => item.Id).First())
            .Where(item => item.State == PullRequestReviewState.Approved
                && (!dismissStale || string.Equals(item.HeadSha, pullRequest.CurrentHeadSha, StringComparison.Ordinal))
                && (_applicationOptions.AllowAuthorApproval
                    || !string.Equals(item.ReviewerUserId, pullRequest.AuthorUserId, StringComparison.Ordinal)))
            .Select(item => item.ReviewerUserId)
            .ToHashSet(StringComparer.Ordinal);
        var blockers = new List<string>();
        if (effectiveApproverIds.Count < requiredApprovals)
        {
            blockers.Add($"{requiredApprovals - effectiveApproverIds.Count} more approval(s) required for the current head");
        }

        var codeOwnersSatisfied = true;
        if (requireCodeOwners)
        {
            var evaluation = CodeOwnersParser.Evaluate(request.Review.CodeOwners);
            if (!evaluation.IsValid)
            {
                codeOwnersSatisfied = false;
                blockers.Add("CODEOWNERS is invalid: " + string.Join("; ", evaluation.Diagnostics.Take(3)));
            }
            else
            {
                var ownerUserIds = await ResolveCodeOwnerUserIdsAsync(
                    dbContext,
                    request.RepositoryId,
                    evaluation.Assignments.SelectMany(static item => item.Owners),
                    cancellationToken);
                foreach (var group in evaluation.Assignments.GroupBy(
                    static item => $"{item.LineNumber}:{string.Join(',', item.Owners.OrderBy(owner => owner, StringComparer.OrdinalIgnoreCase))}",
                    StringComparer.Ordinal))
                {
                    var first = group.First();
                    var eligibleOwners = first.Owners
                        .SelectMany(owner => ownerUserIds.GetValueOrDefault(owner, []))
                        .ToHashSet(StringComparer.Ordinal);
                    var paths = group.Select(static item => item.Path).Take(3).ToArray();
                    var pathDetail = string.Join(", ", paths.Select(static path => $"'{path}'"));
                    if (group.Count() > paths.Length)
                    {
                        pathDetail += $" and {group.Count() - paths.Length} more path(s)";
                    }

                    if (eligibleOwners.Count == 0)
                    {
                        codeOwnersSatisfied = false;
                        blockers.Add($"CODEOWNERS line {first.LineNumber} has no eligible repository writer for {pathDetail}");
                    }
                    else if (!eligibleOwners.Overlaps(effectiveApproverIds))
                    {
                        codeOwnersSatisfied = false;
                        blockers.Add($"CODEOWNERS line {first.LineNumber} requires approval from {string.Join(", ", first.Owners)} for {pathDetail}");
                    }
                }
            }
        }

        return new ReviewGateEvaluation(
            new GitReviewGateStatus(
                effectiveApproverIds.Count,
                requiredApprovals,
                requireCodeOwners,
                codeOwnersSatisfied),
            blockers);
    }

    private static async Task<Dictionary<string, HashSet<string>>> ResolveCodeOwnerUserIdsAsync(
        GitCandyDbContext dbContext,
        long repositoryId,
        IEnumerable<string> owners,
        CancellationToken cancellationToken)
    {
        var ownerTokens = owners.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var normalizedNames = ownerTokens
            .Select(static owner => owner.TrimStart('@').ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var writableUserIds = dbContext.UserRepositoryRoles.AsNoTracking()
            .Where(item => item.RepositoryId == repositoryId && item.AllowRead && item.AllowWrite)
            .Select(item => item.UserId)
            .Union(
                from repositoryRole in dbContext.TeamRepositoryRoles.AsNoTracking()
                join membership in dbContext.UserTeamRoles.AsNoTracking() on repositoryRole.TeamId equals membership.TeamId
                where repositoryRole.RepositoryId == repositoryId
                    && repositoryRole.AllowRead
                    && repositoryRole.AllowWrite
                select membership.UserId);
        var users = await dbContext.Users.AsNoTracking()
            .Where(item => item.NormalizedUserName != null
                && normalizedNames.Contains(item.NormalizedUserName)
                && writableUserIds.Contains(item.Id))
            .Select(item => new { item.Id, item.NormalizedUserName })
            .ToArrayAsync(cancellationToken);
        var teamMembers = await (
            from team in dbContext.Teams.AsNoTracking()
            join repositoryRole in dbContext.TeamRepositoryRoles.AsNoTracking() on team.Id equals repositoryRole.TeamId
            join membership in dbContext.UserTeamRoles.AsNoTracking() on team.Id equals membership.TeamId
            where repositoryRole.RepositoryId == repositoryId
                && repositoryRole.AllowRead
                && repositoryRole.AllowWrite
                && normalizedNames.Contains(team.NormalizedName)
            select new { membership.UserId, team.NormalizedName })
            .ToArrayAsync(cancellationToken);

        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var owner in ownerTokens)
        {
            var normalized = owner.TrimStart('@').ToUpperInvariant();
            result[owner] = users.Where(item => string.Equals(item.NormalizedUserName, normalized, StringComparison.Ordinal))
                .Select(item => item.Id)
                .Concat(teamMembers.Where(item => string.Equals(item.NormalizedName, normalized, StringComparison.Ordinal))
                    .Select(item => item.UserId))
                .ToHashSet(StringComparer.Ordinal);
        }

        return result;
    }

    private sealed record ReviewGateEvaluation(GitReviewGateStatus? Status, IReadOnlyList<string> Blockers)
    {
        public static ReviewGateEvaluation Empty { get; } = new(null, []);
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
            rule.RequiredApprovals,
            rule.RequireCodeOwnerReviews,
            rule.DismissStaleApprovals,
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
            Detail = detail.Length <= SchemaLimits.AuditDetail
                ? detail
                : detail[..SchemaLimits.AuditDetail],
            OccurredAtUtc = occurredAt.UtcDateTime
        });
    }
}
