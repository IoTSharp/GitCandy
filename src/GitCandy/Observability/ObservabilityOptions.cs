namespace GitCandy.Observability;

/// <summary>
/// 配置 GitCandy OpenTelemetry 信号和导出器。
/// </summary>
public sealed class ObservabilityOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "GitCandy:Observability";

    /// <summary>
    /// 获取或设置是否创建 OpenTelemetry tracing、metrics 和 logging provider。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 获取或设置写入 telemetry resource 的服务名称。
    /// </summary>
    public string ServiceName { get; set; } = "GitCandy";

    /// <summary>
    /// 获取或设置 trace id 比例采样率，取值范围为 0 到 1。
    /// </summary>
    public double TraceSamplingRatio { get; set; } = 1;

    /// <summary>
    /// 获取或设置是否启用仅供本地诊断的 Console exporter。
    /// </summary>
    public bool ConsoleExporterEnabled { get; set; }

    /// <summary>
    /// 获取或设置 OTLP exporter 配置。
    /// </summary>
    public OtlpExporterSettings Otlp { get; set; } = new();
}

/// <summary>
/// 配置 OpenTelemetry Protocol exporter。
/// </summary>
public sealed class OtlpExporterSettings
{
    /// <summary>
    /// 获取或设置是否启用 OTLP exporter。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 获取或设置 OTLP collector 绝对地址。留空时使用 OpenTelemetry SDK 默认地址或标准环境变量。
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;
}
