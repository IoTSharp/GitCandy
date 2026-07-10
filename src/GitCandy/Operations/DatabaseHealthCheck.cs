using GitCandy.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GitCandy.Operations;

/// <summary>
/// 验证已配置数据库可建立连接。
/// </summary>
public sealed class DatabaseHealthCheck(IDbContextFactory<GitCandyDbContext> dbContextFactory)
    : IHealthCheck
{
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The database connection could not be established.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(
                "The database readiness check failed.",
                exception);
        }
    }
}
