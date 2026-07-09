using System.Xml.Linq;

namespace GitCandy.Tests;

[TestClass]
public sealed class SystemWebEntryCheckTests
{
    private static readonly string[] ForbiddenReferenceNames =
    [
        "System.Web",
        "System.Web.Mvc",
        "System.Web.Optimization",
        "System.Data.Entity"
    ];

    private static readonly string[] ForbiddenMigrationSourceTokens =
    [
        "HttpRuntime.Cache",
        "System.Web.Caching"
    ];

    private static readonly string[] SourceFileExtensions =
    [
        ".cs",
        ".cshtml",
        ".csproj",
        ".props",
        ".razor",
        ".targets"
    ];

    [TestMethod]
    public void MigrationSolutionProjects_WithReferences_DoNotReferenceLegacyAspNetOrEf6()
    {
        var repositoryRoot = FindRepositoryRoot();
        var failures = new List<string>();

        foreach (var projectPath in GetMigrationSourceProjectPaths(repositoryRoot))
        {
            var projectDocument = XDocument.Load(projectPath);
            foreach (var reference in GetReferenceEntries(projectDocument))
            {
                foreach (var forbiddenName in ForbiddenReferenceNames)
                {
                    if (MatchesForbiddenReference(reference.Include, forbiddenName))
                    {
                        failures.Add(
                            $"{ToRepositoryRelativePath(repositoryRoot, projectPath)}: {reference.ItemName} includes {reference.Include}");
                    }
                }
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                "Migration projects must not reference legacy ASP.NET MVC5 or EF6 entry points:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, failures));
        }
    }

    [TestMethod]
    public void MigrationSourceFiles_WithLegacyNamespaceTokens_DoNotUseLegacyAspNetOrEf6()
    {
        var repositoryRoot = FindRepositoryRoot();
        var failures = new List<string>();

        foreach (var filePath in GetMigrationSourceFiles(repositoryRoot))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(filePath))
            {
                lineNumber++;
                foreach (var forbiddenName in ForbiddenReferenceNames)
                {
                    if (line.Contains(forbiddenName, StringComparison.Ordinal))
                    {
                        failures.Add(
                            $"{ToRepositoryRelativePath(repositoryRoot, filePath)}:{lineNumber}: contains {forbiddenName}");
                    }
                }

                foreach (var forbiddenToken in ForbiddenMigrationSourceTokens)
                {
                    if (line.Contains(forbiddenToken, StringComparison.Ordinal))
                    {
                        failures.Add(
                            $"{ToRepositoryRelativePath(repositoryRoot, filePath)}:{lineNumber}: contains {forbiddenToken}");
                    }
                }
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                "Migration source files must not use legacy ASP.NET MVC5 or EF6 namespaces:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, failures));
        }
    }

    private static IEnumerable<(string ItemName, string Include)> GetReferenceEntries(XContainer projectDocument)
    {
        var referenceItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FrameworkReference",
            "PackageReference",
            "Reference"
        };

        return projectDocument
            .Descendants()
            .Where(element => referenceItemNames.Contains(element.Name.LocalName))
            .Select(element => (
                element.Name.LocalName,
                ((string?)element.Attribute("Include") ?? (string?)element.Attribute("Update") ?? string.Empty).Trim()))
            .Where(reference => reference.Item2.Length > 0);
    }

    private static IEnumerable<string> GetMigrationSourceProjectPaths(string repositoryRoot)
    {
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var solutionDocument = XDocument.Load(Path.Combine(repositoryRoot, "GitCandy.slnx"));

        foreach (var projectElement in solutionDocument.Descendants().Where(element => element.Name.LocalName == "Project"))
        {
            var projectPath = (string?)projectElement.Attribute("Path");
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, projectPath.Replace('/', Path.DirectorySeparatorChar)));
            if (IsUnderDirectory(fullPath, sourceRoot))
            {
                yield return fullPath;
            }
        }
    }

    private static IEnumerable<string> GetMigrationSourceFiles(string repositoryRoot)
    {
        var rootEntryFiles = new[]
        {
            "Directory.Build.props",
            "Directory.Packages.props",
            "GitCandy.slnx"
        };

        foreach (var relativePath in rootEntryFiles)
        {
            var fullPath = Path.Combine(repositoryRoot, relativePath);
            if (File.Exists(fullPath))
            {
                yield return fullPath;
            }
        }

        var sourceRoot = Path.Combine(repositoryRoot, "src");
        foreach (var filePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (IsGeneratedOutputPath(filePath))
            {
                continue;
            }

            if (SourceFileExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
            {
                yield return filePath;
            }
        }
    }

    private static bool MatchesForbiddenReference(string referenceName, string forbiddenName)
    {
        return referenceName.Equals(forbiddenName, StringComparison.OrdinalIgnoreCase)
            || referenceName.StartsWith(forbiddenName + ".", StringComparison.OrdinalIgnoreCase)
            || referenceName.StartsWith(forbiddenName + ",", StringComparison.OrdinalIgnoreCase);
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

    private static bool IsGeneratedOutputPath(string filePath)
    {
        return filePath
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
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

    private static string ToRepositoryRelativePath(string repositoryRoot, string path)
    {
        return Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');
    }
}
