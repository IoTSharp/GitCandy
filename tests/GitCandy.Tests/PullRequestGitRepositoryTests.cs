using System.Text;
using GitCandy.Configuration;
using GitCandy.Git;
using GitCandy.PullRequests;
using LibGit2Sharp;
using Microsoft.Extensions.Options;

namespace GitCandy.Tests;

[TestClass]
public sealed class PullRequestGitRepositoryTests
{
    [TestMethod]
    public void PullRequestHead_WithBareRepository_IsFetchableAndHiddenFromReceivePack()
    {
        var rootPath = TestDirectory.Create();
        try
        {
            var repositoryPath = CreateBareRepository(rootPath);
            var pathResolver = new PullRequestPathResolver(rootPath);
            var service = new PullRequestGitRepository(
                new GitServiceFactory(pathResolver),
                new LibGit2RepositoryService(pathResolver),
                Options.Create(new RepositoryBrowserOptions()));

            var branches = service.GetBranches("reviews");
            Assert.IsTrue(branches.Any(item => item.Name == "main"));
            Assert.IsTrue(branches.Any(item => item.Name == "feature"));
            var comparison = service.CompareBranches("reviews", "feature", "main");
            Assert.IsNotNull(comparison);
            Assert.AreEqual(1, comparison.AheadBy);
            var changes = service.ReadChangeSet(
                "reviews",
                comparison.BaseSha,
                comparison.HeadSha,
                commitPage: 1,
                commitPageSize: 20,
                includeFiles: true);
            Assert.IsNotNull(changes);
            Assert.AreEqual(comparison.BaseSha, changes.MergeBaseSha);
            Assert.HasCount(1, changes.Commits);
            Assert.AreEqual("Feature", changes.Commits[0].MessageShort);
            Assert.HasCount(1, changes.Files);
            Assert.AreEqual("README.md", changes.Files[0].Path);
            Assert.IsNotNull(changes.Files[0].Patch);

            var anchor = service.CaptureReviewAnchor(
                "reviews",
                comparison.BaseSha,
                comparison.HeadSha,
                "README.md",
                PullRequestDiffSide.New,
                startLine: 2,
                endLine: 2);
            Assert.IsNotNull(anchor);
            Assert.AreEqual(2, anchor.StartLine);
            var remappedAnchor = service.RemapReviewAnchor(
                "reviews",
                comparison.BaseSha,
                comparison.HeadSha,
                PullRequestDiffSide.New,
                anchor.Context);
            Assert.IsNotNull(remappedAnchor);
            Assert.AreEqual("README.md", remappedAnchor.Path);
            Assert.IsNull(service.CaptureReviewAnchor(
                "reviews",
                comparison.BaseSha,
                comparison.HeadSha,
                "README.md",
                PullRequestDiffSide.New,
                startLine: 200,
                endLine: 200));

            var renameComparison = service.CompareBranches("reviews", "rename", "main");
            Assert.IsNotNull(renameComparison);
            var renameChanges = service.ReadChangeSet(
                "reviews",
                renameComparison.BaseSha,
                renameComparison.HeadSha,
                commitPage: 1,
                commitPageSize: 20,
                includeFiles: true);
            Assert.IsNotNull(renameChanges);
            Assert.HasCount(1, renameChanges.Files);
            Assert.AreEqual("Renamed", renameChanges.Files[0].Status);
            Assert.AreEqual("README.md", renameChanges.Files[0].OldPath);
            Assert.AreEqual("README-renamed.md", renameChanges.Files[0].Path);

            var multiComparison = service.CompareBranches("reviews", "multi", "main");
            Assert.IsNotNull(multiComparison);
            var firstPage = service.ReadChangeSet(
                "reviews",
                multiComparison.BaseSha,
                multiComparison.HeadSha,
                commitPage: 1,
                commitPageSize: 1,
                includeFiles: false);
            var secondPage = service.ReadChangeSet(
                "reviews",
                multiComparison.BaseSha,
                multiComparison.HeadSha,
                commitPage: 2,
                commitPageSize: 1,
                includeFiles: false);
            Assert.IsNotNull(firstPage);
            Assert.IsNotNull(secondPage);
            Assert.IsTrue(firstPage.HasNextCommitPage);
            Assert.IsFalse(secondPage.HasNextCommitPage);
            Assert.HasCount(1, firstPage.Commits);
            Assert.HasCount(1, secondPage.Commits);

            var binaryComparison = service.CompareBranches("reviews", "binary", "main");
            Assert.IsNotNull(binaryComparison);
            var binaryChanges = service.ReadChangeSet(
                "reviews",
                binaryComparison.BaseSha,
                binaryComparison.HeadSha,
                commitPage: 1,
                commitPageSize: 20,
                includeFiles: true);
            Assert.IsNotNull(binaryChanges);
            Assert.HasCount(1, binaryChanges.Files);
            Assert.IsTrue(binaryChanges.Files[0].IsBinary);
            Assert.AreEqual("binary.dat", binaryChanges.Files[0].Path);

            using (var repository = new Repository(repositoryPath))
            {
                repository.Config.Add("receive.hideRefs", "refs/secret/");
            }

            service.UpdatePullRequestHead("reviews", 7, comparison.HeadSha);
            using (var repository = new Repository(repositoryPath))
            {
                Assert.AreEqual(
                    comparison.HeadSha,
                    repository.Refs["refs/pull/7/head"]?.TargetIdentifier);
                var configContents = File.ReadAllText(Path.Combine(repositoryPath, "config"));
                StringAssert.Contains(configContents, "hideRefs = refs/secret/");
                StringAssert.Contains(configContents, "hideRefs = refs/pull/");
            }

            service.DeletePullRequestHead("reviews", 7);
            using var reopened = new Repository(repositoryPath);
            Assert.IsNull(reopened.Refs["refs/pull/7/head"]);
        }
        finally
        {
            TestDirectory.Delete(rootPath);
        }
    }

    [TestMethod]
    public void ReadChangeSet_WithConfiguredDiffLimit_OmitsOversizedPatch()
    {
        var rootPath = TestDirectory.Create();
        try
        {
            CreateBareRepository(rootPath);
            var pathResolver = new PullRequestPathResolver(rootPath);
            var service = new PullRequestGitRepository(
                new GitServiceFactory(pathResolver),
                new LibGit2RepositoryService(pathResolver),
                Options.Create(new RepositoryBrowserOptions { MaxDiffCharacters = 1 }));
            var comparison = service.CompareBranches("reviews", "feature", "main");
            Assert.IsNotNull(comparison);

            var changes = service.ReadChangeSet(
                "reviews",
                comparison.BaseSha,
                comparison.HeadSha,
                commitPage: 1,
                commitPageSize: 20,
                includeFiles: true);

            Assert.IsNotNull(changes);
            Assert.IsTrue(changes.DiffTruncated);
            Assert.HasCount(1, changes.Files);
            Assert.IsNull(changes.Files[0].Patch);
        }
        finally
        {
            TestDirectory.Delete(rootPath);
        }
    }

    private static string CreateBareRepository(string rootPath)
    {
        var workPath = Path.Combine(rootPath, "work");
        Repository.Init(workPath);
        string mainSha;
        string featureSha;
        string renameSha;
        string multiSha;
        string binarySha;
        using (var repository = new Repository(workPath))
        {
            var signature = new Signature(
                "GitCandy Review",
                "review@gitcandy.local",
                new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero));
            File.WriteAllText(Path.Combine(workPath, "README.md"), "main\n", Encoding.UTF8);
            Commands.Stage(repository, "README.md");
            var initial = repository.Commit("Initial", signature, signature);
            mainSha = initial.Id.Sha;
            var main = repository.CreateBranch("main", initial);
            Commands.Checkout(repository, main);
            var feature = repository.CreateBranch("feature", initial);
            Commands.Checkout(repository, feature);
            File.AppendAllText(Path.Combine(workPath, "README.md"), "feature\n", Encoding.UTF8);
            Commands.Stage(repository, "README.md");
            featureSha = repository.Commit("Feature", signature, signature).Id.Sha;
            Commands.Checkout(repository, main);
            var rename = repository.CreateBranch("rename", initial);
            Commands.Checkout(repository, rename);
            File.Move(
                Path.Combine(workPath, "README.md"),
                Path.Combine(workPath, "README-renamed.md"));
            Commands.Stage(repository, "*");
            renameSha = repository.Commit("Rename README", signature, signature).Id.Sha;
            Commands.Checkout(repository, main);
            var multi = repository.CreateBranch("multi", initial);
            Commands.Checkout(repository, multi);
            File.AppendAllText(Path.Combine(workPath, "README.md"), "one\n", Encoding.UTF8);
            Commands.Stage(repository, "README.md");
            repository.Commit("Multi one", signature, signature);
            File.AppendAllText(Path.Combine(workPath, "README.md"), "two\n", Encoding.UTF8);
            Commands.Stage(repository, "README.md");
            multiSha = repository.Commit("Multi two", signature, signature).Id.Sha;
            Commands.Checkout(repository, main);
            var binary = repository.CreateBranch("binary", initial);
            Commands.Checkout(repository, binary);
            File.WriteAllBytes(Path.Combine(workPath, "binary.dat"), [0, 1, 2, 3]);
            Commands.Stage(repository, "binary.dat");
            binarySha = repository.Commit("Add binary", signature, signature).Id.Sha;
        }

        var barePath = Path.Combine(rootPath, "reviews.git");
        Repository.Clone(workPath, barePath, new CloneOptions { IsBare = true });
        using (var bare = new Repository(barePath))
        {
            SetReference(bare, "refs/heads/main", mainSha);
            SetReference(bare, "refs/heads/feature", featureSha);
            SetReference(bare, "refs/heads/rename", renameSha);
            SetReference(bare, "refs/heads/multi", multiSha);
            SetReference(bare, "refs/heads/binary", binarySha);
            bare.Refs.UpdateTarget(bare.Refs.Head, "refs/heads/main");
        }

        return barePath;
    }

    private static void SetReference(Repository repository, string name, string targetSha)
    {
        var reference = repository.Refs[name];
        if (reference is null)
        {
            repository.Refs.Add(name, targetSha);
        }
        else
        {
            repository.Refs.UpdateTarget(reference, targetSha);
        }
    }

    private sealed class PullRequestPathResolver(string rootPath) : IGitRepositoryPathResolver
    {
        private readonly string _rootPath = Path.GetFullPath(rootPath);

        public string RepositoryRootPath => _rootPath;

        public string ResolveRepositoryPath(string repositoryName) =>
            Path.GetFullPath(repositoryName, _rootPath);
    }
}
