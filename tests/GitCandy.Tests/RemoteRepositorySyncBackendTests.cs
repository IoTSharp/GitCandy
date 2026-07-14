using System.Collections.Concurrent;
using System.Text;
using GitCandy.Configuration;
using GitCandy.Git;
using GitCandy.Remotes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GitCandy.Tests;

[TestClass]
public sealed class RemoteRepositorySyncBackendTests
{
    private const string Secret = "remote-sync-test-secret";

    [TestMethod]
    public void RemoteRepositorySyncRequest_WithUnsafeOriginOrRefSpec_RejectsRequest()
    {
        var repository = new GitRepositoryContext("fixture", "C:/repositories/fixture.git");
        var providerUrl = new Uri("https://github.example/enterprise");

        Assert.ThrowsExactly<ArgumentException>(() => new RemoteRepositorySyncRequest(
            repository,
            RemoteProviderKind.GitHub,
            providerUrl,
            new Uri("https://attacker.example/enterprise/owner/repository.git"),
            RemoteRepositorySyncOperation.Fetch,
            [new RemoteRepositorySyncRefSpec("refs/heads/*", "refs/heads/*")]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new RemoteRepositorySyncRefSpec("refs/heads/main --upload-pack=bad", "refs/heads/main"));
        Assert.ThrowsExactly<ArgumentException>(() => new RemoteRepositorySyncRequest(
            repository,
            RemoteProviderKind.GitHub,
            providerUrl,
            new Uri("https://github.example/enterprise/owner/repository.git"),
            RemoteRepositorySyncOperation.Fetch,
            [RemoteRepositorySyncRefSpec.Delete("refs/heads/main")]));
    }

    [TestMethod]
    public async Task ExecuteAsync_WithBasicChallenge_UsesCredentialHelperAndRedactsSecret()
    {
        await using var remote = await RemoteGitFixture.CreateAsync(RemoteGitFixtureMode.NotFound);
        await using var backend = BackendFixture.Create(TimeSpan.FromSeconds(30));
        var request = CreateRequest(backend.Repository, remote);
        var credential = CreateCredential();

        var exception = await Assert.ThrowsExactlyAsync<RemoteRepositorySyncException>(
            () => backend.SyncBackend.ExecuteAsync(request, credential));

        Assert.AreEqual(RemoteRepositorySyncErrorCodes.RepositoryNotFound, exception.Code);
        Assert.IsTrue(remote.CredentialObserved);
        Assert.IsFalse(remote.SecretObservedInUrl);
        Assert.IsFalse(backend.Logs.Any(message => message.Contains(Secret, StringComparison.Ordinal)));
        Assert.IsFalse(backend.Logs.Any(message =>
            message.Contains(request.RemoteGitUrl.AbsoluteUri, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ExecuteAsync_WithPushDeletion_UsesReceivePackThroughSameCredentialBoundary()
    {
        await using var remote = await RemoteGitFixture.CreateAsync(RemoteGitFixtureMode.NotFound);
        await using var backend = BackendFixture.Create(TimeSpan.FromSeconds(30));
        var request = new RemoteRepositorySyncRequest(
            backend.Repository,
            RemoteProviderKind.GitHub,
            new Uri(remote.BaseAddress, "github"),
            new Uri(remote.BaseAddress, "github/owner/repository.git"),
            RemoteRepositorySyncOperation.Push,
            [RemoteRepositorySyncRefSpec.Delete("refs/heads/removed")]);

        var exception = await Assert.ThrowsExactlyAsync<RemoteRepositorySyncException>(() =>
            backend.SyncBackend.ExecuteAsync(request, CreateCredential()));

        Assert.AreEqual(RemoteRepositorySyncErrorCodes.RepositoryNotFound, exception.Code);
        Assert.AreEqual("git-receive-pack", remote.ObservedService);
        Assert.IsTrue(remote.CredentialObserved);
        Assert.IsFalse(remote.SecretObservedInUrl);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithConfiguredTimeout_KillsProcessAndReturnsStableCode()
    {
        await using var remote = await RemoteGitFixture.CreateAsync(RemoteGitFixtureMode.Hang);
        await using var backend = BackendFixture.Create(TimeSpan.FromSeconds(3));
        var task = backend.SyncBackend.ExecuteAsync(
            CreateRequest(backend.Repository, remote),
            CreateCredential());
        await remote.AuthorizedRequest.WaitAsync(TimeSpan.FromSeconds(10));

        var exception = await Assert.ThrowsExactlyAsync<RemoteRepositorySyncException>(() => task);

        Assert.AreEqual(RemoteRepositorySyncErrorCodes.TimedOut, exception.Code);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithCallerCancellation_KillsProcessAndPropagatesCancellation()
    {
        await using var remote = await RemoteGitFixture.CreateAsync(RemoteGitFixtureMode.Hang);
        await using var backend = BackendFixture.Create(TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();
        var task = backend.SyncBackend.ExecuteAsync(
            CreateRequest(backend.Repository, remote),
            CreateCredential(),
            cancellation.Token);
        await remote.AuthorizedRequest.WaitAsync(TimeSpan.FromSeconds(10));

        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => task);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithRepositoryOutsideRoot_RejectsBeforeNetworkAccess()
    {
        await using var remote = await RemoteGitFixture.CreateAsync(RemoteGitFixtureMode.NotFound);
        await using var backend = BackendFixture.Create(TimeSpan.FromSeconds(30));
        var outside = new GitRepositoryContext(
            backend.Repository.RepositoryName,
            Path.Combine(backend.RootPath, "outside", "repository.git"));
        var request = CreateRequest(outside, remote);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            backend.SyncBackend.ExecuteAsync(request, CreateCredential()));

        Assert.IsFalse(remote.AnyRequestObserved);
    }

    private static RemoteRepositorySyncRequest CreateRequest(
        GitRepositoryContext repository,
        RemoteGitFixture remote) => new(
            repository,
            RemoteProviderKind.GitHub,
            new Uri(remote.BaseAddress, "github"),
            new Uri(remote.BaseAddress, "github/owner/repository.git"),
            RemoteRepositorySyncOperation.Fetch,
            [new RemoteRepositorySyncRefSpec("refs/heads/*", "refs/remotes/mirror/*")],
            prune: true);

    private static RemoteCredential CreateCredential() => new(
        RemoteAuthenticationKind.PersonalAccessToken,
        new RemoteSecret(Secret),
        ["repo"]);

    private enum RemoteGitFixtureMode
    {
        NotFound,
        Hang
    }

    private sealed class RemoteGitFixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly RemoteGitFixtureMode _mode;
        private readonly TaskCompletionSource _authorizedRequest = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        private RemoteGitFixture(WebApplication app, RemoteGitFixtureMode mode)
        {
            _app = app;
            _mode = mode;
            BaseAddress = new Uri("http://127.0.0.1");
        }

        public Uri BaseAddress { get; private set; }

        public bool AnyRequestObserved { get; private set; }

        public bool CredentialObserved { get; private set; }

        public bool SecretObservedInUrl { get; private set; }

        public string? ObservedService { get; private set; }

        public Task AuthorizedRequest => _authorizedRequest.Task;

        public static async Task<RemoteGitFixture> CreateAsync(RemoteGitFixtureMode mode)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var fixture = new RemoteGitFixture(app, mode);
            app.MapMethods(
                "/github/owner/repository.git/{**path}",
                [HttpMethods.Get, HttpMethods.Post],
                fixture.HandleAsync);
            await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?
                .Addresses;
            Assert.IsNotNull(addresses);
            fixture.BaseAddress = new Uri(addresses.Single());
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private async Task HandleAsync(HttpContext context)
        {
            AnyRequestObserved = true;
            ObservedService = context.Request.Query["service"];
            SecretObservedInUrl |= context.Request.Path.Value?.Contains(Secret, StringComparison.Ordinal) == true
                || context.Request.QueryString.Value?.Contains(Secret, StringComparison.Ordinal) == true;
            var expected = "Basic " + Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"x-access-token:{Secret}"));
            if (!string.Equals(context.Request.Headers.Authorization, expected, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Basic realm=\"GitCandy remote fixture\"";
                return;
            }

            CredentialObserved = true;
            _authorizedRequest.TrySetResult();
            if (_mode == RemoteGitFixtureMode.Hang)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("not found", context.RequestAborted);
        }
    }

    private sealed class BackendFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;

        private BackendFixture(
            string rootPath,
            ServiceProvider serviceProvider,
            GitRepositoryContext repository,
            CapturingLoggerProvider loggerProvider)
        {
            RootPath = rootPath;
            _serviceProvider = serviceProvider;
            Repository = repository;
            Logs = loggerProvider.Messages;
            SyncBackend = serviceProvider.GetRequiredService<IRemoteRepositorySyncBackend>();
        }

        public string RootPath { get; }

        public GitRepositoryContext Repository { get; }

        public IRemoteRepositorySyncBackend SyncBackend { get; }

        public IReadOnlyCollection<string> Logs { get; }

        public static BackendFixture Create(TimeSpan timeout)
        {
            var rootPath = TestDirectory.Create();
            var paths = new FixtureApplicationPaths(rootPath);
            var loggerProvider = new CapturingLoggerProvider();
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddProvider(loggerProvider));
            services.AddSingleton<IGitCandyApplicationPaths>(paths);
            services.Configure<RemoteRepositorySyncOptions>(options =>
            {
                options.OperationTimeout = timeout;
                options.StreamBufferSize = 4096;
                options.MaxDiagnosticCharacters = 4096;
            });
            services.Configure<RemoteGitCredentialHelperOptions>(options =>
                options.CommandAssemblyPath = Path.Combine(AppContext.BaseDirectory, "GitCandy.dll"));
            services.AddGitCandyGit();
            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var repository = serviceProvider.GetRequiredService<IManagedGitRepositoryService>()
                .InitializeBare("remote-sync-fixture");
            return new BackendFixture(rootPath, serviceProvider, repository, loggerProvider);
        }

        public async ValueTask DisposeAsync()
        {
            await _serviceProvider.DisposeAsync();
            TestDirectory.Delete(RootPath);
        }
    }

    private sealed class FixtureApplicationPaths(string rootPath) : IGitCandyApplicationPaths
    {
        public string ContentRootPath { get; } = Path.GetFullPath(rootPath);

        public string? WebRootPath => null;

        public string UserConfigurationPath => Path.Combine(ContentRootPath, "config.xml");

        public string RepositoryPath => Path.Combine(ContentRootPath, "repositories");

        public string CachePath => Path.Combine(ContentRootPath, "cache");

        public string GitCorePath => string.Empty;

        public string SshHostKeyPath => Path.Combine(ContentRootPath, "ssh-key.xml");

        public string DataProtectionKeysPath => Path.Combine(ContentRootPath, "keys");

        public string ResolveContentPath(string configuredPath) => Resolve(ContentRootPath, configuredPath);

        public string ResolveWebRootPath(string configuredPath) => throw new InvalidOperationException();

        public string ResolvePathWithinRepositoryRoot(string path) => Resolve(RepositoryPath, path);

        public string ResolvePathWithinCacheRoot(string path) => Resolve(CachePath, path);

        private static string Resolve(string root, string path)
        {
            var normalizedRoot = Path.GetFullPath(root);
            var resolved = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(path, normalizedRoot);
            var relative = Path.GetRelativePath(normalizedRoot, resolved);
            if (relative.Equals("..", StringComparison.Ordinal)
                || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.IsPathRooted(relative))
            {
                throw new InvalidOperationException("The fixture path escaped its root.");
            }

            return resolved;
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
        }
    }
}
