using System.Text;
using GitCandy.Git;
using GitCandy.PullRequests;
using LibGit2Sharp;

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
                new LibGit2RepositoryService(pathResolver));

            var branches = service.GetBranches("reviews");
            Assert.IsTrue(branches.Any(item => item.Name == "main"));
            Assert.IsTrue(branches.Any(item => item.Name == "feature"));
            var comparison = service.CompareBranches("reviews", "feature", "main");
            Assert.IsNotNull(comparison);
            Assert.AreEqual(1, comparison.AheadBy);

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

    private static string CreateBareRepository(string rootPath)
    {
        var workPath = Path.Combine(rootPath, "work");
        Repository.Init(workPath);
        string mainSha;
        string featureSha;
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
        }

        var barePath = Path.Combine(rootPath, "reviews.git");
        Repository.Clone(workPath, barePath, new CloneOptions { IsBare = true });
        using (var bare = new Repository(barePath))
        {
            SetReference(bare, "refs/heads/main", mainSha);
            SetReference(bare, "refs/heads/feature", featureSha);
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
