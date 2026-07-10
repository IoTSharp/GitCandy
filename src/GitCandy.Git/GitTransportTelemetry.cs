using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace GitCandy.Git;

/// <summary>
/// 定义 Git transport 的低基数 tracing 和 metrics 信号。
/// </summary>
public static class GitTransportTelemetry
{
    /// <summary>
    /// Git transport activity source 名称。
    /// </summary>
    public const string ActivitySourceName = "GitCandy.Git.Transport";

    /// <summary>
    /// Git transport meter 名称。
    /// </summary>
    public const string MeterName = "GitCandy.Git.Transport";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);

    internal static readonly Counter<long> Operations = Meter.CreateCounter<long>(
        "gitcandy.git.transport.operations",
        unit: "{operation}",
        description: "Git transport operations grouped by service and result.");
    internal static readonly UpDownCounter<long> ActiveOperations = Meter.CreateUpDownCounter<long>(
        "gitcandy.git.transport.active_operations",
        unit: "{operation}",
        description: "Git helper operations currently streaming data.");
    internal static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "gitcandy.git.transport.duration",
        unit: "s",
        description: "Git transport operation duration, including concurrency-slot wait time.");

    internal static Activity? StartOperation(GitTransportRequest request)
    {
        var activity = ActivitySource.StartActivity("git.transport", ActivityKind.Internal);
        activity?.SetTag("git.service.name", GetServiceName(request.Service));
        activity?.SetTag("git.advertise_refs", request.AdvertiseRefs);
        return activity;
    }

    internal static TagList CreateTags(GitTransportService service, string? result = null)
    {
        var tags = new TagList
        {
            { "git.service.name", GetServiceName(service) }
        };
        if (result is not null)
        {
            tags.Add("gitcandy.result", result);
        }

        return tags;
    }

    private static string GetServiceName(GitTransportService service)
    {
        return service switch
        {
            GitTransportService.UploadPack => "upload-pack",
            GitTransportService.ReceivePack => "receive-pack",
            GitTransportService.UploadArchive => "upload-archive",
            _ => "unknown"
        };
    }
}
