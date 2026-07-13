using GitCandy.Releases;
using LibGit2Sharp;

namespace GitCandy.Git;

/// <summary>通过 LibGit2Sharp 校验 tag 并解析其最终 commit。</summary>
public sealed class ReleaseTagResolver(
    IGitRepositoryPathResolver pathResolver,
    IManagedGitRepositoryService repositoryService) : IReleaseTagResolver
{
    public string? ResolveTag(
        string repositoryStorageName,
        string tagName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryStorageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        cancellationToken.ThrowIfCancellationRequested();
        var context = new GitRepositoryContext(
            repositoryStorageName,
            pathResolver.ResolveRepositoryPath(repositoryStorageName));
        var path = repositoryService.ResolveExistingPath(context);
        using var repository = new Repository(path);
        return repository.Tags[tagName.Trim()]?.Target.Peel<Commit>()?.Id.Sha;
    }
}
