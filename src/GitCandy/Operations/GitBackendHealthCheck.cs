using System.Diagnostics;
using GitCandy.Git;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GitCandy.Operations;

/// <summary>
/// 验证 Git 官方 helper 可以启动并正常返回版本信息。
/// </summary>
public sealed class GitBackendHealthCheck(IGitExecutableResolver executableResolver) : IHealthCheck
{
    private readonly IGitExecutableResolver _executableResolver = executableResolver;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process();
        var started = false;
        try
        {
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _executableResolver.Resolve(),
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            process.StartInfo.ArgumentList.Add("--version");

            started = process.Start();
            if (!started)
            {
                return HealthCheckResult.Unhealthy("The Git backend process did not start.");
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(
                standardOutput,
                standardError,
                process.WaitForExitAsync(cancellationToken));

            return process.ExitCode == 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The Git backend process returned an error.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (started && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception)
            {
                // Preserve the readiness timeout as the actionable failure.
            }

            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or GitTransportException)
        {
            return HealthCheckResult.Unhealthy(
                "The Git backend readiness check failed.",
                exception);
        }
    }
}
