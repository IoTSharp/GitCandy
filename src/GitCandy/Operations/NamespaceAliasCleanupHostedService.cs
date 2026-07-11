using GitCandy.Application;

namespace GitCandy.Operations;

/// <summary>周期性、幂等释放已超过配置保留期的名称 alias。</summary>
internal sealed class NamespaceAliasCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<NamespaceAliasCleanupHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<NamespaceAliasCleanupHostedService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanupAsync(stoppingToken);
        using var timer = new PeriodicTimer(CleanupInterval, _timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanupAsync(stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<INameManagementService>();
            var released = await service.ReleaseExpiredAliasesAsync(
                _timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
            if (released > 0)
            {
                _logger.LogInformation("Released {AliasCount} expired repository address aliases.", released);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Expired repository address aliases could not be released.");
        }
    }
}
