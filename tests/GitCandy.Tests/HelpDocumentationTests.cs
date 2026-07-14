using System.Text.Json;
using System.Text.RegularExpressions;

namespace GitCandy.Tests;

[TestClass]
public sealed class HelpDocumentationTests
{
    [TestMethod]
    public void DocumentInventory_WithRepositoryMarkdown_CoversEverySourceAndSeparatesArchives()
    {
        var repositoryRoot = GetRepositoryRoot();
        var inventory = LoadInventory(repositoryRoot);
        var expectedPaths = Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "docs"),
                "*.md",
                SearchOption.AllDirectories)
            .Select(path => NormalizePath(Path.GetRelativePath(repositoryRoot, path)))
            .Concat(["README.md", "README.zh-cn.md", "ROADMAP.md", "CHANGES.md", "LICENSE.md"])
            .Order(StringComparer.Ordinal)
            .ToArray();
        var inventoryPaths = inventory.Documents
            .Select(document => document.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(expectedPaths, inventoryPaths);
        Assert.AreEqual(inventory.Documents.Count, inventory.Documents.Select(document => document.Path).Distinct(StringComparer.Ordinal).Count());
        StringAssert.Contains(inventory.VersionStrategy, "/help/current");
        StringAssert.Contains(inventory.VersionStrategy, "archived", StringComparison.OrdinalIgnoreCase);

        foreach (var document in inventory.Documents)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(document.Owner), document.Path);
            Assert.IsFalse(string.IsNullOrWhiteSpace(document.Audience), document.Path);
            Assert.IsFalse(string.IsNullOrWhiteSpace(document.Version), document.Path);
            Assert.IsFalse(document.Path.Contains('\\'), document.Path);
            Assert.IsTrue(File.Exists(Path.Combine(repositoryRoot, document.Path)), document.Path);
            Assert.IsTrue(File.Exists(Path.Combine(repositoryRoot, document.Canonical)), document.Canonical);
            if (document.Public)
            {
                Assert.IsFalse(document.Archived, document.Path);
                Assert.AreEqual(document.Path, document.Canonical, document.Path);
                Assert.IsTrue(document.Path.StartsWith("docs/help/", StringComparison.Ordinal), document.Path);
                Assert.IsTrue(document.Permalink?.StartsWith("/help/", StringComparison.Ordinal) == true, document.Path);
            }
        }

        AssertArchived(inventory, "docs/roadmap/completed-milestones.md");
        AssertArchived(inventory, "docs/roadmap/roadmap-archive-2026-07-13.md");
        Assert.IsTrue(inventory.Documents.Where(document => document.Path.StartsWith("docs/migration/", StringComparison.Ordinal)).All(document => document.Archived));
    }

    [TestMethod]
    public void PublicHelpSources_WithManifestAndSearchIndex_HaveStableMetadataAndOfflineAssets()
    {
        var repositoryRoot = GetRepositoryRoot();
        var helpRoot = Path.Combine(repositoryRoot, "docs", "help");
        var inventory = LoadInventory(repositoryRoot);
        var publicDocuments = inventory.Documents.Where(document => document.Public).ToArray();
        Assert.AreEqual(13, publicDocuments.Length);

        foreach (var document in publicDocuments)
        {
            var sourcePath = Path.Combine(repositoryRoot, document.Path);
            var source = File.ReadAllText(sourcePath);
            var frontMatter = ReadFrontMatter(sourcePath);
            Assert.AreEqual("default", frontMatter["layout"], document.Path);
            Assert.AreEqual("current", frontMatter["version"], document.Path);
            Assert.AreEqual(document.Canonical, frontMatter["canonical"], document.Path);
            Assert.AreEqual(ToJekyllPermalink(document.Permalink!), frontMatter["permalink"], document.Path);
            Assert.IsTrue(frontMatter.ContainsKey("title"), document.Path);
            Assert.IsTrue(frontMatter.ContainsKey("description"), document.Path);
            Assert.IsTrue(frontMatter.ContainsKey("updated"), document.Path);
            Assert.IsTrue(frontMatter.ContainsKey("help_root"), document.Path);
            Assert.AreEqual(document.Owner, frontMatter["owner"], document.Path);
            Assert.AreEqual(document.Audience, frontMatter["audience"], document.Path);
            Assert.AreEqual("true", frontMatter["public"], document.Path);
            Assert.AreEqual("false", frontMatter["archived"], document.Path);
            Assert.IsFalse(source.Contains("BEGIN PRIVATE KEY", StringComparison.OrdinalIgnoreCase), document.Path);
            Assert.IsFalse(source.Contains("BEGIN OPENSSH PRIVATE KEY", StringComparison.OrdinalIgnoreCase), document.Path);
        }

        var publicPermalinks = publicDocuments.Select(document => document.Permalink!).ToHashSet(StringComparer.Ordinal);
        foreach (var document in publicDocuments)
        {
            var source = File.ReadAllText(Path.Combine(repositoryRoot, document.Path));
            foreach (Match match in Regex.Matches(source, @"\[[^\]]+\]\((?<url>[^)\s]+)\)"))
            {
                var url = match.Groups["url"].Value;
                if (url.StartsWith('#') || Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    continue;
                }

                var target = new Uri(new Uri($"https://help.invalid{document.Permalink}"), url).AbsolutePath;
                Assert.IsTrue(
                    publicPermalinks.Contains(target) || target == "/help/help-manifest.json",
                    $"{document.Path} links to unpublished help target '{url}'.");
            }
        }

        var manifest = JsonSerializer.Deserialize<HelpManifest>(
            File.ReadAllText(Path.Combine(helpRoot, "help-manifest.json")),
            JsonOptions);
        Assert.IsNotNull(manifest);
        Assert.AreEqual(1, manifest.SchemaVersion);
        Assert.AreEqual("JekyllNet 0.2.5", manifest.Generator);
        Assert.AreEqual("current", manifest.DocumentationVersion);
        Assert.AreEqual("20260714.1", manifest.AssetVersion);
        Assert.AreEqual(12, manifest.Documents.Count);

        var searchDocuments = JsonSerializer.Deserialize<List<SearchDocument>>(
            File.ReadAllText(Path.Combine(helpRoot, "search-index.json")),
            JsonOptions);
        Assert.IsNotNull(searchDocuments);
        CollectionAssert.AreEquivalent(manifest.Documents.ToArray(), searchDocuments.Select(document => document.Url).ToArray());
        Assert.AreEqual(searchDocuments.Count, searchDocuments.Select(document => document.Url).Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(searchDocuments.All(document => !string.IsNullOrWhiteSpace(document.Title)
            && !string.IsNullOrWhiteSpace(document.Summary)
            && !string.IsNullOrWhiteSpace(document.Keywords)));

        var themeSources = new[]
        {
            Path.Combine(helpRoot, "_layouts", "default.html"),
            Path.Combine(helpRoot, "assets", "help.css"),
            Path.Combine(helpRoot, "assets", "help.js")
        };
        foreach (var themeSource in themeSources)
        {
            var content = File.ReadAllText(themeSource);
            Assert.IsFalse(content.Contains("https://", StringComparison.OrdinalIgnoreCase), themeSource);
            Assert.IsFalse(content.Contains("http://", StringComparison.OrdinalIgnoreCase), themeSource);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static DocumentInventory LoadInventory(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "docs", "help", "_data", "document-inventory.json");
        var inventory = JsonSerializer.Deserialize<DocumentInventory>(File.ReadAllText(path), JsonOptions);
        Assert.IsNotNull(inventory);
        Assert.AreEqual(1, inventory.SchemaVersion);
        return inventory;
    }

    private static Dictionary<string, string> ReadFrontMatter(string path)
    {
        var lines = File.ReadAllLines(path);
        Assert.IsTrue(lines.Length > 2 && lines[0] == "---", path);
        var closingLine = Array.IndexOf(lines, "---", 1);
        Assert.IsTrue(closingLine > 1, path);
        return lines[1..closingLine]
            .Select(line => line.Split(':', 2, StringSplitOptions.TrimEntries))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
    }

    private static string ToJekyllPermalink(string publicPermalink) =>
        publicPermalink == "/help/"
            ? "/index.html"
            : $"/{publicPermalink[6..]}index.html";

    private static void AssertArchived(DocumentInventory inventory, string path)
    {
        var document = inventory.Documents.Single(item => item.Path == path);
        Assert.IsTrue(document.Archived, path);
        Assert.IsFalse(document.Public, path);
        Assert.IsNull(document.Permalink, path);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GitCandy.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory);
        return directory.FullName;
    }

    private sealed record DocumentInventory(int SchemaVersion, string VersionStrategy, IReadOnlyList<InventoryDocument> Documents);
    private sealed record InventoryDocument(string Path, string Owner, string Audience, bool Public, string Canonical, bool Archived, string Version, string? Permalink);
    private sealed record HelpManifest(int SchemaVersion, string Generator, string DocumentationVersion, string AssetVersion, IReadOnlyList<string> Documents);
    private sealed record SearchDocument(string Title, string Url, string Summary, string Keywords);
}
