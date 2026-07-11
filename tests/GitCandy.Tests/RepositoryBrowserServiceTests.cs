using System.IO.Compression;
using System.Text;
using GitCandy.Configuration;
using GitCandy.Git;
using LibGit2Sharp;
using Microsoft.Extensions.Options;

namespace GitCandy.Tests;

[TestClass]
public sealed class RepositoryBrowserServiceTests
{
    [TestMethod]
    public async Task RepositoryBrowser_WithHistoryAndSpecialEntries_ReturnsBoundedViewsAndArchive()
    {
        var rootPath = TestDirectory.Create();
        try
        {
            var repositoryPath = CreateRepository(rootPath);
            var service = CreateService(rootPath, maxBlobBytes: 128);
            var context = new GitRepositoryContext("browser", repositoryPath);

            var tree = service.ReadTree(context, "HEAD", null);
            Assert.IsNotNull(tree);
            Assert.IsTrue(tree.Entries.Any(entry =>
                entry.Name == "target-link" && entry.Kind == RepositoryTreeEntryKind.Symlink));
            Assert.IsTrue(tree.Entries.Any(entry =>
                entry.Name == "vendor" && entry.Kind == RepositoryTreeEntryKind.Tree));
            var vendor = service.ReadTree(context, "HEAD", "vendor");
            Assert.IsNotNull(vendor);
            Assert.AreEqual(RepositoryTreeEntryKind.Submodule, vendor.Entries.Single().Kind);

            var blob = service.ReadBlob(context, "HEAD", "README.md");
            Assert.IsNotNull(blob);
            Assert.IsFalse(blob.IsBinary);
            Assert.IsFalse(blob.HasUnknownEncoding);
            StringAssert.Contains(blob.Text, "second line");

            var binary = service.ReadBlob(context, "HEAD", "binary.dat");
            Assert.IsNotNull(binary);
            Assert.IsTrue(binary.IsBinary);
            var unknown = service.ReadBlob(context, "HEAD", "unknown.txt");
            Assert.IsNotNull(unknown);
            Assert.IsTrue(unknown.HasUnknownEncoding);
            var large = service.ReadBlob(context, "HEAD", "large.txt");
            Assert.IsNotNull(large);
            Assert.IsTrue(large.IsTooLarge);
            Assert.IsNull(large.Text);

            await using var raw = new MemoryStream();
            var rawResult = await service.CopyBlobAsync(context, "HEAD", "README.md", raw);
            Assert.IsNotNull(rawResult);
            Assert.AreEqual(rawResult.Size, raw.Length);

            var commits = service.ReadCommits(context, "HEAD", page: 1, pageSize: 20);
            Assert.IsNotNull(commits);
            Assert.IsGreaterThanOrEqualTo(2, commits.Commits.Count);
            var commit = service.ReadCommit(context, commits.Commits[0].Id);
            Assert.IsNotNull(commit);
            Assert.IsTrue(commit.Files.Any(file => file.Path is "target-link" or "vendor/demo"));
            var blame = service.ReadBlame(context, "HEAD", "README.md");
            Assert.IsNotNull(blame);
            Assert.IsGreaterThanOrEqualTo(2, blame.Hunks.Sum(hunk => hunk.Lines.Count));
            var compare = service.Compare(context, commits.Commits[^1].Id, commits.Commits[0].Id);
            Assert.IsNotNull(compare);
            Assert.IsGreaterThanOrEqualTo(1, compare.AheadBy);

            await using var archiveStream = new MemoryStream();
            var archiveRevision = await service.WriteArchiveAsync(
                context,
                commits.Commits[0].Id,
                archiveStream);
            Assert.IsNotNull(archiveRevision);
            archiveStream.Position = 0;
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            Assert.IsNotNull(archive.GetEntry("README.md"));
            Assert.IsNull(archive.GetEntry("vendor/demo"));
        }
        finally
        {
            TestDirectory.Delete(rootPath);
        }
    }

    [TestMethod]
    public void RepositoryBrowser_WithInvalidPathOrLimits_RejectsUnsafeAndOversizedRequests()
    {
        var rootPath = TestDirectory.Create();
        try
        {
            var repositoryPath = CreateRepository(rootPath);
            var context = new GitRepositoryContext("browser", repositoryPath);
            var service = CreateService(rootPath, maxBlobBytes: 128, maxArchiveBytes: 64);

            Assert.ThrowsExactly<ArgumentException>(() =>
                service.ReadBlob(context, "HEAD", "../README.md"));
            Assert.ThrowsExactly<ArgumentException>(() =>
                service.ReadTree(context, "HEAD", "folder\\child"));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                service.WriteArchiveAsync(context, "HEAD", new MemoryStream()).GetAwaiter().GetResult());
        }
        finally
        {
            TestDirectory.Delete(rootPath);
        }
    }

    private static string CreateRepository(string rootPath)
    {
        var repositoryPath = Path.Combine(rootPath, "browser");
        Repository.Init(repositoryPath);
        using var repository = new Repository(repositoryPath);
        var signature = new Signature(
            "GitCandy Browser",
            "browser@gitcandy.local",
            new DateTimeOffset(2026, 7, 11, 8, 0, 0, TimeSpan.Zero));

        File.WriteAllText(Path.Combine(repositoryPath, "README.md"), "first line\n", Encoding.UTF8);
        File.WriteAllBytes(Path.Combine(repositoryPath, "binary.dat"), [1, 0, 2, 3]);
        File.WriteAllBytes(Path.Combine(repositoryPath, "unknown.txt"), [0x80, 0x81, 0x82]);
        File.WriteAllText(Path.Combine(repositoryPath, "large.txt"), new string('x', 512), Encoding.UTF8);
        Commands.Stage(repository, ["README.md", "binary.dat", "unknown.txt", "large.txt"]);
        var firstCommit = repository.Commit("Initial browser content", signature, signature);

        File.AppendAllText(Path.Combine(repositoryPath, "README.md"), "second line\n", Encoding.UTF8);
        Commands.Stage(repository, "README.md");
        var secondCommit = repository.Commit("Update README", signature, signature);

        using var linkContent = new MemoryStream(Encoding.UTF8.GetBytes("README.md"));
        var linkBlob = repository.ObjectDatabase.CreateBlob(linkContent);
        var definition = TreeDefinition.From(secondCommit.Tree);
        definition.Add("target-link", linkBlob, Mode.SymbolicLink);
        definition.AddGitLink("vendor/demo", firstCommit.Id);
        var tree = repository.ObjectDatabase.CreateTree(definition);
        var specialCommit = repository.ObjectDatabase.CreateCommit(
            signature,
            signature,
            "Add symlink and submodule",
            tree,
            [secondCommit],
            prettifyMessage: true);
        repository.Refs.UpdateTarget(repository.Head.CanonicalName, specialCommit.Id.Sha);
        repository.ApplyTag("v1", firstCommit.Sha);
        repository.CreateBranch("feature", secondCommit);
        return repositoryPath;
    }

    private static RepositoryBrowserService CreateService(
        string rootPath,
        long maxBlobBytes,
        long maxArchiveBytes = 1024 * 1024)
    {
        var pathResolver = new BrowserPathResolver(rootPath);
        return new RepositoryBrowserService(
            new LibGit2RepositoryService(pathResolver),
            Options.Create(new RepositoryBrowserOptions
            {
                MaxDisplayedBlobBytes = maxBlobBytes,
                MaxDiffCharacters = 1024 * 1024,
                MaxDiffFiles = 100,
                MaxArchiveBytes = maxArchiveBytes,
                MaxArchiveEntries = 100
            }));
    }

    private sealed class BrowserPathResolver(string rootPath) : IGitRepositoryPathResolver
    {
        private readonly string _rootPath = Path.GetFullPath(rootPath);

        public string RepositoryRootPath => _rootPath;

        public string ResolveRepositoryPath(string repositoryName)
        {
            return Path.GetFullPath(repositoryName, _rootPath);
        }
    }
}
