using GitCandy.Configuration;
using GitCandy.PullRequests;
using LibGit2Sharp;
using Microsoft.Extensions.Options;

namespace GitCandy.Git;

/// <summary>基于 LibGit2Sharp 的 PR 分支快照与内部 ref 实现。</summary>
public sealed class PullRequestGitRepository(
    IGitServiceFactory serviceFactory,
    IManagedGitRepositoryService repositoryService,
    IOptions<RepositoryBrowserOptions> browserOptions) : IPullRequestGitRepository
{
    private const string HeadsPrefix = "refs/heads/";
    private const string PullRequestRefPrefix = "refs/pull/";
    private readonly IGitServiceFactory _serviceFactory = serviceFactory;
    private readonly IManagedGitRepositoryService _repositoryService = repositoryService;
    private readonly RepositoryBrowserOptions _browserOptions = browserOptions.Value;

    /// <inheritdoc />
    public IReadOnlyList<PullRequestBranch> GetBranches(
        string repositoryStorageName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var repository = Open(repositoryStorageName);
        return repository.Branches
            .Where(static branch => !branch.IsRemote && branch.Tip is not null)
            .Select(static branch => new PullRequestBranch(
                branch.CanonicalName[HeadsPrefix.Length..],
                branch.Tip!.Id.Sha))
            .OrderBy(static branch => branch.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public PullRequestBranchComparison? CompareBranches(
        string repositoryStorageName,
        string sourceBranch,
        string targetBranch,
        CancellationToken cancellationToken = default)
    {
        var normalizedSource = NormalizeBranch(sourceBranch, nameof(sourceBranch));
        var normalizedTarget = NormalizeBranch(targetBranch, nameof(targetBranch));
        cancellationToken.ThrowIfCancellationRequested();
        using var repository = Open(repositoryStorageName);
        var source = repository.Branches[$"{HeadsPrefix}{normalizedSource}"];
        var target = repository.Branches[$"{HeadsPrefix}{normalizedTarget}"];
        if (source?.Tip is null || target?.Tip is null)
        {
            return null;
        }

        var divergence = repository.ObjectDatabase.CalculateHistoryDivergence(target.Tip, source.Tip);
        return new PullRequestBranchComparison(
            target.Tip.Id.Sha,
            source.Tip.Id.Sha,
            divergence?.BehindBy ?? 0,
            divergence?.AheadBy ?? 0);
    }

    /// <inheritdoc />
    public PullRequestChangeSet? ReadChangeSet(
        string repositoryStorageName,
        string baseSha,
        string headSha,
        int commitPage,
        int commitPageSize,
        bool includeFiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseSha);
        ArgumentException.ThrowIfNullOrWhiteSpace(headSha);
        if (commitPage < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(commitPage));
        }

        if (commitPageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(commitPageSize));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var repository = Open(repositoryStorageName);
        var baseCommit = repository.Lookup<Commit>(baseSha.Trim());
        var headCommit = repository.Lookup<Commit>(headSha.Trim());
        if (baseCommit is null || headCommit is null)
        {
            return null;
        }

        var mergeBase = repository.ObjectDatabase.FindMergeBase(baseCommit, headCommit);
        if (mergeBase is null)
        {
            return null;
        }

        var divergence = repository.ObjectDatabase.CalculateHistoryDivergence(baseCommit, headCommit);
        var commits = repository.Commits.QueryBy(new CommitFilter
            {
                IncludeReachableFrom = headCommit,
                ExcludeReachableFrom = mergeBase,
                SortBy = CommitSortStrategies.Time | CommitSortStrategies.Topological
            })
            .Skip((commitPage - 1) * commitPageSize)
            .Take(commitPageSize + 1)
            .Select(static commit => new PullRequestCommit(
                commit.Id.Sha,
                commit.Message,
                commit.MessageShort,
                commit.Author.Name,
                commit.Author.Email,
                commit.Author.When,
                commit.Parents.Select(static parent => parent.Id.Sha).ToArray()))
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var hasNextCommitPage = commits.Length > commitPageSize;
        var files = Array.Empty<PullRequestFileChange>();
        var diffTruncated = false;
        if (includeFiles)
        {
            (files, diffTruncated) = ReadDiff(repository, mergeBase.Tree, headCommit.Tree, cancellationToken);
        }

        return new PullRequestChangeSet(
            mergeBase.Id.Sha,
            baseCommit.Id.Sha,
            headCommit.Id.Sha,
            divergence?.BehindBy ?? 0,
            divergence?.AheadBy ?? 0,
            commitPage,
            commitPageSize,
            hasNextCommitPage,
            commits.Take(commitPageSize).ToArray(),
            files,
            diffTruncated);
    }

    /// <inheritdoc />
    public void UpdatePullRequestHead(
        string repositoryStorageName,
        long number,
        string headSha,
        CancellationToken cancellationToken = default)
    {
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(headSha);
        cancellationToken.ThrowIfCancellationRequested();
        using var repository = Open(repositoryStorageName);
        var commit = repository.Lookup<Commit>(headSha)
            ?? throw new InvalidOperationException("The Pull Request head commit does not exist.");
        var referenceName = $"{PullRequestRefPrefix}{number}/head";
        var existing = repository.Refs[referenceName];
        var created = existing is null;
        try
        {
            if (created)
            {
                repository.Refs.Add(referenceName, commit.Id);
            }
            else
            {
                repository.Refs.UpdateTarget(existing!, commit.Id.Sha);
            }

            // upload-pack can advertise these refs, while receive-pack rejects client writes.
            if (!repository.Config
                .Find("^receive\\.hideRefs$", ConfigurationLevel.Local)
                .Any(entry => string.Equals(entry.Value, PullRequestRefPrefix, StringComparison.Ordinal)))
            {
                repository.Config.Add("receive.hideRefs", PullRequestRefPrefix);
            }
        }
        catch
        {
            if (created && repository.Refs[referenceName] is Reference createdReference)
            {
                repository.Refs.Remove(createdReference);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public void DeletePullRequestHead(
        string repositoryStorageName,
        long number,
        CancellationToken cancellationToken = default)
    {
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var repository = Open(repositoryStorageName);
        var reference = repository.Refs[$"{PullRequestRefPrefix}{number}/head"];
        if (reference is not null)
        {
            repository.Refs.Remove(reference);
        }
    }

    private Repository Open(string repositoryStorageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryStorageName);
        var context = _serviceFactory.Create(repositoryStorageName);
        return new Repository(_repositoryService.ResolveExistingPath(context));
    }

    private (PullRequestFileChange[] Files, bool Truncated) ReadDiff(
        Repository repository,
        Tree oldTree,
        Tree newTree,
        CancellationToken cancellationToken)
    {
        var patch = repository.Diff.Compare<Patch>(
            oldTree,
            newTree,
            new CompareOptions { Similarity = SimilarityOptions.Renames });
        var files = new List<PullRequestFileChange>();
        var characters = 0;
        var truncated = false;
        foreach (var change in patch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (files.Count >= _browserOptions.MaxDiffFiles)
            {
                truncated = true;
                break;
            }

            var content = change.Patch;
            if (characters + content.Length > _browserOptions.MaxDiffCharacters)
            {
                content = null;
                truncated = true;
            }
            else
            {
                characters += content.Length;
            }

            files.Add(new PullRequestFileChange(
                change.Path,
                change.OldPath,
                change.Status.ToString(),
                change.IsBinaryComparison,
                change.LinesAdded,
                change.LinesDeleted,
                content));
        }

        return (files.ToArray(), truncated);
    }

    private static string NormalizeBranch(string branchName, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName, parameterName);
        var value = branchName.Trim();
        if (!Reference.IsValidName($"{HeadsPrefix}{value}"))
        {
            throw new ArgumentException("A valid short local branch name is required.", parameterName);
        }

        return value;
    }
}
