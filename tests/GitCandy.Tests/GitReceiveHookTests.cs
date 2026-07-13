using System.Diagnostics;
using GitCandy.Git;
using GitCandy.Governance;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Tests;

[TestClass]
[DoNotParallelize]
public sealed class GitReceiveHookTests
{
    [TestMethod]
    public async Task ExecuteAsync_WithProtectedRefRejection_ReturnsFailureAndWritesSafeReason()
    {
        var gate = new CapturingPushGate(new GitPushGateResult(false, ["refs/heads/main: direct pushes are disabled"]));
        await using var provider = CreateServices(gate);
        using var environment = new HookEnvironment(
            ("GITCANDY_REPOSITORY_ID", "42"),
            ("GITCANDY_ACTOR_NAME", "build-agent"),
            ("GITCANDY_ACTOR_USER_ID", "user-42"),
            ("GITCANDY_DEPLOY_KEY_ID", null));
        var input = new StringReader($"{new string('0', 40)} {new string('a', 40)} refs/heads/main\n");
        var error = new StringWriter();

        var exitCode = await provider.GetRequiredService<IGitReceiveHookRunner>()
            .ExecuteAsync(input, error);

        Assert.AreEqual(1, exitCode);
        Assert.IsNotNull(gate.Request);
        Assert.AreEqual(42L, gate.Request.RepositoryId);
        Assert.AreEqual("user-42", gate.Request.Actor.UserId);
        Assert.AreEqual("refs/heads/main", gate.Request.Updates.Single().ReferenceName);
        StringAssert.Contains(error.ToString(), "GitCandy push rejected");
        Assert.IsFalse(error.ToString().Contains("GITCANDY_", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ExecuteAsync_WithMalformedRefInput_ReturnsFailureBeforePolicyEvaluation()
    {
        var gate = new CapturingPushGate(GitPushGateResult.Allow);
        await using var provider = CreateServices(gate);
        using var environment = new HookEnvironment(
            ("GITCANDY_REPOSITORY_ID", "42"),
            ("GITCANDY_ACTOR_NAME", "build-agent"),
            ("GITCANDY_ACTOR_USER_ID", null),
            ("GITCANDY_DEPLOY_KEY_ID", "9"));

        var exitCode = await provider.GetRequiredService<IGitReceiveHookRunner>()
            .ExecuteAsync(new StringReader("not-a-ref-update\n"), new StringWriter());

        Assert.AreEqual(1, exitCode);
        Assert.IsNull(gate.Request);
    }

    private static ServiceProvider CreateServices(IGitPushGate gate)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton(gate);
        services.AddSingleton<IGitExecutableResolver>(new TestGitExecutableResolver());
        services.AddGitCandyGit();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class TestGitExecutableResolver : IGitExecutableResolver
    {
        public string Resolve() => "git";
    }

    private sealed class CapturingPushGate(GitPushGateResult result) : IGitPushGate
    {
        public GitPushGateRequest? Request { get; private set; }

        public Task<GitPushGateResult> EvaluateAsync(
            GitPushGateRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(result);
        }
    }

    private sealed class HookEnvironment : IDisposable
    {
        private readonly IReadOnlyList<(string Name, string? Value)> _original;

        public HookEnvironment(params (string Name, string? Value)[] values)
        {
            _original = values.Select(value =>
                (value.Name, Environment.GetEnvironmentVariable(value.Name))).ToArray();
            foreach (var value in values)
            {
                Environment.SetEnvironmentVariable(value.Name, value.Value);
            }
        }

        public void Dispose()
        {
            foreach (var value in _original)
            {
                Environment.SetEnvironmentVariable(value.Name, value.Value);
            }
        }
    }
}
