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
        var normalizedRepositoryName = repositoryName.Trim();
        var repositoryPath = _pathResolver.ResolveRepositoryPath(normalizedRepositoryName);
        if (!Directory.Exists(repositoryPath))
        {
            var dotGitPath = _pathResolver.ResolveRepositoryPath($"{normalizedRepositoryName}.git");
            if (Directory.Exists(dotGitPath))
            {
                repositoryPath = dotGitPath;
            }
        }

        return new GitRepositoryContext(
            normalizedRepositoryName,
            repositoryPath);
    }
}
