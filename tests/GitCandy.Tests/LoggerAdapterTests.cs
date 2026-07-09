using GitCandy.Configuration;
using GitCandy.Log;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GitCandy.Tests;

[TestClass]
public sealed class LoggerAdapterTests
{
    [TestMethod]
    public void Info_WithLegacyCompositeFormat_WritesInformationLog()
    {
        using var provider = new CapturingLoggerProvider();
        using ILoggerFactory loggerFactory = CreateLoggerFactory(provider);
        Logger.Configure(loggerFactory);

        Logger.Info("Repository {0} deleted by {1}#{2}", "sample", "alice", 42);

        var entry = AssertSingle(provider.Entries);
        Assert.AreEqual("GitCandy.LegacyLogger", entry.CategoryName);
        Assert.AreEqual(LogLevel.Information, entry.Level);
        Assert.AreEqual("LegacyLogger", entry.EventId.Name);
        Assert.AreEqual("Repository sample deleted by alice#42", entry.Message);
    }

    [TestMethod]
    public void Write_WithLegacyWarningAndErrorLevels_MapsToMicrosoftLogLevels()
    {
        using var provider = new CapturingLoggerProvider();
        using ILoggerFactory loggerFactory = CreateLoggerFactory(provider);
        Logger.Configure(loggerFactory);

        Logger.Write(LogLevels.Warning, "configuration warning");
        Logger.Error("transport failed");

        var entries = provider.Entries;
        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual(LogLevel.Warning, entries[0].Level);
        Assert.AreEqual("configuration warning", entries[0].Message);
        Assert.AreEqual(LogLevel.Error, entries[1].Level);
        Assert.AreEqual("transport failed", entries[1].Message);
    }

    [TestMethod]
    public async Task ConfigureGitCandyLegacyLogger_WithWebApplication_BindsStaticLoggerAdapter()
    {
        using var provider = new CapturingLoggerProvider();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = GetWebProjectRoot(),
            EnvironmentName = Environments.Development,
        });
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(provider);

        await using var app = builder.Build();
        app.ConfigureGitCandyLegacyLogger();

        Logger.Warning("adapter configured");

        var entry = AssertSingle(provider.Entries);
        Assert.AreEqual(LogLevel.Warning, entry.Level);
        Assert.AreEqual("adapter configured", entry.Message);
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

    private static CapturedLog AssertSingle(IReadOnlyList<CapturedLog> entries)
    {
        Assert.AreEqual(1, entries.Count);
        return entries[0];
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
                entries.Add(new CapturedLog(categoryName, logLevel, eventId, formatter(state, exception)));
            }
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed record CapturedLog(string CategoryName, LogLevel Level, EventId EventId, string Message);
}
