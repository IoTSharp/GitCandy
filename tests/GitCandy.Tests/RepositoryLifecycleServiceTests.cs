using System.Text;
using GitCandy.Application;
using GitCandy.Configuration;
using GitCandy.Git;
using LibGit2Sharp;
using Microsoft.Extensions.Options;

namespace GitCandy.Tests;

[TestClass]
public sealed class RepositoryLifecycleServiceTests
{
    [TestMethod]
    public async Task CreateAsync_WithFork_PersistsNetworkClonesObjectsAndDeletesSafely()
    {
        var rootPath = TestDirectory.Create();
        try
        {
            var repositoryRoot = Path.Combine(rootPath, "repositories");
            Directory.CreateDirectory(repositoryRoot);
            CreateSourceRepository(Path.Combine(repositoryRoot, "upstream.git"));
            var management = new InMemoryRepositoryManagementService();
            management.AddExisting(new RepositoryDetails(
                "upstream",
                "source",
                false,
                true,
                false,
                DateTime.UtcNow,
                null,
                null,
                [],
                []));
            var lifecycle = CreateService(rootPath, management);

            var result = await lifecycle.CreateAsync(new RepositoryCreation(
                new RepositoryEdit("forked", "fork", false, true, false),
                "owner-id",
                RepositoryCreationMode.Fork,
                "upstream"));

            Assert.IsTrue(result.Succeeded, result.Error);
            var details = await management.GetRepositoryAsync("forked");
            Assert.IsNotNull(details);
            Assert.AreEqual("upstream", details.ForkedFromRepository);
            Assert.AreEqual("upstream", details.ForkNetworkRoot);
            var forkPath = Path.Combine(repositoryRoot, "forked.git");
            Assert.IsTrue(Repository.IsValid(forkPath));
            using (var fork = new Repository(forkPath))
            {
                Assert.IsNotNull(fork.Lookup<Commit>("refs/heads/main"));
            }

            Assert.IsTrue(await lifecycle.SetDefaultBranchAsync("forked", "main"));
            Assert.IsFalse(await lifecycle.SetDefaultBranchAsync("forked", "missing"));
            Assert.IsTrue(await lifecycle.DeleteAsync("forked"));
            Assert.IsFalse(Directory.Exists(forkPath));
            Assert.IsNull(await management.GetRepositoryAsync("forked"));
        }
        finally
        {
            TestDirectory.Delete(rootPath);
        }
    }

    [TestMethod]
    public async Task CreateAsync_WithUnsafeImportSource_RejectsWithoutMetadataOrDirectory()
    {
        var rootPath = TestDirectory.Create();
        try
        {
            var management = new InMemoryRepositoryManagementService();
            var lifecycle = CreateService(rootPath, management);
            var result = await lifecycle.CreateAsync(new RepositoryCreation(
                new RepositoryEdit("imported", "import", false, true, false),
                "owner-id",
                RepositoryCreationMode.Import,
                new Uri(Path.Combine(rootPath, "source.git")).AbsoluteUri));

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(await management.GetRepositoryAsync("imported"));
            Assert.IsFalse(Directory.Exists(Path.Combine(rootPath, "repositories", "imported.git")));
        }
        finally
        {
            TestDirectory.Delete(rootPath);
        }
    }

    private static RepositoryLifecycleService CreateService(
        string rootPath,
        IRepositoryManagementService management)
    {
        var applicationPaths = new LifecycleApplicationPaths(rootPath);
        var resolver = new GitRepositoryPathResolver(applicationPaths);
        var managed = new LibGit2RepositoryService(resolver);
        var lfs = new GitLfsObjectStore(
            applicationPaths,
            Options.Create(new GitLfsOptions { StreamBufferSize = 4096 }));
        return new RepositoryLifecycleService(management, managed, resolver, lfs);
    }

    private static void CreateSourceRepository(string path)
    {
        Repository.Init(path, isBare: true);
        using var bare = new Repository(path);
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("source\n"));
        var blob = bare.ObjectDatabase.CreateBlob(content);
        var definition = new TreeDefinition();
        definition.Add("README.md", blob, Mode.NonExecutableFile);
        var tree = bare.ObjectDatabase.CreateTree(definition);
        var signature = new Signature("GitCandy", "gitcandy@example.com", DateTimeOffset.UtcNow);
        var commit = bare.ObjectDatabase.CreateCommit(
            signature,
            signature,
            "Initial",
            tree,
            [],
            prettifyMessage: true);
        bare.Refs.Add("refs/heads/main", commit.Id);
        bare.Refs.UpdateTarget(bare.Refs.Head, "refs/heads/main");
    }

    private sealed class InMemoryRepositoryManagementService : IRepositoryManagementService
    {
        private readonly Dictionary<string, RepositoryDetails> _repositories =
            new(StringComparer.OrdinalIgnoreCase);

        public void AddExisting(RepositoryDetails details) => _repositories.Add(details.Name, details);

        public Task<RepositoryDetails?> GetRepositoryAsync(
            string repositoryName,
            CancellationToken cancellationToken = default)
        {
            _repositories.TryGetValue(repositoryName, out var details);
            return Task.FromResult(details);
        }

        public Task<bool> CreateRepositoryAsync(
            RepositoryEdit command,
            string creatorUserId,
            CancellationToken cancellationToken = default)
        {
            if (_repositories.ContainsKey(command.Name))
            {
                return Task.FromResult(false);
            }

            _repositories.Add(command.Name, new RepositoryDetails(
                command.Name,
                command.Description,
                command.IsPrivate,
                command.AllowAnonymousRead,
                command.AllowAnonymousWrite,
                DateTime.UtcNow,
                command.ForkedFromRepository,
                command.ForkNetworkRoot,
                [],
                []));
            return Task.FromResult(true);
        }

        public Task<bool> UpdateRepositoryAsync(string repositoryName, RepositoryEdit command, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> DeleteRepositoryAsync(string repositoryName, CancellationToken cancellationToken = default) => Task.FromResult(_repositories.Remove(repositoryName));

        public Task<bool> SetUserRoleAsync(string repositoryName, string userName, RepositoryUserRoleAction action, bool value, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> SetTeamRoleAsync(string repositoryName, string teamName, RepositoryTeamRoleAction action, bool value, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class LifecycleApplicationPaths(string rootPath) : IGitCandyApplicationPaths
    {
        private readonly string _rootPath = Path.GetFullPath(rootPath);

        public string ContentRootPath => _rootPath;
        public string? WebRootPath => null;
        public string UserConfigurationPath => Path.Combine(_rootPath, "config.xml");
        public string RepositoryPath => Path.Combine(_rootPath, "repositories");
        public string CachePath => Path.Combine(_rootPath, "cache");
        public string GitCorePath => string.Empty;
        public string SshHostKeyPath => Path.Combine(_rootPath, "ssh.xml");
        public string DataProtectionKeysPath => Path.Combine(_rootPath, "keys");
        public string ResolveContentPath(string configuredPath) => Resolve(_rootPath, configuredPath);
        public string ResolveWebRootPath(string configuredPath) => throw new InvalidOperationException();
        public string ResolvePathWithinRepositoryRoot(string path) => Resolve(RepositoryPath, path);
        public string ResolvePathWithinCacheRoot(string path) => Resolve(CachePath, path);

        private static string Resolve(string root, string path)
        {
            var rootPath = Path.GetFullPath(root);
            var fullPath = Path.GetFullPath(path, rootPath);
            var relative = Path.GetRelativePath(rootPath, fullPath);
            if (relative.Equals("..", StringComparison.Ordinal)
                || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.IsPathRooted(relative))
            {
                throw new InvalidOperationException("Path escapes test root.");
            }

            return fullPath;
        }
    }
}
