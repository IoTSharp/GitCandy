using GitCandy.Data;
using GitCandy.Data.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GitCandy.Data.SqlServer;

/// <summary>
/// SQL Server migrations 的设计时 DbContext 工厂。
/// </summary>
public sealed class SqlServerDesignTimeGitCandyDbContextFactory : IDesignTimeDbContextFactory<GitCandyDbContext>
{
    /// <inheritdoc />
    public GitCandyDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault(static arg =>
                arg.StartsWith("Server=", StringComparison.OrdinalIgnoreCase))
            ?? "Server=(localdb)\\mssqllocaldb;Database=GitCandyDesign;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new GitCandyDatabaseOptions
        {
            Provider = GitCandyDatabaseProvider.SqlServer,
            ConnectionString = connectionString
        };
        var dbContextOptions = new DbContextOptionsBuilder<GitCandyDbContext>();

        dbContextOptions.UseSqlServer(connectionString, sqlServerOptions =>
            sqlServerOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                .MigrationsAssembly("GitCandy.Data.SqlServer"));
        dbContextOptions.ConfigureGitCandyCommonOptions(options);

        return new GitCandyDbContext(dbContextOptions.Options);
    }
}
