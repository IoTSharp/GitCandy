using Quartz;

namespace GitCandy.Schedules;

internal static class QuartzSchedulerKeys
{
    public const string JobGroup = "GitCandy.Scheduler";
    public const string TriggerGroup = "GitCandy.Scheduler.Triggers";
    public const string SchedulerJobName = "GitCandy.Scheduler.JobName";
    public const string ExecutionTimes = "GitCandy.Scheduler.ExecutionTimes";
    public const string UtcCreationUnixTimeMilliseconds = "GitCandy.Scheduler.UtcCreation";
    public const string UtcLastStartUnixTimeMilliseconds = "GitCandy.Scheduler.UtcLastStart";
    public const string UtcLastEndUnixTimeMilliseconds = "GitCandy.Scheduler.UtcLastEnd";

    public static JobKey CreateJobKey(string jobName)
    {
        return new JobKey(jobName, JobGroup);
    }

    public static TriggerKey CreateTriggerKey(string jobName)
    {
        return new TriggerKey($"{jobName}.{Guid.NewGuid():N}", TriggerGroup);
    }
}
