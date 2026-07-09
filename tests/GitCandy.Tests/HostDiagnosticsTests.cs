using GitCandy.Configuration;
using GitCandy.Diagnostics;
using GitCandy.Schedules;
using GitCandy.Ssh;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitCandy.Tests;

[TestClass]
public sealed class HostDiagnosticsTests
{
    [TestMethod]
    public async Task GitCandyHostDiagnosticsHostedService_WithLifecycleEvents_WritesStartupAndShutdownLogs()
    {
        using var provider = new CapturingLoggerProvider();
        using var loggerFactory = CreateLoggerFactory(provider);
        var environment = new TestWebHostEnvironment
        {
            ApplicationName = "GitCandy.Tests",
            ContentRootPath = GetWebProjectRoot(),
            EnvironmentName = Environments.Development,
            WebRootPath = Path.Combine(GetWebProjectRoot(), "wwwroot")
        };
        var service = new GitCandyHostDiagnosticsHostedService(
            environment,
            Options.Create(new GitCandyApplicationOptions
            {
                EnableSsh = false,
                SshPort = 2022
            }),
            loggerFactory.CreateLogger<GitCandyHostDiagnosticsHostedService>());

        await service.StartingAsync(CancellationToken.None);
        await service.StartedAsync(CancellationToken.None);
        await service.StoppingAsync(CancellationToken.None);
        await service.StoppedAsync(CancellationToken.None);

        AssertContains(provider.Entries, LogLevel.Information, "GitCandy host is starting.");
        AssertContains(provider.Entries, LogLevel.Information, "SshEnabled=False; SshPort=2022");
        AssertContains(provider.Entries, LogLevel.Information, "GitCandy host started.");
        AssertContains(provider.Entries, LogLevel.Information, "GitCandy host is stopping.");
        AssertContains(provider.Entries, LogLevel.Information, "GitCandy host stopped.");
    }

    [TestMethod]
    public async Task SshServerHostedService_WithRuntimeStartFailure_LogsPortDiagnosticAndRethrows()
    {
        using var provider = new CapturingLoggerProvider();
        using var loggerFactory = CreateLoggerFactory(provider);
        var expectedException = new InvalidOperationException("listener failed");
        var service = new SshServerHostedService(
            Options.Create(new GitCandyApplicationOptions
            {
                EnableSsh = true,
                SshPort = 2022
            }),
            new ThrowingSshServerRuntime(expectedException),
            loggerFactory.CreateLogger<SshServerHostedService>());

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.AreSame(expectedException, exception);
        var entry = AssertContains(
            provider.Entries,
            LogLevel.Error,
            "Failed to start built-in SSH server on port 2022.");
        Assert.AreSame(expectedException, entry.Exception);
    }

    [TestMethod]
    public async Task AddGitCandyWebShell_WithDuplicateSchedulerJobs_LogsSchedulerStartupFailure()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(tempRoot, "GitCandy.db");
        using var provider = new CapturingLoggerProvider();

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = GetWebProjectRoot(),
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseKestrel();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            builder.Logging.AddProvider(provider);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Application:EnableSsh"] = "false",
                ["GitCandy:Database:Provider"] = "sqlite",
                ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
            });
            builder.Services.AddGitCandyWebShell(builder.Configuration);
            builder.Services.AddScoped<ISchedulerJob, DuplicateSchedulerJobA>();
            builder.Services.AddScoped<ISchedulerJob, DuplicateSchedulerJobB>();

            await using var app = builder.Build();

            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => app.StartAsync());

            StringAssert.Contains(exception.Message, DuplicateSchedulerJobA.JobName);
            AssertContains(
                provider.Entries,
                LogLevel.Error,
                "Failed to register GitCandy scheduler jobs during application startup.");
        }
        finally
        {
            ResetQuartzLogging();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static ILoggerFactory CreateLoggerFactory(ILoggerProvider provider)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(provider);
        });
    }

    private static CapturedLog AssertContains(
        IReadOnlyList<CapturedLog> entries,
        LogLevel level,
        string expectedMessage)
    {
        foreach (var entry in entries)
        {
            if (entry.Level == level
                && entry.Message.Contains(expectedMessage, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        Assert.Fail(
            $"Expected {level} log containing '{expectedMessage}'. Actual logs:{Environment.NewLine}"
            + string.Join(
                Environment.NewLine,
                entries.Select(entry => $"{entry.Level}: {entry.CategoryName}: {entry.Message}")));
        throw new InvalidOperationException("Unreachable assertion failure.");
    }

    private static string GetWebProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GitCandy.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory);
        return Path.Combine(directory.FullName, "src", "GitCandy");
    }

    private static void ResetQuartzLogging()
    {
        Quartz.Logging.LogProvider.SetCurrentLogProvider(new NoopQuartzLogProvider());
    }

    private sealed class DuplicateSchedulerJobA : ISchedulerJob
    {
        public const string JobName = "DuplicateDiagnosticJob";

        public string Name => JobName;

        public SchedulerJobType JobType => SchedulerJobType.RealTime;

        public ValueTask ExecuteAsync(
            SchedulerJobContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public TimeSpan GetNextInterval(SchedulerJobContext context)
        {
            return TimeSpan.MaxValue;
        }
    }

    private sealed class DuplicateSchedulerJobB : ISchedulerJob
    {
        public string Name => DuplicateSchedulerJobA.JobName;

        public SchedulerJobType JobType => SchedulerJobType.RealTime;

        public ValueTask ExecuteAsync(
            SchedulerJobContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public TimeSpan GetNextInterval(SchedulerJobContext context)
        {
            return TimeSpan.MaxValue;
        }
    }

    private sealed class ThrowingSshServerRuntime(Exception exception) : ISshServerRuntime
    {
        public Task StartAsync(int port, CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<CapturedLog> _entries = [];

        public IReadOnlyList<CapturedLog> Entries
        {
            get
            {
                lock (_entries)
                {
                    return _entries.ToArray();
                }
            }
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(categoryName, _entries);
        }

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(string categoryName, List<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NoopScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            lock (entries)
            {
                entries.Add(new CapturedLog(
                    categoryName,
                    logLevel,
                    eventId,
                    formatter(state, exception),
                    exception));
            }
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "GitCandy.Tests";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = string.Empty;

        public string EnvironmentName { get; set; } = Environments.Development;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = string.Empty;
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed class NoopQuartzLogProvider : Quartz.Logging.ILogProvider
    {
        public Quartz.Logging.Logger GetLogger(string name)
        {
            return (_, _, _, _) => false;
        }

        public IDisposable OpenNestedContext(string message)
        {
            return NoopScope.Instance;
        }

        public IDisposable OpenMappedContext(string key, object value, bool destructure)
        {
            return NoopScope.Instance;
        }
    }

    private sealed record CapturedLog(
        string CategoryName,
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception);
}
