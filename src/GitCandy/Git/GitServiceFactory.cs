namespace GitCandy.Git;

/// <summary>
/// 迁移期 Git 服务上下文工厂。
/// </summary>
public sealed class GitServiceFactory(IGitRepositoryPathResolver pathResolver) : IGitServiceFactory
{
    private readonly IGitRepositoryPathResolver _pathResolver = pathResolver;

    /// <inheritdoc />
    public GitRepositoryContext Create(string repositoryName)
    {
        var repositoryPath = _pathResolver.ResolveRepositoryPath(repositoryName);

        return new GitRepositoryContext(
            repositoryName.Trim(),
            repositoryPath);
    }
}
