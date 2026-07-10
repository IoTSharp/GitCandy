using GitCandy.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GitCandy.Operations;

/// <summary>
/// 验证 repository 根目录存在且可写。
/// </summary>
public sealed class RepositoryPathHealthCheck(IGitCandyApplicationPaths paths) : IHealthCheck
{
    private readonly IGitCandyApplicationPaths _paths = paths;

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return WritableDirectoryHealthCheck.CheckAsync(
            _paths.RepositoryPath,
            "repository",
            cancellationToken);
    }
}

/// <summary>
/// 验证 cache 根目录存在且可写。
/// </summary>
public sealed class CachePathHealthCheck(IGitCandyApplicationPaths paths) : IHealthCheck
{
    private readonly IGitCandyApplicationPaths _paths = paths;

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return WritableDirectoryHealthCheck.CheckAsync(
            _paths.CachePath,
            "cache",
            cancellationToken);
    }
}

internal static class WritableDirectoryHealthCheck
{
    public static async Task<HealthCheckResult> CheckAsync(
        string path,
        string pathName,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path))
        {
            return HealthCheckResult.Unhealthy($"The {pathName} directory does not exist.");
        }

        var probePath = Path.Combine(path, $".gitcandy-health-{Guid.NewGuid():N}.tmp");
        try
        {
            await using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.Asynchronous);
            await stream.WriteAsync(new byte[] { 0 }, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return HealthCheckResult.Unhealthy(
                $"The {pathName} directory is not writable.",
                exception);
        }
        finally
        {
            try
            {
                File.Delete(probePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A failed cleanup must not replace the actionable readiness result.
            }
        }
    }
}
