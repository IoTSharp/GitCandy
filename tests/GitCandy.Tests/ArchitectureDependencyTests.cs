using System.Xml.Linq;

namespace GitCandy.Tests;

[TestClass]
public sealed class ArchitectureDependencyTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedProjectReferences =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["GitCandy.Core"] = [],
            ["GitCandy.Data"] = ["GitCandy.Core"],
            ["GitCandy.Data.PostgreSql"] = ["GitCandy.Data"],
            ["GitCandy.Data.SonnetDB"] = ["GitCandy.Data", "SonnetDB.EntityFrameworkCore"],
            ["GitCandy.Data.Sqlite"] = ["GitCandy.Data"],
            ["GitCandy.Data.SqlServer"] = ["GitCandy.Data"],
            ["GitCandy.Git"] = ["GitCandy.Core"],
            ["GitCandy.Ssh"] = ["GitCandy.Core", "GitCandy.Git"],
            ["GitCandy"] =
            [
                "GitCandy.Core",
                "GitCandy.Data",
                "GitCandy.Data.SonnetDB",
                "GitCandy.Data.Sqlite",
                "GitCandy.Git",
                "GitCandy.Ssh"
            ]
        };

    [TestMethod]
    public void MigrationProjects_WithProjectReferences_FollowAllowedDependencyDirection()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPaths = GetSourceProjectPaths(repositoryRoot).ToArray();
        var failures = new List<string>();

        foreach (var projectPath in projectPaths)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            if (!AllowedProjectReferences.TryGetValue(projectName, out var allowedReferences))
            {
                failures.Add($"No architecture rule is defined for src project '{projectName}'.");
                continue;
            }

            var actualReferences = XDocument.Load(projectPath)
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => (string?)element.Attribute("Include"))
                .Where(static include => !string.IsNullOrWhiteSpace(include))
                .Select(static include => Path.GetFileNameWithoutExtension(include))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var expectedReferences = allowedReferences
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (!actualReferences.SequenceEqual(expectedReferences, StringComparer.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"{projectName}: expected [{string.Join(", ", expectedReferences)}], "
                    + $"actual [{string.Join(", ", actualReferences)}].");
            }
        }

        CollectionAssert.AreEquivalent(
            AllowedProjectReferences.Keys.ToArray(),
            projectPaths.Select(Path.GetFileNameWithoutExtension).ToArray(),
            "GitCandy.slnx source projects and architecture rules must stay in sync.");

        if (failures.Count > 0)
        {
            Assert.Fail(
                "Source project references violate the GitCandy module dependency direction:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, failures));
        }
    }

    [TestMethod]
    public void CoreProject_WithReferences_RemainsFrameworkAndInfrastructureIndependent()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "src", "GitCandy.Core", "GitCandy.Core.csproj");
        var projectDocument = XDocument.Load(projectPath);
        var externalReferences = projectDocument
            .Descendants()
            .Where(element => element.Name.LocalName is "FrameworkReference" or "PackageReference" or "ProjectReference")
            .Select(element => $"{element.Name.LocalName}: {(string?)element.Attribute("Include")}")
            .ToArray();

        Assert.HasCount(0, externalReferences,
            $"GitCandy.Core must not reference frameworks, packages, or infrastructure projects: {string.Join(", ", externalReferences)}");
    }

    [TestMethod]
    public void WebProject_WithSourceFiles_DoesNotOwnApplicationGitOrSshImplementations()
    {
        var repositoryRoot = FindRepositoryRoot();
        var webRoot = Path.Combine(repositoryRoot, "src", "GitCandy");
        var forbiddenDirectories = new[] { "Application", "Git", "Ssh" };
        var failures = forbiddenDirectories
            .Select(directory => Path.Combine(webRoot, directory))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToArray();

        Assert.HasCount(0, failures,
            $"The Web host must remain a presentation/composition module: {string.Join(", ", failures)}");
    }

    [TestMethod]
    public void MigrationProjects_WithProcessLaunches_KeepThemInsideTransportAndReadinessBoundaries()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(sourceRoot, "GitCandy.Git", "GitProcessTransportBackend.cs"),
            Path.Combine(sourceRoot, "GitCandy.Git", "GitProcessRemoteRepositorySyncBackend.cs"),
            Path.Combine(sourceRoot, "GitCandy.Git", "GitReceiveHook.cs"),
            Path.Combine(sourceRoot, "GitCandy", "Operations", "GitBackendHealthCheck.cs")
        };
        var processMarkers = new[] { "new Process", "Process.Start", "new ProcessStartInfo" };
        var failures = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}Protocol{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !allowedFiles.Contains(path))
            .Where(path => processMarkers.Any(marker =>
                File.ReadAllText(path).Contains(marker, StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToArray();

        Assert.HasCount(0, failures,
            "External processes are limited to controlled Git transport, remote sync, receive-hook, and readiness boundaries: "
            + string.Join(", ", failures));

        var transportSource = File.ReadAllText(
            Path.Combine(sourceRoot, "GitCandy.Git", "GitProcessTransportBackend.cs"));
        StringAssert.Contains(transportSource, "GitTransportService.UploadPack => \"upload-pack\"");
        StringAssert.Contains(transportSource, "GitTransportService.ReceivePack => \"receive-pack\"");
        StringAssert.Contains(transportSource, "GitTransportService.UploadArchive => \"upload-archive\"");
        var remoteSyncSource = File.ReadAllText(
            Path.Combine(sourceRoot, "GitCandy.Git", "GitProcessRemoteRepositorySyncBackend.cs"));
        StringAssert.Contains(remoteSyncSource, "startInfo.ArgumentList.Add");
        StringAssert.Contains(remoteSyncSource, "RemoteCredentialPipeServer");
    }

    private static IEnumerable<string> GetSourceProjectPaths(string repositoryRoot)
    {
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var solutionDocument = XDocument.Load(Path.Combine(repositoryRoot, "GitCandy.slnx"));

        return solutionDocument
            .Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => (string?)element.Attribute("Path"))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Path.Combine(
                repositoryRoot,
                path!.Replace('/', Path.DirectorySeparatorChar))))
            .Where(path => IsUnderDirectory(path, sourceRoot));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GitCandy.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing GitCandy.slnx.");
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory);
        if (!fullDirectory.EndsWith(Path.DirectorySeparatorChar))
        {
            fullDirectory += Path.DirectorySeparatorChar;
        }

        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }
}
