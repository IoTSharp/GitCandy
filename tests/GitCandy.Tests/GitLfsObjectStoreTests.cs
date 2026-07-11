using System.Security.Cryptography;
using GitCandy.Configuration;
using GitCandy.Git;
using Microsoft.Extensions.Options;

namespace GitCandy.Tests;

[TestClass]
public sealed class GitLfsObjectStoreTests
{
    [TestMethod]
    public async Task WriteAsync_WithMatchingSha256_AtomicallyStoresAndStreamsObject()
    {
        var rootPath = TestDirectory.Create();
        try
        {
            var content = RandomNumberGenerator.GetBytes(128 * 1024);
            var oid = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            var store = CreateStore(rootPath, maxObjectBytes: content.Length * 2L);

            var result = await store.WriteAsync(
                "lfs-demo",
                oid,
                content.Length,
                new MemoryStream(content));

            Assert.AreEqual(oid, result.Oid);
            Assert.AreEqual(content.Length, result.Size);
            Assert.AreEqual(result, store.GetInfo("lfs-demo", oid));
            await using var input = store.OpenRead("lfs-demo", oid);
            await using var copy = new MemoryStream();
            await input.CopyToAsync(copy);
            CollectionAssert.AreEqual(content, copy.ToArray());
            Assert.IsFalse(Directory.EnumerateFiles(rootPath, "*.upload", SearchOption.AllDirectories).Any());
        }
        finally
        {
            TestDirectory.Delete(rootPath);
        }
    }

    [TestMethod]
    public async Task WriteAsync_WithHashSizeOrQuotaViolation_RejectsAndLeavesNoObject()
    {
        var rootPath = TestDirectory.Create();
        try
        {
            var content = RandomNumberGenerator.GetBytes(1024);
            var oid = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            var wrongOid = new string('a', 64);
            var store = CreateStore(rootPath, maxObjectBytes: 2048, quotaBytes: 1024);

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => store.WriteAsync(
                "lfs-demo",
                wrongOid,
                content.Length,
                new MemoryStream(content)));
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => store.WriteAsync(
                "lfs-demo",
                oid,
                content.Length - 1,
                new MemoryStream(content)));
            Assert.ThrowsExactly<ArgumentException>(() => store.GetInfo("../escape", oid));
            Assert.ThrowsExactly<ArgumentException>(() => store.GetInfo("lfs-demo", "not-an-oid"));

            var first = await store.WriteAsync("lfs-demo", oid, content.Length, new MemoryStream(content));
            Assert.AreEqual(content.Length, first.Size);
            Assert.IsFalse(store.CanStore("lfs-demo", 1));
        }
        finally
        {
            TestDirectory.Delete(rootPath);
        }
    }

    private static GitLfsObjectStore CreateStore(
        string rootPath,
        long maxObjectBytes,
        long quotaBytes = 0)
    {
        return new GitLfsObjectStore(
            new TestApplicationPaths(rootPath),
            Options.Create(new GitLfsOptions
            {
                MaxObjectBytes = maxObjectBytes,
                RepositoryQuotaBytes = quotaBytes,
                StreamBufferSize = 4096
            }));
    }

    private sealed class TestApplicationPaths(string rootPath) : IGitCandyApplicationPaths
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
