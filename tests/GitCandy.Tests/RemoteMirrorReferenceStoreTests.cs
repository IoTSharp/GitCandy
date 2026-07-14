using GitCandy.Configuration;
using GitCandy.Git;
using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Tests;

[TestClass]
public sealed class RemoteMirrorReferenceStoreTests
{
    [TestMethod]
    public void ReferenceStore_WithStagingRefs_AppliesPublicRefsAndCleansNamespace()
    {
        var rootPath = TestDirectory.Create();
        try
        {
            var workPath = Path.Combine(rootPath, "work");
            Repository.Init(workPath);
            string firstObjectId;
            string secondObjectId;
            using (var work = new Repository(workPath))
            {
                var signature = new Signature("GitCandy Test", "test@gitcandy.local", DateTimeOffset.UtcNow);
                File.WriteAllText(Path.Combine(workPath, "README.md"), "first");
                Commands.Stage(work, "README.md");
                firstObjectId = work.Commit("first", signature, signature).Id.Sha;
                File.WriteAllText(Path.Combine(workPath, "README.md"), "second");
                Commands.Stage(work, "README.md");
                secondObjectId = work.Commit("second", signature, signature).Id.Sha;
            }

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IGitCandyApplicationPaths>(new FixtureApplicationPaths(rootPath));
            services.AddGitCandyGit();
            using var provider = services.BuildServiceProvider(validateScopes: true);
            var managed = provider.GetRequiredService<IManagedGitRepositoryService>();
            var repository = managed.CloneBare(workPath, "mirror-reference-store");
            var store = provider.GetRequiredService<IRemoteMirrorReferenceStore>();
            var stagePrefix = RemoteMirrorReferenceNamespace.CreatePrefix(9);
            using (var git = new Repository(managed.ResolveExistingPath(repository)))
            {
                git.Refs.Add(stagePrefix + "heads/main", secondObjectId);
            }

            var staged = store.ReadReferences(repository, stagePrefix);
            Assert.AreEqual(secondObjectId, staged[stagePrefix + "heads/main"]);
            Assert.IsTrue(store.IsAncestor(repository, firstObjectId, secondObjectId));
            Assert.IsFalse(store.IsAncestor(repository, secondObjectId, firstObjectId));

            store.ApplyUpdates(
                repository,
                [new RemoteMirrorReferenceUpdate("refs/heads/release", firstObjectId)]);
            Assert.AreEqual(
                firstObjectId,
                store.ReadReferences(repository, "refs/heads/")["refs/heads/release"]);

            store.DeleteNamespace(repository, stagePrefix);
            Assert.AreEqual(0, store.ReadReferences(repository, stagePrefix).Count);
        }
        finally
        {
            TestDirectory.Delete(rootPath);
        }
    }

    private sealed class FixtureApplicationPaths(string rootPath) : IGitCandyApplicationPaths
    {
        public string ContentRootPath { get; } = Path.GetFullPath(rootPath);
        public string? WebRootPath => null;
        public string UserConfigurationPath => Path.Combine(ContentRootPath, "config.xml");
        public string RepositoryPath => Path.Combine(ContentRootPath, "repositories");
        public string CachePath => Path.Combine(ContentRootPath, "cache");
        public string GitCorePath => string.Empty;
        public string SshHostKeyPath => Path.Combine(ContentRootPath, "ssh-key.xml");
        public string DataProtectionKeysPath => Path.Combine(ContentRootPath, "keys");

        public string ResolveContentPath(string configuredPath) => Resolve(ContentRootPath, configuredPath);

        public string ResolveWebRootPath(string configuredPath) => throw new InvalidOperationException();

        public string ResolvePathWithinRepositoryRoot(string path) => Resolve(RepositoryPath, path);

        public string ResolvePathWithinCacheRoot(string path) => Resolve(CachePath, path);

        private static string Resolve(string root, string path)
        {
            var normalizedRoot = Path.GetFullPath(root);
            var resolved = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(path, normalizedRoot);
            var relative = Path.GetRelativePath(normalizedRoot, resolved);
            if (relative.Equals("..", StringComparison.Ordinal)
                || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.IsPathRooted(relative))
            {
                throw new InvalidOperationException("The fixture path escaped its root.");
            }

            return resolved;
        }
    }
}
