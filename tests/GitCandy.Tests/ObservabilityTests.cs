using System.Diagnostics;
using System.Diagnostics.Metrics;
using GitCandy.Configuration;
using GitCandy.Git;
using GitCandy.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace GitCandy.Tests;

[TestClass]
public sealed class ObservabilityTests
{
    [TestMethod]
    public async Task AddGitCandyObservability_WithDefaultConfiguration_RegistersThreeSignalProviders()
    {
        var builder = CreateBuilder();

        builder.AddGitCandyObservability();
        await using var app = builder.Build();

        var settings = app.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
        Assert.IsTrue(settings.Enabled);
        Assert.AreEqual("GitCandy", settings.ServiceName);
        Assert.IsNotNull(app.Services.GetService<TracerProvider>());
        Assert.IsNotNull(app.Services.GetService<MeterProvider>());
        Assert.IsTrue(app.Services.GetServices<ILoggerProvider>().Any(
            provider => string.Equals(
                provider.GetType().FullName,
                "OpenTelemetry.Logs.OpenTelemetryLoggerProvider",
                StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AddGitCandyObservability_WhenDisabled_DoesNotRegisterSignalProviders()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            [$"{ObservabilityOptions.SectionName}:Enabled"] = "false",
            [$"{ObservabilityOptions.SectionName}:TraceSamplingRatio"] = "2",
            [$"{ObservabilityOptions.SectionName}:Otlp:Enabled"] = "true",
            [$"{ObservabilityOptions.SectionName}:Otlp:Endpoint"] = "ftp://unused.example.com"
        });

        builder.AddGitCandyObservability();
        await using var app = builder.Build();

        Assert.IsFalse(app.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value.Enabled);
        Assert.IsNull(app.Services.GetService<TracerProvider>());
        Assert.IsNull(app.Services.GetService<MeterProvider>());
        Assert.IsFalse(app.Services.GetServices<ILoggerProvider>().Any(
            provider => string.Equals(
                provider.GetType().FullName,
                "OpenTelemetry.Logs.OpenTelemetryLoggerProvider",
                StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AddGitCandyObservability_WithInvalidOtlpEndpoint_RejectsConfiguration()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            [$"{ObservabilityOptions.SectionName}:Otlp:Enabled"] = "true",
            [$"{ObservabilityOptions.SectionName}:Otlp:Endpoint"] = "ftp://collector.example.com"
        });

        builder.AddGitCandyObservability();
        await using var app = builder.Build();

        Assert.Throws<OptionsValidationException>(
            () => _ = app.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithMissingRepository_EmitsSanitizedTraceAndMetrics()
    {
        var repositoryRoot = TestDirectory.Create();
        try
        {
            var activities = new List<Activity>();
            using var activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == GitTransportTelemetry.ActivitySourceName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => activities.Add(activity)
            };
            ActivitySource.AddActivityListener(activityListener);

            var measurements = new List<Measurement>();
            using var meterListener = new MeterListener();
            meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == GitTransportTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            meterListener.SetMeasurementEventCallback<long>(
                (instrument, value, tags, _) => measurements.Add(
                    new Measurement(instrument.Name, value, CopyTags(tags))));
            meterListener.SetMeasurementEventCallback<double>(
                (instrument, value, tags, _) => measurements.Add(
                    new Measurement(instrument.Name, value, CopyTags(tags))));
            meterListener.Start();

            var pathResolver = new TestPathResolver(repositoryRoot);
            using var backend = new GitProcessTransportBackend(
                pathResolver,
                new LibGit2RepositoryService(pathResolver),
                new TestExecutableResolver(),
                Options.Create(new GitSmartHttpOptions()),
                [],
                new TestReceiveHookLauncher(),
                NullLogger<GitProcessTransportBackend>.Instance);
            var missingPath = Path.Combine(repositoryRoot, "private-repository");
            var request = new GitTransportRequest(
                new GitRepositoryContext("private-repository", missingPath),
                GitTransportService.UploadPack,
                StatelessRpc: true,
                AdvertiseRefs: false,
                ProtocolVersion: "version=2",
                ActorName: "private-actor");

            await Assert.ThrowsAsync<GitRepositoryNotFoundException>(
                () => backend.ExecuteAsync(request, Stream.Null, Stream.Null));

            Assert.HasCount(1, activities);
            Assert.AreEqual(ActivityStatusCode.Error, activities[0].Status);
            Assert.IsTrue(measurements.Any(measurement =>
                measurement.Name == "gitcandy.git.transport.operations"
                && measurement.Tags.Any(tag => tag is { Key: "gitcandy.result", Value: "error" })));
            Assert.IsTrue(measurements.Any(measurement =>
                measurement.Name == "gitcandy.git.transport.duration"));

            var exportedText = string.Join(
                '|',
                activities[0].Tags.Select(tag => $"{tag.Key}={tag.Value}")
                    .Concat(measurements.SelectMany(measurement => measurement.Tags)
                        .Select(tag => $"{tag.Key}={tag.Value}")));
            Assert.IsFalse(exportedText.Contains("private-repository", StringComparison.Ordinal));
            Assert.IsFalse(exportedText.Contains("private-actor", StringComparison.Ordinal));
            Assert.IsFalse(exportedText.Contains(repositoryRoot, StringComparison.Ordinal));
        }
        finally
        {
            TestDirectory.Delete(repositoryRoot);
        }
    }

    private static WebApplicationBuilder CreateBuilder(
        IReadOnlyDictionary<string, string?>? configuration = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            EnvironmentName = Environments.Development
        });
        if (configuration is not null)
        {
            builder.Configuration.AddInMemoryCollection(configuration);
        }

        return builder;
    }

    private static KeyValuePair<string, object?>[] CopyTags(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        return tags.ToArray();
    }

    private sealed class TestReceiveHookLauncher : IGitReceiveHookLauncher
    {
        public void Configure(ProcessStartInfo startInfo, GitTransportRequest request)
        {
        }
    }

    private sealed record Measurement(
        string Name,
        double Value,
        IReadOnlyList<KeyValuePair<string, object?>> Tags);

    private sealed class TestPathResolver(string repositoryRoot) : IGitRepositoryPathResolver
    {
        public string RepositoryRootPath { get; } = repositoryRoot;

        public string ResolveRepositoryPath(string repositoryName)
        {
            return Path.GetFullPath(repositoryName, RepositoryRootPath);
        }
    }

    private sealed class TestExecutableResolver : IGitExecutableResolver
    {
        public string Resolve()
        {
            throw new InvalidOperationException("A missing repository must fail before resolving Git.");
        }
    }
}
