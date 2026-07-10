using System.Xml.Linq;

namespace GitCandy.Tests;

[TestClass]
public sealed class NullableConfigurationTests
{
    [TestMethod]
    public void MigrationProjects_WithNullableEnabled_DoNotDisableNullableInSource()
    {
        var repositoryRoot = FindRepositoryRoot();
        var buildProperties = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Build.props"));
        var nullableValues = buildProperties
            .Descendants()
            .Where(element => element.Name.LocalName == "Nullable")
            .Select(element => element.Value.Trim())
            .ToArray();

        CollectionAssert.Contains(nullableValues, "enable");

        var failures = new List<string>();
        foreach (var sourceRootName in new[] { "src", "tests" })
        {
            var sourceRoot = Path.Combine(repositoryRoot, sourceRootName);
            foreach (var filePath in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (IsGeneratedOutputPath(filePath))
                {
                    continue;
                }

                var lineNumber = 0;
                foreach (var line in File.ReadLines(filePath))
                {
                    lineNumber++;
                    var directive = line.TrimStart();
                    if (directive.StartsWith("#nullable", StringComparison.Ordinal)
                        && directive.Contains("disable", StringComparison.Ordinal))
                    {
                        failures.Add(
                            $"{Path.GetRelativePath(repositoryRoot, filePath)}:{lineNumber}: {directive}");
                    }
                }
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                "Migration source must keep nullable analysis enabled:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, failures));
        }
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
}
