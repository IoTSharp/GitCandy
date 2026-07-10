using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace GitCandy.Schedules;

internal static class SchedulerTelemetry
{
    public const string ActivitySourceName = "GitCandy.Scheduler";
    public const string MeterName = "GitCandy.Scheduler";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Executions = Meter.CreateCounter<long>(
        "gitcandy.scheduler.executions",
        unit: "{execution}",
        description: "Scheduler job executions grouped by result.");
    public static readonly UpDownCounter<long> ActiveExecutions = Meter.CreateUpDownCounter<long>(
        "gitcandy.scheduler.active_executions",
        unit: "{execution}",
        description: "Scheduler jobs currently executing.");
    public static readonly Histogram<double> ExecutionDuration = Meter.CreateHistogram<double>(
        "gitcandy.scheduler.duration",
        unit: "s",
        description: "Scheduler job execution duration.");

    public static Activity? StartExecution()
    {
        return ActivitySource.StartActivity("scheduler.execute", ActivityKind.Internal);
    }

    public static TagList CreateTags(string result)
    {
        return new TagList
        {
            { "gitcandy.result", result }
        };
    }
}
