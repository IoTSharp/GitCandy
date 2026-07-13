using GitCandy.Configuration;
using GitCandy.Git;

namespace GitCandy.Tests;

[TestClass]
public sealed class ReleaseAssetStoreTests
{
    [TestMethod]
    public async Task ReleaseAssetStore_WithBoundedContent_StoresInsideCacheAndCleansOrphans()
    {
        var root = TestDirectory.Create();
        try
        {
            var paths = new TestPaths(root);
            var store = new ReleaseAssetStore(paths);
            var assetId = new string('a', 32);
            await using var content = new MemoryStream([1, 2, 3, 4]);
            var stored = await store.StoreAsync(1, 2, assetId, content, maxBytes: 4);
            Assert.IsNotNull(stored);
            Assert.AreEqual(4, stored.Length);
            var file = Directory.EnumerateFiles(paths.CachePath, assetId, SearchOption.AllDirectories).Single();
            StringAssert.StartsWith(Path.GetFullPath(file), Path.GetFullPath(paths.CachePath));
            var read = await store.OpenReadAsync(1, 2, assetId);
            Assert.IsNotNull(read);
            Assert.AreEqual(4, read.Length);
            await read.DisposeAsync();

            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-2));
            Assert.AreEqual(1, await store.DeleteOrphansAsync(
                new HashSet<string>(StringComparer.Ordinal),
                DateTimeOffset.UtcNow.AddDays(-1)));
            Assert.IsFalse(File.Exists(file));
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.OpenReadAsync(1, 2, "../outside"));
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    [TestMethod]
    public async Task ReleaseAssetStore_WithOversizedContent_DeletesTemporaryFile()
    {
        var root = TestDirectory.Create();
        try
        {
            var paths = new TestPaths(root);
            var store = new ReleaseAssetStore(paths);
            await using var content = new MemoryStream([1, 2, 3, 4, 5]);

            Assert.IsNull(await store.StoreAsync(1, 2, new string('b', 32), content, maxBytes: 4));
            Assert.IsFalse(Directory.Exists(paths.CachePath)
                && Directory.EnumerateFiles(paths.CachePath, "*", SearchOption.AllDirectories).Any());
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    private sealed class TestPaths(string root) : IGitCandyApplicationPaths
    {
        public string ContentRootPath { get; } = Path.GetFullPath(root);
        public string? WebRootPath => null;
        public string UserConfigurationPath => Path.Combine(ContentRootPath, "config.xml");
        public string RepositoryPath => Path.Combine(ContentRootPath, "repos");
        public string CachePath => Path.Combine(ContentRootPath, "cache");
        public string GitCorePath => string.Empty;
        public string SshHostKeyPath => Path.Combine(ContentRootPath, "ssh.xml");
        public string DataProtectionKeysPath => Path.Combine(ContentRootPath, "keys");

        public string ResolveContentPath(string configuredPath) => Path.GetFullPath(configuredPath, ContentRootPath);
        public string ResolveWebRootPath(string configuredPath) => throw new InvalidOperationException();
        public string ResolvePathWithinRepositoryRoot(string path) => ResolveWithin(RepositoryPath, path);
        public string ResolvePathWithinCacheRoot(string path) => ResolveWithin(CachePath, path);

        private static string ResolveWithin(string rootPath, string path)
        {
            var root = Path.GetFullPath(rootPath);
            var fullPath = Path.GetFullPath(path, root);
            var relative = Path.GetRelativePath(root, fullPath);
            if (Path.IsPathRooted(relative)
                || relative.Equals("..", StringComparison.Ordinal)
                || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Path escaped test root.");
            }
            return fullPath;
        }
    }
}
