using GitCandy.PullRequests;
using LibGit2Sharp;

namespace GitCandy.Git;

/// <summary>基于 LibGit2Sharp 的 PR 分支快照与内部 ref 实现。</summary>
public sealed class PullRequestGitRepository(
    IGitServiceFactory serviceFactory,
    IManagedGitRepositoryService repositoryService) : IPullRequestGitRepository
{
    private const string HeadsPrefix = "refs/heads/";
    private const string PullRequestRefPrefix = "refs/pull/";
    private readonly IGitServiceFactory _serviceFactory = serviceFactory;
    private readonly IManagedGitRepositoryService _repositoryService = repositoryService;

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
