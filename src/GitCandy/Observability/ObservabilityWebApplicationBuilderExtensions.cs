using GitCandy.Git;
using GitCandy.Schedules;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GitCandy.Observability;

/// <summary>
/// 注册 GitCandy OpenTelemetry tracing、metrics 和 logging。
/// </summary>
public static class ObservabilityWebApplicationBuilderExtensions
{
    /// <summary>
    /// 从 <c>GitCandy:Observability</c> 注册可观测性 provider 和 exporter。
    /// </summary>
    /// <param name="builder">Web 应用构建器。</param>
    /// <returns>同一个 Web 应用构建器。</returns>
    public static WebApplicationBuilder AddGitCandyObservability(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var section = builder.Configuration.GetSection(ObservabilityOptions.SectionName);
        var settings = section.Get<ObservabilityOptions>() ?? new ObservabilityOptions();
        builder.Services.AddOptions<ObservabilityOptions>()
            .Bind(section)
            .Validate(
                options => !options.Enabled
                    || (!string.IsNullOrWhiteSpace(options.ServiceName)
                        && options.ServiceName.Length <= 255),
                $"{nameof(ObservabilityOptions.ServiceName)} must contain 1 to 255 characters.")
            .Validate(
                options => !options.Enabled
                    || options.TraceSamplingRatio is >= 0 and <= 1,
                $"{nameof(ObservabilityOptions.TraceSamplingRatio)} must be between 0 and 1.")
            .Validate(
                options => !options.Enabled
                    || !options.Otlp.Enabled
                    || string.IsNullOrWhiteSpace(options.Otlp.Endpoint)
                    || IsValidOtlpEndpoint(options.Otlp.Endpoint),
                "The enabled OTLP endpoint must be an absolute HTTP or HTTPS URI.")
            .ValidateOnStart();

        if (!settings.Enabled)
        {
            return builder;
        }

        var serviceName = string.IsNullOrWhiteSpace(settings.ServiceName)
            ? "GitCandy"
            : settings.ServiceName;
        var samplingRatio = settings.TraceSamplingRatio is >= 0 and <= 1
            ? settings.TraceSamplingRatio
            : 1;
        var serviceVersion = typeof(ObservabilityWebApplicationBuilderExtensions)
            .Assembly
            .GetName()
            .Version?
            .ToString();
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName,
                serviceVersion: serviceVersion))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new TraceIdRatioBasedSampler(samplingRatio))
                    .AddSource(GitTransportTelemetry.ActivitySourceName)
                    .AddSource(SchedulerTelemetry.ActivitySourceName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = context => !context.Request.Path.StartsWithSegments(
                            "/health/live",
                            StringComparison.OrdinalIgnoreCase);
                    });

                AddTraceExporters(tracing, settings);
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(GitTransportTelemetry.MeterName)
                    .AddMeter(SchedulerTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation();

                AddMetricExporters(metrics, settings);
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = false;
            logging.ParseStateValues = true;

            if (settings.ConsoleExporterEnabled)
            {
                logging.AddConsoleExporter();
            }

            if (settings.Otlp.Enabled)
            {
                logging.AddOtlpExporter(options => ConfigureOtlpExporter(options, settings.Otlp));
            }
        });

        return builder;
    }

    private static void AddTraceExporters(
        TracerProviderBuilder tracing,
        ObservabilityOptions settings)
    {
        if (settings.ConsoleExporterEnabled)
        {
            tracing.AddConsoleExporter();
        }

        if (settings.Otlp.Enabled)
        {
            tracing.AddOtlpExporter(options => ConfigureOtlpExporter(options, settings.Otlp));
        }
    }

    private static void AddMetricExporters(
        MeterProviderBuilder metrics,
        ObservabilityOptions settings)
    {
        if (settings.ConsoleExporterEnabled)
        {
            metrics.AddConsoleExporter();
        }

        if (settings.Otlp.Enabled)
        {
            metrics.AddOtlpExporter(options => ConfigureOtlpExporter(options, settings.Otlp));
        }
    }

    private static void ConfigureOtlpExporter(
        OtlpExporterOptions options,
        OtlpExporterSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Endpoint)
            && IsValidOtlpEndpoint(settings.Endpoint))
        {
            options.Endpoint = new Uri(settings.Endpoint, UriKind.Absolute);
        }
    }

    private static bool IsValidOtlpEndpoint(string endpoint)
    {
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }
}
