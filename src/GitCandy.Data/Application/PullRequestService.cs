using System.Data;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Permissions;
using GitCandy.Issues;
using GitCandy.PullRequests;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

/// <summary>基于 EF Core 和受控 Git ref 能力的 Pull Request 应用服务。</summary>
internal sealed class PullRequestService(
    GitCandyDbContext dbContext,
    IIssueMarkdownRenderer markdownRenderer,
    IGitCandyRepositoryPermissionQuery permissionQuery,
    IPullRequestGitRepository gitRepository,
    TimeProvider timeProvider) : IPullRequestService
{
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly IIssueMarkdownRenderer _markdownRenderer = markdownRenderer;
    private readonly IGitCandyRepositoryPermissionQuery _permissionQuery = permissionQuery;
    private readonly IPullRequestGitRepository _gitRepository = gitRepository;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<PullRequestPage> GetPullRequestsAsync(
        long repositoryId,
        PullRequestQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var pullRequests = _dbContext.PullRequests.AsNoTracking()
            .Where(item => item.RepositoryId == repositoryId && item.State == query.State);
        var totalCount = await pullRequests.CountAsync(cancellationToken);
        var items = await pullRequests
            .Include(item => item.Author)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ThenByDescending(item => item.Number)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new PullRequestSummary(
                item.Id,
                item.Number,
                item.Title,
                item.State,
                item.IsDraft,
                item.Author!.UserName ?? string.Empty,
                item.SourceBranch,
                item.TargetBranch,
                item.CurrentHeadSha,
                item.CreatedAtUtc,
                item.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new PullRequestPage(page, pageSize, totalCount, items);
    }

    public async Task<PullRequestDetails?> GetPullRequestAsync(
        long repositoryId,
        long number,
        CancellationToken cancellationToken = default)
    {
        var pullRequest = await _dbContext.PullRequests.AsNoTracking()
            .Include(item => item.Author)
            .SingleOrDefaultAsync(
                item => item.RepositoryId == repositoryId && item.Number == number,
                cancellationToken);
        if (pullRequest is null)
        {
            return null;
        }

        var timeline = await _dbContext.PullRequestTimelineEvents.AsNoTracking()
            .Include(item => item.Actor)
            .Where(item => item.PullRequestId == pullRequest.Id)
            .OrderBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .Select(item => new PullRequestTimelineItem(
                item.Id,
                item.Type,
                item.Actor == null ? null : item.Actor.UserName,
                item.Detail,
                item.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return ToDetails(pullRequest, timeline);
    }

    public async Task<IReadOnlyList<PullRequestBranch>> GetBranchesAsync(
        long repositoryId,
        CancellationToken cancellationToken = default)
    {
        var storageName = await GetStorageNameAsync(repositoryId, cancellationToken);
        return storageName is null
            ? []
            : _gitRepository.GetBranches(storageName, cancellationToken);
    }

    public async Task<PullRequestDetails> CreatePullRequestAsync(
        long repositoryId,
        CreatePullRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await CreatePullRequestAttemptAsync(repositoryId, command, cancellationToken);
            }
            catch (Exception exception) when (attempt < 5 && IsTransientCreateConflict(exception))
            {
                _dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken);
            }
        }
    }

    private async Task<PullRequestDetails> CreatePullRequestAttemptAsync(
        long repositoryId,
        CreatePullRequestCommand command,
        CancellationToken cancellationToken)
    {
        ValidateTitleAndBody(command.Title, command.Body);
        var sourceBranch = NormalizeBranch(command.SourceBranch);
        var targetBranch = NormalizeBranch(command.TargetBranch);
        if (string.Equals(sourceBranch, targetBranch, StringComparison.Ordinal))
        {
            throw new PullRequestValidationException(
                PullRequestMutationResult.Invalid,
                "Source and target branches must be different.");
        }

        if (!await CanWriteAsUserAsync(repositoryId, command.AuthorUserId, cancellationToken))
        {
            throw new PullRequestValidationException(
                PullRequestMutationResult.Forbidden,
                "Write access to the repository is required.");
        }

        var address = await _dbContext.Repositories.AsNoTracking()
            .Where(item => item.Id == repositoryId)
            .Select(item => new { item.StorageName, NamespaceSlug = item.Namespace!.Slug, RepositorySlug = item.Name })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PullRequestValidationException(
                PullRequestMutationResult.NotFound,
                "The repository does not exist.");
        PullRequestBranchComparison? comparison;
        try
        {
            comparison = _gitRepository.CompareBranches(
                address.StorageName,
                sourceBranch,
                targetBranch,
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new PullRequestValidationException(
                PullRequestMutationResult.Invalid,
                exception.Message);
        }

        if (comparison is null)
        {
            throw new PullRequestValidationException(
                PullRequestMutationResult.BranchNotFound,
                "The source or target branch does not exist.");
        }

        if (comparison.AheadBy == 0 || string.Equals(comparison.BaseSha, comparison.HeadSha, StringComparison.Ordinal))
        {
            throw new PullRequestValidationException(
                PullRequestMutationResult.NoChanges,
                "The source branch has no commits to merge into the target branch.");
        }

        var openPairKey = BuildOpenPairKey(sourceBranch, targetBranch);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        if (await _dbContext.PullRequests.AnyAsync(
            item => item.RepositoryId == repositoryId && item.ActivePairKey == openPairKey,
            cancellationToken))
        {
            throw new PullRequestValidationException(
                PullRequestMutationResult.Duplicate,
                "An open Pull Request already exists for these branches.");
        }

        var sequence = await _dbContext.WorkItemSequences.SingleOrDefaultAsync(
            item => item.RepositoryId == repositoryId,
            cancellationToken);
        if (sequence is null)
        {
            sequence = new GitCandyWorkItemSequence { RepositoryId = repositoryId };
            _dbContext.WorkItemSequences.Add(sequence);
        }

        var now = UtcNow;
        var pullRequest = new GitCandyPullRequest
        {
            RepositoryId = repositoryId,
            Number = sequence.NextNumber,
            Title = command.Title.Trim(),
            BodyMarkdown = command.Body.Trim(),
            BodyHtml = _markdownRenderer.Render(command.Body, address.NamespaceSlug, address.RepositorySlug),
            AuthorUserId = command.AuthorUserId,
            SourceBranch = sourceBranch,
            TargetBranch = targetBranch,
            OriginalBaseSha = comparison.BaseSha,
            OriginalHeadSha = comparison.HeadSha,
            CurrentBaseSha = comparison.BaseSha,
            CurrentHeadSha = comparison.HeadSha,
            State = PullRequestState.Open,
            IsDraft = command.IsDraft,
            ActivePairKey = openPairKey,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = 1
        };
        pullRequest.Timeline.Add(NewEvent(
            PullRequestEventType.Created,
            command.AuthorUserId,
            now,
            $"{sourceBranch} into {targetBranch}"));
        sequence.NextNumber++;
        sequence.Version++;
        _dbContext.PullRequests.Add(pullRequest);

        var headWritten = false;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _gitRepository.UpdatePullRequestHead(
                address.StorageName,
                pullRequest.Number,
                comparison.HeadSha,
                cancellationToken);
            headWritten = true;
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (headWritten)
            {
                _gitRepository.DeletePullRequestHead(
                    address.StorageName,
                    pullRequest.Number,
                    CancellationToken.None);
            }

            throw;
        }

        return (await GetPullRequestAsync(repositoryId, pullRequest.Number, cancellationToken))!;
    }

    public async Task<PullRequestMutationResult> EditPullRequestAsync(
        long repositoryId,
        long number,
        string actorUserId,
        bool isOwner,
        EditPullRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateTitleAndBody(command.Title, command.Body);
        }
        catch (PullRequestValidationException)
        {
            return PullRequestMutationResult.Invalid;
        }

        var pullRequest = await FindPullRequestAsync(repositoryId, number, cancellationToken);
        if (pullRequest is null)
        {
            return PullRequestMutationResult.NotFound;
        }

        if (!await CanReadAsUserAsync(repositoryId, actorUserId, cancellationToken)
            || (!isOwner && pullRequest.AuthorUserId != actorUserId))
        {
            return PullRequestMutationResult.Forbidden;
        }

        if (pullRequest.Version != command.Version)
        {
            return PullRequestMutationResult.Conflict;
        }

        var address = await GetAddressAsync(repositoryId, cancellationToken);
        if (address is null)
        {
            return PullRequestMutationResult.NotFound;
        }

        pullRequest.Title = command.Title.Trim();
        pullRequest.BodyMarkdown = command.Body.Trim();
        pullRequest.BodyHtml = _markdownRenderer.Render(
            command.Body,
            address.Value.NamespaceSlug,
            address.Value.RepositorySlug);
        Touch(pullRequest);
        pullRequest.Timeline.Add(NewEvent(PullRequestEventType.Edited, actorUserId, UtcNow));
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<PullRequestMutationResult> SetDraftAsync(
        long repositoryId,
        long number,
        string actorUserId,
        bool isOwner,
        bool isDraft,
        CancellationToken cancellationToken = default)
    {
        var pullRequest = await FindPullRequestAsync(repositoryId, number, cancellationToken);
        if (pullRequest is null)
        {
            return PullRequestMutationResult.NotFound;
        }

        if (!await CanReadAsUserAsync(repositoryId, actorUserId, cancellationToken)
            || (!isOwner && pullRequest.AuthorUserId != actorUserId))
        {
            return PullRequestMutationResult.Forbidden;
        }

        if (pullRequest.State != PullRequestState.Open)
        {
            return PullRequestMutationResult.Invalid;
        }

        if (pullRequest.IsDraft == isDraft)
        {
            return PullRequestMutationResult.Succeeded;
        }

        pullRequest.IsDraft = isDraft;
        Touch(pullRequest);
        pullRequest.Timeline.Add(NewEvent(
            isDraft ? PullRequestEventType.ConvertedToDraft : PullRequestEventType.MarkedReady,
            actorUserId,
            UtcNow));
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<PullRequestMutationResult> SetStateAsync(
        long repositoryId,
        long number,
        string actorUserId,
        bool isOwner,
        PullRequestState state,
        CancellationToken cancellationToken = default)
    {
        if (state == PullRequestState.Merged)
        {
            return PullRequestMutationResult.Invalid;
        }

        var pullRequest = await FindPullRequestAsync(repositoryId, number, cancellationToken);
        if (pullRequest is null)
        {
            return PullRequestMutationResult.NotFound;
        }

        if (!await CanReadAsUserAsync(repositoryId, actorUserId, cancellationToken)
            || (!isOwner && pullRequest.AuthorUserId != actorUserId))
        {
            return PullRequestMutationResult.Forbidden;
        }

        if (pullRequest.State == PullRequestState.Merged)
        {
            return PullRequestMutationResult.Invalid;
        }

        if (pullRequest.State == state)
        {
            return PullRequestMutationResult.Succeeded;
        }

        if (state == PullRequestState.Open)
        {
            var openPairKey = BuildOpenPairKey(pullRequest.SourceBranch, pullRequest.TargetBranch);
            if (await _dbContext.PullRequests.AsNoTracking().AnyAsync(
                item => item.RepositoryId == repositoryId
                    && item.Id != pullRequest.Id
                    && item.ActivePairKey == openPairKey,
                cancellationToken))
            {
                return PullRequestMutationResult.Duplicate;
            }

            pullRequest.ActivePairKey = openPairKey;
            pullRequest.ClosedAtUtc = null;
        }
        else
        {
            pullRequest.ActivePairKey = $"closed:{pullRequest.Number}";
            pullRequest.ClosedAtUtc = UtcNow;
        }

        pullRequest.State = state;
        Touch(pullRequest);
        pullRequest.Timeline.Add(NewEvent(
            state == PullRequestState.Open ? PullRequestEventType.Reopened : PullRequestEventType.Closed,
            actorUserId,
            UtcNow));
        return await SaveMutationAsync(cancellationToken);
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private Task<GitCandyPullRequest?> FindPullRequestAsync(
        long repositoryId,
        long number,
        CancellationToken cancellationToken) =>
        _dbContext.PullRequests.Include(item => item.Timeline).SingleOrDefaultAsync(
            item => item.RepositoryId == repositoryId && item.Number == number,
            cancellationToken);

    private Task<string?> GetStorageNameAsync(long repositoryId, CancellationToken cancellationToken) =>
        _dbContext.Repositories.AsNoTracking()
            .Where(item => item.Id == repositoryId)
            .Select(item => item.StorageName)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<(string NamespaceSlug, string RepositorySlug)?> GetAddressAsync(
        long repositoryId,
        CancellationToken cancellationToken)
    {
        var value = await _dbContext.Repositories.AsNoTracking()
            .Where(item => item.Id == repositoryId)
            .Select(item => new ValueTuple<string, string>(item.Namespace!.Slug, item.Name))
            .SingleOrDefaultAsync(cancellationToken);
        return string.IsNullOrEmpty(value.Item1) ? null : value;
    }

    private async Task<bool> CanReadAsUserAsync(
        long repositoryId,
        string userId,
        CancellationToken cancellationToken) =>
        await _permissionQuery.CanReadRepositoryAsync(
            repositoryId,
            userId,
            await IsAdministratorAsync(userId, cancellationToken),
            cancellationToken);

    private async Task<bool> CanWriteAsUserAsync(
        long repositoryId,
        string userId,
        CancellationToken cancellationToken) =>
        await _permissionQuery.CanWriteRepositoryAsync(
            repositoryId,
            userId,
            await IsAdministratorAsync(userId, cancellationToken),
            cancellationToken);

    private Task<bool> IsAdministratorAsync(string userId, CancellationToken cancellationToken) =>
        (from userRole in _dbContext.UserRoles.AsNoTracking()
         join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
         where userRole.UserId == userId && role.NormalizedName == "ADMINISTRATOR"
         select userRole).AnyAsync(cancellationToken);

    private void Touch(GitCandyPullRequest pullRequest)
    {
        pullRequest.UpdatedAtUtc = UtcNow;
        pullRequest.Version++;
    }

    private async Task<PullRequestMutationResult> SaveMutationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return PullRequestMutationResult.Succeeded;
        }
        catch (DbUpdateConcurrencyException)
        {
            return PullRequestMutationResult.Conflict;
        }
        catch (DbUpdateException)
        {
            return PullRequestMutationResult.Conflict;
        }
    }

    private static PullRequestTimelineItem[] EmptyTimeline => [];

    private static PullRequestDetails ToDetails(
        GitCandyPullRequest item,
        IReadOnlyList<PullRequestTimelineItem>? timeline = null) =>
        new(
            item.Id,
            item.RepositoryId,
            item.Number,
            item.Title,
            item.BodyMarkdown,
            item.BodyHtml,
            item.State,
            item.IsDraft,
            item.AuthorUserId,
            item.Author?.UserName ?? string.Empty,
            item.SourceBranch,
            item.TargetBranch,
            item.OriginalBaseSha,
            item.OriginalHeadSha,
            item.CurrentBaseSha,
            item.CurrentHeadSha,
            item.CreatedAtUtc,
            item.UpdatedAtUtc,
            item.ClosedAtUtc,
            item.MergedAtUtc,
            item.MergeCommitSha,
            item.Version,
            timeline ?? EmptyTimeline);

    private static GitCandyPullRequestTimelineEvent NewEvent(
        PullRequestEventType type,
        string actorUserId,
        DateTime createdAtUtc,
        string? detail = null) =>
        new()
        {
            Type = type,
            ActorUserId = actorUserId,
            CreatedAtUtc = createdAtUtc,
            Detail = detail
        };

    private static string NormalizeBranch(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.Length > SchemaLimits.GitRefName)
        {
            throw new PullRequestValidationException(
                PullRequestMutationResult.Invalid,
                "A source and target branch are required.");
        }

        return branch.Trim();
    }

    private static string BuildOpenPairKey(string sourceBranch, string targetBranch) =>
        $"open:{sourceBranch.Length}:{sourceBranch}{targetBranch}";

    private static void ValidateTitleAndBody(string title, string body)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > SchemaLimits.IssueTitle)
        {
            throw new PullRequestValidationException(
                PullRequestMutationResult.Invalid,
                $"Title must contain 1 to {SchemaLimits.IssueTitle} characters.");
        }

        if (body.Length > SchemaLimits.IssueBody)
        {
            throw new PullRequestValidationException(
                PullRequestMutationResult.Invalid,
                $"Body cannot exceed {SchemaLimits.IssueBody} characters.");
        }
    }

    private static bool IsTransientCreateConflict(Exception exception) =>
        exception is DbUpdateException
        || (string.Equals(exception.GetType().FullName, "Microsoft.Data.Sqlite.SqliteException", StringComparison.Ordinal)
            && (exception.Message.Contains("locked", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("busy", StringComparison.OrdinalIgnoreCase)));
}
