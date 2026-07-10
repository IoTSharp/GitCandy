using GitCandy.Git;
using LibGit2Sharp;

namespace GitCandy.Tests;

[TestClass]
public sealed class ManagedGitRepositoryServiceTests
{
    [TestMethod]
    public void InitializeBare_WithNewRepository_CreatesValidBareRepositoryWithoutGitProcess()
    {
        var rootPath = TestDirectory.Create();
        try
        {
            var service = CreateService(rootPath);

            var repository = service.InitializeBare("managed-demo");
            var snapshot = service.ReadSnapshot(repository);

            Assert.IsTrue(Repository.IsValid(repository.RepositoryPath));
            Assert.IsTrue(snapshot.IsBare);
            Assert.IsNull(snapshot.HeadCommitId);
            Assert.IsNull(snapshot.LatestCommit);
            Assert.IsEmpty(snapshot.Branches);
            Assert.IsEmpty(snapshot.Tags);
            Assert.ThrowsExactly<InvalidOperationException>(
                () => service.InitializeBare("managed-demo"));
        }
        finally
        {
            TestDirectory.Delete(rootPath);
        }
    }

    [TestMethod]
    public void ReadSnapshot_WithCommitBranchAndTag_ReturnsRepositoryMetadata()
    {
        var rootPath = TestDirectory.Create();
        try
        {
            var repositoryPath = Path.Combine(rootPath, "history-demo");
            Repository.Init(repositoryPath);
            using (var repository = new Repository(repositoryPath))
            {
                var readmePath = Path.Combine(repositoryPath, "README.md");
                File.WriteAllText(readmePath, "# managed repository\n");
                Commands.Stage(repository, "README.md");
                var signature = new Signature(
                    "GitCandy M9",
                    "m9@gitcandy.local",
                    new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero));
                var commit = repository.Commit("M9 managed repository", signature, signature);
                repository.CreateBranch("feature-managed", commit);
                repository.ApplyTag("v1-managed", commit.Sha, signature, "M9 tag");
            }

            var service = CreateService(rootPath);
            var context = new GitRepositoryContext("history-demo", repositoryPath);
            var snapshot = service.ReadSnapshot(context);

            Assert.IsFalse(snapshot.IsBare);
            Assert.IsNotNull(snapshot.LatestCommit);
            Assert.AreEqual("M9 managed repository", snapshot.LatestCommit.MessageShort);
            Assert.AreEqual("GitCandy M9", snapshot.LatestCommit.AuthorName);
            Assert.IsTrue(snapshot.Branches.Any(branch =>
                branch.CanonicalName == "refs/heads/feature-managed"));
            Assert.IsTrue(snapshot.Tags.Any(tag =>
                tag.CanonicalName == "refs/tags/v1-managed"));
        }
        finally
        {
            TestDirectory.Delete(rootPath);
        }
    }

    [TestMethod]
    public void ResolveExistingPath_WithInvalidOrEscapedRepository_RejectsPath()
    {
        var rootPath = TestDirectory.Create();
        var outsidePath = TestDirectory.Create();
        try
        {
            var invalidPath = Path.Combine(rootPath, "invalid");
            Directory.CreateDirectory(invalidPath);
            var service = CreateService(rootPath);

            Assert.ThrowsExactly<GitRepositoryNotFoundException>(() =>
                service.ResolveExistingPath(new GitRepositoryContext("invalid", invalidPath)));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                service.ResolveExistingPath(new GitRepositoryContext("invalid", outsidePath)));
        }
        finally
        {
            TestDirectory.Delete(rootPath);
            TestDirectory.Delete(outsidePath);
        }
    }

    [TestMethod]
    public void NativeRuntime_WithCurrentPackage_LoadsBundledLibGit2()
    {
        var version = GlobalSettings.Version;

        Assert.IsFalse(string.IsNullOrWhiteSpace(version.InformationalVersion));
    }

    private static LibGit2RepositoryService CreateService(string rootPath)
    {
        return new LibGit2RepositoryService(new TestRepositoryPathResolver(rootPath));
    }

    private sealed class TestRepositoryPathResolver(string rootPath) : IGitRepositoryPathResolver
    {
        private readonly string _rootPath = Path.GetFullPath(rootPath);

        public string RepositoryRootPath => Path.EndsInDirectorySeparator(_rootPath)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        public string ResolveRepositoryPath(string repositoryName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
            if (repositoryName.IndexOfAny(['/', '\\']) >= 0)
            {
                throw new ArgumentException("Repository name must be one segment.", nameof(repositoryName));
            }

            return Path.GetFullPath(repositoryName, RepositoryRootPath);
        }
    }
}
