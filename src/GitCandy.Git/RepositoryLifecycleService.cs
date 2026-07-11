using GitCandy.Application;

namespace GitCandy.Git;

/// <summary>
/// 协调 EF 元数据和仓库根目录内物理对象的生命周期服务。
/// </summary>
public sealed class RepositoryLifecycleService(
    IRepositoryManagementService repositoryManagementService,
    IManagedGitRepositoryService managedRepositoryService,
    IGitRepositoryPathResolver pathResolver,
    IGitLfsObjectStore lfsObjectStore) : IRepositoryLifecycleService
{
    private readonly IRepositoryManagementService _repositoryManagementService = repositoryManagementService;
    private readonly IManagedGitRepositoryService _managedRepositoryService = managedRepositoryService;
    private readonly IGitRepositoryPathResolver _pathResolver = pathResolver;
    private readonly IGitLfsObjectStore _lfsObjectStore = lfsObjectStore;

    /// <inheritdoc />
    public async Task<RepositoryLifecycleResult> CreateAsync(
        RepositoryCreation request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        GitRepositoryContext? repository = null;
        try
        {
            var storageName = string.IsNullOrWhiteSpace(request.Repository.StorageName)
                ? request.Repository.Name.Trim()
                : request.Repository.StorageName.Trim();
            if (Directory.Exists(_pathResolver.ResolveRepositoryPath(storageName))
                || Directory.Exists(_pathResolver.ResolveRepositoryPath($"{storageName}.git")))
            {
                storageName = $"r-{Guid.NewGuid():N}";
            }
            var metadata = request.Repository with { StorageName = storageName };
            switch (request.Mode)
            {
                case RepositoryCreationMode.Empty:
                    repository = _managedRepositoryService.InitializeBare(storageName);
                    break;
                case RepositoryCreationMode.Import:
                    if (!TryValidateRemoteSource(request.Source, out var remoteSource))
                    {
                        return new RepositoryLifecycleResult(false, "A valid HTTP(S), SSH or Git remote URL is required.");
                    }

                    repository = _managedRepositoryService.CloneBare(
                        remoteSource,
                        storageName,
                        cancellationToken);
                    break;
                case RepositoryCreationMode.Fork:
                    var sourceDetails = string.IsNullOrWhiteSpace(request.Source)
                        ? null
                        : await _repositoryManagementService.GetRepositoryAsync(
                            request.Source,
                            cancellationToken);
                    if (sourceDetails is null)
                    {
                        return new RepositoryLifecycleResult(false, "The source repository does not exist.");
                    }

                    var sourceContext = new GitRepositoryContext(
                        sourceDetails.StorageName,
                        _pathResolver.ResolveRepositoryPath(sourceDetails.StorageName));
                    var sourcePath = _managedRepositoryService.ResolveExistingPath(sourceContext);
                    repository = _managedRepositoryService.CloneBare(
                        sourcePath,
                        storageName,
                        cancellationToken);
                    metadata = metadata with
                    {
                        ForkedFromRepository = sourceDetails.Name,
                        ForkNetworkRoot = sourceDetails.ForkNetworkRoot ?? sourceDetails.Name
                    };
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }

            if (await _repositoryManagementService.CreateRepositoryAsync(
                metadata,
                request.CreatorUserId,
                cancellationToken))
            {
                return new RepositoryLifecycleResult(true);
            }

            DeletePhysicalRepository(repository);
            return new RepositoryLifecycleResult(false, "Repository metadata could not be created.");
        }
        catch (OperationCanceledException)
        {
            if (repository is not null)
            {
                DeletePhysicalRepository(repository);
            }

            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or LibGit2Sharp.LibGit2SharpException
                or IOException
                or UnauthorizedAccessException)
        {
            if (repository is not null)
            {
                DeletePhysicalRepository(repository);
            }

            return new RepositoryLifecycleResult(false, "The physical Git repository could not be created.");
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetDefaultBranchAsync(
        string repositoryName,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        var details = await _repositoryManagementService.GetRepositoryAsync(repositoryName, cancellationToken);
        if (details is null)
        {
            return false;
        }

        var context = CreateContext(details.StorageName);
        return _managedRepositoryService.SetDefaultBranch(
            context,
            branchName,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        string repositoryName,
        CancellationToken cancellationToken = default)
    {
        var details = await _repositoryManagementService.GetRepositoryAsync(
            repositoryName,
            cancellationToken);
        if (details is null)
        {
            return false;
        }

        string? originalPath = null;
        string? quarantinePath = null;
        try
        {
            originalPath = _managedRepositoryService.ResolveExistingPath(CreateContext(details.StorageName));
            quarantinePath = _pathResolver.ResolveRepositoryPath(
                $".deleting-{Guid.NewGuid():N}.git");
            Directory.Move(originalPath, quarantinePath);
        }
        catch (GitRepositoryNotFoundException)
        {
            // Metadata-only repositories from earlier milestones remain deletable.
        }

        try
        {
            if (!await _repositoryManagementService.DeleteRepositoryAsync(
                details.Name,
                cancellationToken))
            {
                RestoreQuarantinedRepository(originalPath, quarantinePath);
                return false;
            }

            if (quarantinePath is not null)
            {
                SafeDirectoryDeletion.Delete(quarantinePath);
            }

            await _lfsObjectStore.DeleteRepositoryAsync(details.StorageName, cancellationToken);

            return true;
        }
        catch
        {
            RestoreQuarantinedRepository(originalPath, quarantinePath);
            throw;
        }
    }

    private GitRepositoryContext CreateContext(string repositoryName)
    {
        return new GitRepositoryContext(
            repositoryName,
            _pathResolver.ResolveRepositoryPath(repositoryName));
    }

    private void DeletePhysicalRepository(GitRepositoryContext repository)
    {
        try
        {
            var path = _managedRepositoryService.ResolveExistingPath(repository);
            SafeDirectoryDeletion.Delete(path);
        }
        catch (GitRepositoryNotFoundException)
        {
        }
    }

    private static void RestoreQuarantinedRepository(string? originalPath, string? quarantinePath)
    {
        if (originalPath is not null
            && quarantinePath is not null
            && Directory.Exists(quarantinePath)
            && !Directory.Exists(originalPath))
        {
            Directory.Move(quarantinePath, originalPath);
        }
    }

    private static bool TryValidateRemoteSource(string? source, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(source)
            || !Uri.TryCreate(source.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https" or "ssh" or "git")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        value = uri.AbsoluteUri;
        return true;
    }
}
