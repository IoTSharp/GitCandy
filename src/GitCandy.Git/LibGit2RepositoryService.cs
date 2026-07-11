using LibGit2Sharp;

namespace GitCandy.Git;

/// <summary>
/// 基于 LibGit2Sharp 的仓库初始化、验证和只读元数据能力。
/// </summary>
public sealed class LibGit2RepositoryService(IGitRepositoryPathResolver pathResolver)
    : IManagedGitRepositoryService
{
    private readonly IGitRepositoryPathResolver _pathResolver = pathResolver;

    /// <inheritdoc />
    public string ResolveExistingPath(GitRepositoryContext repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var repositoryName = repository.RepositoryName.Trim();
        var legacyPath = _pathResolver.ResolveRepositoryPath(repositoryName);
        var dotGitPath = _pathResolver.ResolveRepositoryPath($"{repositoryName}.git");
        var configuredPath = Path.GetFullPath(repository.RepositoryPath);

        if (!PathsEqual(configuredPath, legacyPath)
            && !PathsEqual(configuredPath, dotGitPath))
        {
            throw new InvalidOperationException(
                "The Git repository context did not originate from the configured repository root.");
        }

        foreach (var candidate in EnumerateDistinctPaths(configuredPath, legacyPath, dotGitPath))
        {
            if (!Directory.Exists(candidate))
            {
                continue;
            }

            EnsureResolvedPathWithinRoot(candidate);
            if (Repository.IsValid(candidate))
            {
                return candidate;
            }
        }

        throw new GitRepositoryNotFoundException(repository.RepositoryName);
    }

    /// <inheritdoc />
    public GitRepositoryContext InitializeBare(string repositoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);

        var normalizedName = repositoryName.Trim();
        Directory.CreateDirectory(_pathResolver.RepositoryRootPath);
        EnsureResolvedPathWithinRoot(_pathResolver.RepositoryRootPath);
        var legacyPath = _pathResolver.ResolveRepositoryPath(normalizedName);
        var repositoryPath = _pathResolver.ResolveRepositoryPath($"{normalizedName}.git");
        if (Directory.Exists(legacyPath) || Directory.Exists(repositoryPath))
        {
            throw new InvalidOperationException("A repository directory with the same name already exists.");
        }

        Repository.Init(repositoryPath, isBare: true);
        return new GitRepositoryContext(normalizedName, repositoryPath);
    }

    /// <inheritdoc />
    public GitRepositoryContext CloneBare(
        string source,
        string repositoryName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedName = repositoryName.Trim();
        Directory.CreateDirectory(_pathResolver.RepositoryRootPath);
        EnsureResolvedPathWithinRoot(_pathResolver.RepositoryRootPath);
        var legacyPath = _pathResolver.ResolveRepositoryPath(normalizedName);
        var repositoryPath = _pathResolver.ResolveRepositoryPath($"{normalizedName}.git");
        if (Directory.Exists(legacyPath) || Directory.Exists(repositoryPath))
        {
            throw new InvalidOperationException("A repository directory with the same name already exists.");
        }

        var fetchOptions = new FetchOptions
        {
            OnTransferProgress = _ => !cancellationToken.IsCancellationRequested
        };
        var cloneOptions = new CloneOptions(fetchOptions)
        {
            IsBare = true,
            Checkout = false,
            RecurseSubmodules = false
        };
        try
        {
            Repository.Clone(source.Trim(), repositoryPath, cloneOptions);
            cancellationToken.ThrowIfCancellationRequested();
            return new GitRepositoryContext(normalizedName, repositoryPath);
        }
        catch
        {
            if (Directory.Exists(repositoryPath))
            {
                EnsureResolvedPathWithinRoot(repositoryPath);
                SafeDirectoryDeletion.Delete(repositoryPath);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public bool SetDefaultBranch(
        GitRepositoryContext repository,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedBranch = branchName.Trim();
        if (normalizedBranch.StartsWith("refs/", StringComparison.Ordinal)
            || normalizedBranch.Contains("..", StringComparison.Ordinal)
            || normalizedBranch.Any(char.IsControl))
        {
            throw new ArgumentException("A short local branch name is required.", nameof(branchName));
        }

        using var gitRepository = new Repository(ResolveExistingPath(repository));
        var canonicalName = $"refs/heads/{normalizedBranch}";
        if (gitRepository.Refs[canonicalName] is null)
        {
            return false;
        }

        gitRepository.Refs.UpdateTarget(gitRepository.Refs.Head, canonicalName);
        return true;
    }

    /// <inheritdoc />
    public GitRepositorySnapshot ReadSnapshot(
        GitRepositoryContext repository,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var repositoryPath = ResolveExistingPath(repository);
        using var gitRepository = new Repository(repositoryPath);

        var branches = gitRepository.Branches
            .Where(static branch => !branch.IsRemote)
            .Select(static branch => new GitReferenceSnapshot(
                branch.CanonicalName,
                branch.Tip?.Id.Sha))
            .OrderBy(static branch => branch.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var tags = gitRepository.Tags
            .Select(static tag => new GitReferenceSnapshot(
                tag.CanonicalName,
                tag.Target.Id.Sha))
            .OrderBy(static tag => tag.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        var headCommit = gitRepository.Head.Tip;
        var latestCommit = headCommit is null
            ? null
            : new GitCommitSnapshot(
                headCommit.Id.Sha,
                headCommit.MessageShort,
                headCommit.Author.Name,
                headCommit.Author.Email,
                headCommit.Author.When);

        return new GitRepositorySnapshot(
            gitRepository.Info.IsBare,
            gitRepository.Head.CanonicalName,
            headCommit?.Id.Sha,
            latestCommit,
            branches,
            tags);
    }

    private void EnsureResolvedPathWithinRoot(string repositoryPath)
    {
        var rootPath = ResolveFinalDirectoryTarget(_pathResolver.RepositoryRootPath);
        var resolvedRepositoryPath = ResolveFinalDirectoryTarget(repositoryPath);
        var relativePath = Path.GetRelativePath(rootPath, resolvedRepositoryPath);

        if (relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException(
                "The resolved Git repository path escapes the configured repository root.");
        }
    }

    private static string ResolveFinalDirectoryTarget(string path)
    {
        var directory = new DirectoryInfo(path);
        var target = directory.ResolveLinkTarget(returnFinalTarget: true);
        return Path.GetFullPath(target?.FullName ?? directory.FullName);
    }

    private static IEnumerable<string> EnumerateDistinctPaths(params string[] paths)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return paths.Distinct(comparer);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }
}
