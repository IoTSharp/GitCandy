using GitCandy.Configuration;
using GitCandy.Ssh;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GitCandy.Operations;

/// <summary>
/// 验证启用的内置 SSH listener 已经随应用宿主启动。
/// </summary>
public sealed class SshListenerHealthCheck(
    IOptions<GitCandyApplicationOptions> options,
    ISshServerRuntime serverRuntime) : IHealthCheck
{
    private readonly IOptions<GitCandyApplicationOptions> _options = options;
    private readonly ISshServerRuntime _serverRuntime = serverRuntime;

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = !_options.Value.EnableSsh || _serverRuntime.IsRunning
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The configured SSH listener is not running.");

        return Task.FromResult(result);
    }
}
