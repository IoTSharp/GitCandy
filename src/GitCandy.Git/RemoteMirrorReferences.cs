using LibGit2Sharp;

namespace GitCandy.Git;

/// <summary>GitCandy 内部 mirror staging refs 的保留命名空间。</summary>
public static class RemoteMirrorReferenceNamespace
{
    public const string Prefix = "refs/gitcandy/mirrors/";

    public static string CreatePrefix(long mirrorId)
    {
        if (mirrorId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mirrorId));
        }

        return $"{Prefix}{mirrorId}/";
    }
}

/// <summary>一次受控本地 ref 创建、更新或删除。</summary>
public sealed record RemoteMirrorReferenceUpdate(string ReferenceName, string? TargetObjectId);

/// <summary>读取和应用 mirror refs 的本地仓库边界。</summary>
public interface IRemoteMirrorReferenceStore
{
    IReadOnlyDictionary<string, string> ReadReferences(
        GitRepositoryContext repository,
        string referencePrefix,
        CancellationToken cancellationToken = default);

    bool IsAncestor(
        GitRepositoryContext repository,
        string ancestorObjectId,
        string descendantObjectId,
        CancellationToken cancellationToken = default);

    void ApplyUpdates(
        GitRepositoryContext repository,
        IReadOnlyList<RemoteMirrorReferenceUpdate> updates,
        CancellationToken cancellationToken = default);

    void DeleteNamespace(
        GitRepositoryContext repository,
        string referencePrefix,
        CancellationToken cancellationToken = default);
}

internal sealed class LibGit2RemoteMirrorReferenceStore(
    IManagedGitRepositoryService repositoryService) : IRemoteMirrorReferenceStore
{
    private readonly IManagedGitRepositoryService _repositoryService = repositoryService;

    public IReadOnlyDictionary<string, string> ReadReferences(
        GitRepositoryContext repository,
        string referencePrefix,
        CancellationToken cancellationToken = default)
    {
        ValidatePrefix(referencePrefix);
        cancellationToken.ThrowIfCancellationRequested();
        using var git = new Repository(_repositoryService.ResolveExistingPath(repository));
        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reference in git.Refs.Where(item =>
                     item.CanonicalName.StartsWith(referencePrefix, StringComparison.Ordinal)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var direct = reference.ResolveToDirectReference();
            references[reference.CanonicalName] = direct.TargetIdentifier;
        }

        return references;
    }

    public bool IsAncestor(
        GitRepositoryContext repository,
        string ancestorObjectId,
        string descendantObjectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ancestorObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descendantObjectId);
        if (string.Equals(ancestorObjectId, descendantObjectId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var git = new Repository(_repositoryService.ResolveExistingPath(repository));
        var ancestor = git.Lookup<Commit>(ancestorObjectId);
        var descendant = git.Lookup<Commit>(descendantObjectId);
        if (ancestor is null || descendant is null)
        {
            return false;
        }

        var divergence = git.ObjectDatabase.CalculateHistoryDivergence(ancestor, descendant);
        return divergence.AheadBy == 0;
    }

    public void ApplyUpdates(
        GitRepositoryContext repository,
        IReadOnlyList<RemoteMirrorReferenceUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count > 1024)
        {
            throw new ArgumentException("A mirror operation cannot update more than 1024 refs.", nameof(updates));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var git = new Repository(_repositoryService.ResolveExistingPath(repository));
        var headWasUnborn = git.Head.Tip is null;
        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidatePublicReference(update.ReferenceName);
            if (update.TargetObjectId is null)
            {
                if (git.Refs[update.ReferenceName] is not null)
                {
                    git.Refs.Remove(update.ReferenceName);
                }
                continue;
            }

            var target = git.Lookup(update.TargetObjectId)?.Id
                ?? throw new InvalidOperationException("A mirror ref target is not present in the local object database.");
            git.Refs.Add(update.ReferenceName, target, allowOverwrite: true);
        }

        if (headWasUnborn)
        {
            var defaultReference = SelectDefaultBranch(updates, git);
            if (defaultReference is not null)
            {
                git.Refs.UpdateTarget("HEAD", defaultReference);
            }
        }
    }

    public void DeleteNamespace(
        GitRepositoryContext repository,
        string referencePrefix,
        CancellationToken cancellationToken = default)
    {
        ValidatePrefix(referencePrefix);
        cancellationToken.ThrowIfCancellationRequested();
        using var git = new Repository(_repositoryService.ResolveExistingPath(repository));
        var references = git.Refs
            .Where(item => item.CanonicalName.StartsWith(referencePrefix, StringComparison.Ordinal))
            .Select(item => item.CanonicalName)
            .ToArray();
        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            git.Refs.Remove(reference);
        }
    }

    private static string? SelectDefaultBranch(
        IReadOnlyList<RemoteMirrorReferenceUpdate> updates,
        Repository git)
    {
        var branches = updates
            .Where(update => update.TargetObjectId is not null
                && update.ReferenceName.StartsWith("refs/heads/", StringComparison.Ordinal)
                && git.Refs[update.ReferenceName] is not null)
            .Select(update => update.ReferenceName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return branches.FirstOrDefault(branch => string.Equals(branch, "refs/heads/main", StringComparison.Ordinal))
            ?? branches.FirstOrDefault(branch => string.Equals(branch, "refs/heads/master", StringComparison.Ordinal))
            ?? branches.FirstOrDefault();
    }

    private static void ValidatePrefix(string referencePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referencePrefix);
        if (!referencePrefix.StartsWith("refs/", StringComparison.Ordinal)
            || !referencePrefix.EndsWith('/')
            || referencePrefix.Length > 240)
        {
            throw new ArgumentException("A fully qualified Git reference namespace is required.", nameof(referencePrefix));
        }
    }

    private static void ValidatePublicReference(string referenceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceName);
        if ((!referenceName.StartsWith("refs/heads/", StringComparison.Ordinal)
                && !referenceName.StartsWith("refs/tags/", StringComparison.Ordinal))
            || referenceName.StartsWith(RemoteMirrorReferenceNamespace.Prefix, StringComparison.Ordinal)
            || !Reference.IsValidName(referenceName))
        {
            throw new ArgumentException("Mirror updates are limited to valid branch and tag refs.", nameof(referenceName));
        }
    }
}
