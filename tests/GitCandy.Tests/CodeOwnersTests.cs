using GitCandy.Governance;

namespace GitCandy.Tests;

[TestClass]
public sealed class CodeOwnersTests
{
    [TestMethod]
    public void Evaluate_WithRootRecursiveAndLastMatchRules_ResolvesChangedPathOwners()
    {
        const string content = """
            * @default
            /docs/** @docs
            /docs/private/* @security @audit
            *.cs @dotnet
            """;

        var result = CodeOwnersParser.Evaluate(new CodeOwnersSnapshot(
            ".github/CODEOWNERS",
            content,
            ["README.md", "docs/guide.md", "docs/private/secret.md", "src/App.cs"]));

        Assert.IsTrue(result.IsValid);
        Assert.HasCount(4, result.Assignments);
        CollectionAssert.AreEqual(
            new[] { "@security", "@audit" },
            result.Assignments.Single(item => item.Path == "docs/private/secret.md").Owners.ToArray());
        CollectionAssert.AreEqual(
            new[] { "@dotnet" },
            result.Assignments.Single(item => item.Path == "src/App.cs").Owners.ToArray());
        CollectionAssert.AreEqual(
            new[] { "@default" },
            result.Assignments.Single(item => item.Path == "README.md").Owners.ToArray());
    }

    [TestMethod]
    public void Evaluate_WithUnsupportedNegationAndOwnerToken_ReturnsDiagnostics()
    {
        var result = CodeOwnersParser.Evaluate(new CodeOwnersSnapshot(
            "CODEOWNERS",
            "!secret/** @owner\n*.cs owner@example.com",
            ["secret/key.txt", "Program.cs"]));

        Assert.IsFalse(result.IsValid);
        Assert.HasCount(2, result.Diagnostics);
        Assert.IsTrue(result.Diagnostics.Any(item => item.Contains("unsupported pattern", StringComparison.Ordinal)));
        Assert.IsTrue(result.Diagnostics.Any(item => item.Contains("unsupported owner", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Evaluate_WithEscapedHashAndSpace_MatchesLiteralPath()
    {
        var result = CodeOwnersParser.Evaluate(new CodeOwnersSnapshot(
            "CODEOWNERS",
            "docs/with\\ space/\\#guide.md @docs",
            ["docs/with space/#guide.md"]));

        Assert.IsTrue(result.IsValid);
        Assert.HasCount(1, result.Assignments);
        Assert.AreEqual("@docs", result.Assignments[0].Owners.Single());
    }
}
