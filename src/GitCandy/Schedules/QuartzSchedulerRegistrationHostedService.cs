using GitCandy.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace GitCandy.Schedules;

/// <summary>
/// 启动时把 DI 中注册的 <see cref="ISchedulerJob" /> 发布到 Quartz.NET in-memory scheduler。
/// </summary>
internal sealed class QuartzSchedulerRegistrationHostedService(
    ISchedulerFactory schedulerFactory,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<GitCandyApplicationOptions> applicationOptions,
    ILogger<QuartzSchedulerRegistrationHostedService> logger) : IHostedLifecycleService
{
    private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly IOptions<GitCandyApplicationOptions> _applicationOptions = applicationOptions;
    private readonly ILogger<QuartzSchedulerRegistrationHostedService> _logger = logger;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _ = _applicationOptions.Value;

        return Task.CompletedTask;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetServices<ISchedulerJob>().ToArray();
        if (jobs.Length == 0)
        {
            _logger.LogInformation("No GitCandy scheduler jobs are registered.");
            return;
        }

        ValidateJobNames(jobs);

        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);
        foreach (var job in jobs)
        {
            var jobDetail = QuartzSchedulerJob.CreateJobDetail(job.Name);
            await scheduler.AddJob(jobDetail, replace: true, cancellationToken).ConfigureAwait(false);
            await scheduler.ScheduleJob(
                QuartzSchedulerJob.CreateInitialTrigger(job.Name),
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Registered scheduler job {JobName} ({JobType}) with Quartz.NET.",
                job.Name,
                job.JobType);
        }
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static void ValidateJobNames(ISchedulerJob[] jobs)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var job in jobs)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(job.Name);

            if (!names.Add(job.Name))
            {
                throw new InvalidOperationException($"Multiple scheduler jobs named '{job.Name}' are registered.");
            }
        }
    }
}
