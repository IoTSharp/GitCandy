using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace GitCandy.Schedules;

/// <summary>
/// 将迁移期的 <see cref="ISchedulerJob" /> 适配为 Quartz.NET job。
/// </summary>
[DisallowConcurrentExecution]
[PersistJobDataAfterExecution]
public sealed class QuartzSchedulerJob(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<QuartzSchedulerJob> logger) : Quartz.IJob
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly ILogger<QuartzSchedulerJob> _logger = logger;

    /// <inheritdoc />
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var dataMap = context.JobDetail.JobDataMap;
        var jobName = dataMap.GetString(QuartzSchedulerKeys.SchedulerJobName);
        if (string.IsNullOrWhiteSpace(jobName))
        {
            throw new JobExecutionException("The Quartz scheduler job is missing the GitCandy scheduler job name.");
        }

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var schedulerJob = ResolveSchedulerJob(scope.ServiceProvider, jobName);
        if (schedulerJob is null)
        {
            _logger.LogError("Scheduler job {JobName} is no longer registered in DI.", jobName);
            return;
        }

        var utcCreation = GetDateTimeOffset(
            dataMap,
            QuartzSchedulerKeys.UtcCreationUnixTimeMilliseconds) ?? DateTimeOffset.UtcNow;
        var previousUtcLastStart = GetDateTimeOffset(
            dataMap,
            QuartzSchedulerKeys.UtcLastStartUnixTimeMilliseconds);
        var previousUtcLastEnd = GetDateTimeOffset(
            dataMap,
            QuartzSchedulerKeys.UtcLastEndUnixTimeMilliseconds);
        var executionTimes = GetExecutionTimes(dataMap) + 1;
        var utcStart = DateTimeOffset.UtcNow;

        SetDateTimeOffset(dataMap, QuartzSchedulerKeys.UtcCreationUnixTimeMilliseconds, utcCreation);
        dataMap[QuartzSchedulerKeys.ExecutionTimes] = executionTimes;

        var executeContext = new SchedulerJobContext(
            executionTimes,
            utcCreation,
            previousUtcLastStart,
            previousUtcLastEnd);

        try
        {
            _logger.LogInformation("Scheduler job {JobName} executing.", schedulerJob.Name);
            await schedulerJob.ExecuteAsync(executeContext, context.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Scheduler job {JobName} executed.", schedulerJob.Name);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Scheduler job {JobName} was canceled.", schedulerJob.Name);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduler job {JobName} failed.", schedulerJob.Name);
        }

        var utcEnd = DateTimeOffset.UtcNow;
        SetDateTimeOffset(dataMap, QuartzSchedulerKeys.UtcLastStartUnixTimeMilliseconds, utcStart);
        SetDateTimeOffset(dataMap, QuartzSchedulerKeys.UtcLastEndUnixTimeMilliseconds, utcEnd);

        if (context.CancellationToken.IsCancellationRequested)
        {
            return;
        }

        var completedContext = new SchedulerJobContext(
            executionTimes,
            utcCreation,
            utcStart,
            utcEnd);
        var nextInterval = schedulerJob.GetNextInterval(completedContext);
        if (nextInterval == TimeSpan.MaxValue)
        {
            _logger.LogInformation("Scheduler job {JobName} completed without another scheduled run.", schedulerJob.Name);
            return;
        }

        await context.Scheduler.ScheduleJob(
            CreateTrigger(schedulerJob.Name, nextInterval),
            context.CancellationToken).ConfigureAwait(false);
    }

    internal static IJobDetail CreateJobDetail(string jobName)
    {
        var utcCreation = DateTimeOffset.UtcNow;

        return JobBuilder.Create<QuartzSchedulerJob>()
            .WithIdentity(QuartzSchedulerKeys.CreateJobKey(jobName))
            .UsingJobData(QuartzSchedulerKeys.SchedulerJobName, jobName)
            .UsingJobData(QuartzSchedulerKeys.ExecutionTimes, 0)
            .UsingJobData(
                QuartzSchedulerKeys.UtcCreationUnixTimeMilliseconds,
                utcCreation.ToUnixTimeMilliseconds())
            .StoreDurably()
            .Build();
    }

    internal static ITrigger CreateInitialTrigger(string jobName)
    {
        return CreateTrigger(jobName, TimeSpan.FromSeconds(1));
    }

    private static ITrigger CreateTrigger(string jobName, TimeSpan interval)
    {
        var normalizedInterval = interval < TimeSpan.Zero
            ? TimeSpan.Zero
            : interval;

        return TriggerBuilder.Create()
            .ForJob(QuartzSchedulerKeys.CreateJobKey(jobName))
            .WithIdentity(QuartzSchedulerKeys.CreateTriggerKey(jobName))
            .StartAt(DateTimeOffset.UtcNow.Add(normalizedInterval))
            .Build();
    }

    private static ISchedulerJob? ResolveSchedulerJob(IServiceProvider serviceProvider, string jobName)
    {
        var jobs = serviceProvider.GetServices<ISchedulerJob>();
        ISchedulerJob? match = null;

        foreach (var job in jobs)
        {
            if (!string.Equals(job.Name, jobName, StringComparison.Ordinal))
            {
                continue;
            }

            if (match is not null)
            {
                throw new InvalidOperationException($"Multiple scheduler jobs named '{jobName}' are registered.");
            }

            match = job;
        }

        return match;
    }

    private static int GetExecutionTimes(JobDataMap dataMap)
    {
        if (!dataMap.ContainsKey(QuartzSchedulerKeys.ExecutionTimes))
        {
            return 0;
        }

        return dataMap[QuartzSchedulerKeys.ExecutionTimes] switch
        {
            int value => value,
            long value => checked((int)value),
            string value => int.Parse(value, CultureInfo.InvariantCulture),
            IConvertible value => value.ToInt32(CultureInfo.InvariantCulture),
            _ => 0
        };
    }

    private static DateTimeOffset? GetDateTimeOffset(JobDataMap dataMap, string key)
    {
        if (!dataMap.ContainsKey(key))
        {
            return null;
        }

        return dataMap[key] switch
        {
            long value => DateTimeOffset.FromUnixTimeMilliseconds(value),
            string value => DateTimeOffset.FromUnixTimeMilliseconds(
                long.Parse(value, CultureInfo.InvariantCulture)),
            DateTimeOffset value => value.ToUniversalTime(),
            DateTime value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
            IConvertible value => DateTimeOffset.FromUnixTimeMilliseconds(
                value.ToInt64(CultureInfo.InvariantCulture)),
            _ => null
        };
    }

    private static void SetDateTimeOffset(JobDataMap dataMap, string key, DateTimeOffset value)
    {
        dataMap[key] = value.ToUnixTimeMilliseconds();
    }
}
