using GitCandy.Data.Configuration;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class GitCandyDatabaseProviderParserTests
{
    [TestMethod]
    [DataRow("sqlite", GitCandyDatabaseProvider.Sqlite)]
    [DataRow("SQLite3", GitCandyDatabaseProvider.Sqlite)]
    [DataRow("pgsql", GitCandyDatabaseProvider.PostgreSql)]
    [DataRow("postgres", GitCandyDatabaseProvider.PostgreSql)]
    [DataRow("PostgreSQL", GitCandyDatabaseProvider.PostgreSql)]
    [DataRow("npgsql", GitCandyDatabaseProvider.PostgreSql)]
    [DataRow("SonnetDB", GitCandyDatabaseProvider.SonnetDB)]
    [DataRow("sonnet-db", GitCandyDatabaseProvider.SonnetDB)]
    public void TryParse_WithProviderAlias_ReturnsProvider(
        string value,
        GitCandyDatabaseProvider expectedProvider)
    {
        var parsed = GitCandyDatabaseProviderParser.TryParse(value, out var provider);

        Assert.IsTrue(parsed);
        Assert.AreEqual(expectedProvider, provider);
    }

    [TestMethod]
    public void TryParse_WithUnsupportedProvider_ReturnsFalse()
    {
        var parsed = GitCandyDatabaseProviderParser.TryParse("mysql", out _);

        Assert.IsFalse(parsed);
    }
}
