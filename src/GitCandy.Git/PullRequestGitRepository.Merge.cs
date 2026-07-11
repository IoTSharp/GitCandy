using System.Collections.Concurrent;
using GitCandy.PullRequests;
using LibGit2Sharp;

namespace GitCandy.Git;

public sealed partial class PullRequestGitRepository
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepositoryMergeLocks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public PullRequestBranchComparison? CompareBranches(
        string sourceRepositoryStorageName,
        string sourceBranch,
        string targetRepositoryStorageName,
        string targetBranch,
        CancellationToken cancellationToken = default)
    {
        var normalizedSource = NormalizeBranch(sourceBranch, nameof(sourceBranch));
        var normalizedTarget = NormalizeBranch(targetBranch, nameof(targetBranch));
        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(sourceRepositoryStorageName, targetRepositoryStorageName, StringComparison.OrdinalIgnoreCase))
        {
            return CompareBranches(sourceRepositoryStorageName, normalizedSource, normalizedTarget, cancellationToken);
        }

        using var sourceRepository = Open(sourceRepositoryStorageName);
        using var targetRepository = Open(targetRepositoryStorageName);
        var source = sourceRepository.Branches[$"{HeadsPrefix}{normalizedSource}"]?.Tip;
        var target = targetRepository.Branches[$"{HeadsPrefix}{normalizedTarget}"]?.Tip;
        if (source is null || target is null)
        {
            return null;
        }

        // Forks contain their ancestor objects, so divergence can be calculated in the source ODB.
        var sourceTarget = sourceRepository.Lookup<Commit>(target.Id);
        if (sourceTarget is null)
        {
            return null;
        }

        var divergence = sourceRepository.ObjectDatabase.CalculateHistoryDivergence(sourceTarget, source);
        return new PullRequestBranchComparison(
            target.Id.Sha,
            source.Id.Sha,
            divergence?.BehindBy ?? 0,
            divergence?.AheadBy ?? 0);
    }

    /// <inheritdoc />
    public void UpdatePullRequestHead(
        string sourceRepositoryStorageName,
        string sourceBranch,
        string targetRepositoryStorageName,
        long number,
        string expectedHeadSha,
        CancellationToken cancellationToken = default)
    {
        var normalizedSource = NormalizeBranch(sourceBranch, nameof(sourceBranch));
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHeadSha);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(sourceRepositoryStorageName, targetRepositoryStorageName, StringComparison.OrdinalIgnoreCase))
        {
            using var repository = Open(sourceRepositoryStorageName);
            var currentHead = repository.Branches[$"{HeadsPrefix}{normalizedSource}"]?.Tip?.Id.Sha;
            if (!string.Equals(currentHead, expectedHeadSha, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The Pull Request source branch changed before its head ref was updated.");
            }

            UpdatePullRequestHead(targetRepositoryStorageName, number, expectedHeadSha, cancellationToken);
            return;
        }

        var sourcePath = _repositoryService.ResolveExistingPath(_serviceFactory.Create(sourceRepositoryStorageName));
        using var targetRepository = Open(targetRepositoryStorageName);
        var incomingRef = $"{PullRequestRefPrefix}{number}/incoming-{Guid.NewGuid():N}";
        var remoteName = $"gitcandy-pr-{Guid.NewGuid():N}";
        targetRepository.Network.Remotes.Add(remoteName, sourcePath);
        try
        {
            Commands.Fetch(
                targetRepository,
                remoteName,
                [$"+{HeadsPrefix}{normalizedSource}:{incomingRef}"],
                new FetchOptions { Prune = false },
                "Import Pull Request head");
            cancellationToken.ThrowIfCancellationRequested();
            var imported = targetRepository.Refs[incomingRef];
            if (!string.Equals(imported?.TargetIdentifier, expectedHeadSha, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The Pull Request source branch changed while its objects were imported.");
            }

            SetPullRequestHead(targetRepository, number, expectedHeadSha);
        }
        finally
        {
            if (targetRepository.Refs[incomingRef] is not null)
            {
                targetRepository.Refs.Remove(incomingRef);
            }

            targetRepository.Network.Remotes.Remove(remoteName);
        }
    }

    /// <inheritdoc />
    public PullRequestGitMergeability EvaluateMergeability(
        string sourceRepositoryStorageName,
        string sourceBranch,
        string targetRepositoryStorageName,
        string targetBranch,
        long number,
        string expectedBaseSha,
        string expectedHeadSha,
        CancellationToken cancellationToken = default)
    {
        var normalizedSource = NormalizeBranch(sourceBranch, nameof(sourceBranch));
        var normalizedTarget = NormalizeBranch(targetBranch, nameof(targetBranch));
        cancellationToken.ThrowIfCancellationRequested();
        using var sourceRepository = Open(sourceRepositoryStorageName);
        using var targetRepository = Open(targetRepositoryStorageName);
        var source = sourceRepository.Branches[$"{HeadsPrefix}{normalizedSource}"]?.Tip;
        var target = targetRepository.Branches[$"{HeadsPrefix}{normalizedTarget}"]?.Tip;
        var pullHead = targetRepository.Lookup<Commit>($"{PullRequestRefPrefix}{number}/head");
        var sourceMatches = source is not null
            && pullHead is not null
            && string.Equals(source.Id.Sha, expectedHeadSha, StringComparison.Ordinal)
            && string.Equals(pullHead.Id.Sha, expectedHeadSha, StringComparison.Ordinal);
        var targetMatches = target is not null
            && string.Equals(target.Id.Sha, expectedBaseSha, StringComparison.Ordinal);
        var hasConflicts = false;
        if (pullHead is not null && target is not null)
        {
            hasConflicts = !targetRepository.ObjectDatabase.CanMergeWithoutConflict(target, pullHead);
        }

        return new PullRequestGitMergeability(
            source is not null,
            target is not null,
            sourceMatches,
            targetMatches,
            hasConflicts,
            source?.Id.Sha,
            target?.Id.Sha);
    }

    /// <inheritdoc />
    public PullRequestGitMergeResult Merge(
        string sourceRepositoryStorageName,
        string sourceBranch,
        string targetRepositoryStorageName,
        string targetBranch,
        long number,
        string expectedBaseSha,
        string expectedHeadSha,
        PullRequestMergeMethod method,
        string message,
        string actorName,
        string actorEmail,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        var normalizedTarget = NormalizeBranch(targetBranch, nameof(targetBranch));
        var repositoryLock = GetMergeLock(targetRepositoryStorageName);
        repositoryLock.Wait(cancellationToken);
        try
        {
            UpdatePullRequestHead(
                sourceRepositoryStorageName,
                sourceBranch,
                targetRepositoryStorageName,
                number,
                expectedHeadSha,
                cancellationToken);
            using var repository = Open(targetRepositoryStorageName);
            var targetReference = repository.Refs[$"{HeadsPrefix}{normalizedTarget}"];
            var target = targetReference is null ? null : repository.Lookup<Commit>(targetReference.TargetIdentifier);
            var head = repository.Lookup<Commit>($"{PullRequestRefPrefix}{number}/head");
            if (target is null || head is null)
            {
                return new PullRequestGitMergeResult(PullRequestMutationResult.BranchNotFound);
            }

            if (!string.Equals(target.Id.Sha, expectedBaseSha, StringComparison.Ordinal)
                || !string.Equals(head.Id.Sha, expectedHeadSha, StringComparison.Ordinal))
            {
                return new PullRequestGitMergeResult(PullRequestMutationResult.Conflict);
            }

            var mergeTree = repository.ObjectDatabase.MergeCommits(target, head, new MergeTreeOptions());
            if (mergeTree.Status == MergeTreeStatus.Conflicts)
            {
                return new PullRequestGitMergeResult(PullRequestMutationResult.Conflict);
            }

            var signature = new Signature(actorName, actorEmail, timestamp);
            var parents = method == PullRequestMergeMethod.MergeCommit
                ? new[] { target, head }
                : new[] { target };
            var commit = repository.ObjectDatabase.CreateCommit(
                signature,
                signature,
                message,
                mergeTree.Tree,
                parents,
                prettifyMessage: true);

            // Re-read immediately before the atomic lock-file ref update.
            targetReference = repository.Refs[$"{HeadsPrefix}{normalizedTarget}"];
            if (!string.Equals(targetReference?.TargetIdentifier, expectedBaseSha, StringComparison.Ordinal))
            {
                return new PullRequestGitMergeResult(PullRequestMutationResult.Conflict);
            }

            repository.Refs.UpdateTarget(targetReference, commit.Id, $"GitCandy PR #{number} {method}");
            return new PullRequestGitMergeResult(PullRequestMutationResult.Succeeded, commit.Id.Sha);
        }
        catch (LibGit2SharpException)
        {
            return new PullRequestGitMergeResult(PullRequestMutationResult.Conflict);
        }
        finally
        {
            repositoryLock.Release();
        }
    }

    /// <inheritdoc />
    public bool RollbackMerge(
        string targetRepositoryStorageName,
        string targetBranch,
        string mergeCommitSha,
        string previousTargetSha,
        CancellationToken cancellationToken = default)
    {
        var normalizedTarget = NormalizeBranch(targetBranch, nameof(targetBranch));
        var repositoryLock = GetMergeLock(targetRepositoryStorageName);
        repositoryLock.Wait(cancellationToken);
        try
        {
            using var repository = Open(targetRepositoryStorageName);
            var reference = repository.Refs[$"{HeadsPrefix}{normalizedTarget}"];
            if (!string.Equals(reference?.TargetIdentifier, mergeCommitSha, StringComparison.Ordinal)
                || repository.Lookup<Commit>(previousTargetSha) is null)
            {
                return false;
            }

            repository.Refs.UpdateTarget(reference, previousTargetSha, "Rollback incomplete GitCandy PR merge");
            return true;
        }
        finally
        {
            repositoryLock.Release();
        }
    }

    private static SemaphoreSlim GetMergeLock(string repositoryStorageName) =>
        RepositoryMergeLocks.GetOrAdd(repositoryStorageName, static _ => new SemaphoreSlim(1, 1));

    private static void SetPullRequestHead(Repository repository, long number, string headSha)
    {
        var referenceName = $"{PullRequestRefPrefix}{number}/head";
        var existing = repository.Refs[referenceName];
        if (existing is null)
        {
            repository.Refs.Add(referenceName, headSha);
        }
        else
        {
            repository.Refs.UpdateTarget(existing, headSha);
        }

        if (!repository.Config.Find("^receive\\.hideRefs$", ConfigurationLevel.Local)
            .Any(entry => string.Equals(entry.Value, PullRequestRefPrefix, StringComparison.Ordinal)))
        {
            repository.Config.Add("receive.hideRefs", PullRequestRefPrefix);
        }
    }
}
