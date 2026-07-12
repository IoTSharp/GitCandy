using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using GitCandy.Application;
using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz.Logging;

namespace GitCandy.Tests;

[TestClass]
public sealed class SshGitIntegrationTests
{
    [TestMethod]
    public async Task BuiltInSshServer_WithRealGitClient_ClonesFetchesAndPushes()
    {
        await using var fixture = await SshGitFixture.CreateAsync();
        var clonePath = Path.Combine(fixture.TempRoot, "clone");

        await fixture.RunGitAsync(["clone", fixture.CurrentRemoteUrl, clonePath]);
        Assert.IsTrue(File.Exists(Path.Combine(clonePath, "README.md")));

        File.AppendAllText(
            Path.Combine(fixture.SeedWorkTree, "README.md"),
            "fetch over SSH\n",
            Encoding.UTF8);
        await fixture.RunGitAsync(["-C", fixture.SeedWorkTree, "add", "README.md"], useSsh: false);
        await fixture.RunGitAsync(
            ["-C", fixture.SeedWorkTree, "commit", "-m", "M7 SSH fetch verification"],
            useSsh: false);
        await fixture.RunGitAsync(
            ["-C", fixture.SeedWorkTree, "push", "origin", "main"],
            useSsh: false);
        await fixture.RunGitAsync(["-C", clonePath, "fetch", "origin"]);

        var remoteHead = await fixture.RunGitForOutputAsync(
            ["-C", clonePath, "rev-parse", "origin/main"]);
        var bareHead = await fixture.RunGitForOutputAsync(
            ["--git-dir", fixture.BareRepositoryPath, "rev-parse", "refs/heads/main"],
            useSsh: false);
        Assert.AreEqual(bareHead, remoteHead);

        File.AppendAllText(
            Path.Combine(clonePath, "README.md"),
            "push over SSH\n",
            Encoding.UTF8);
        await fixture.RunGitAsync(["-C", clonePath, "config", "user.name", "GitCandy M7 Bot"]);
        await fixture.RunGitAsync(["-C", clonePath, "config", "user.email", "m7@gitcandy.local"]);
        await fixture.RunGitAsync(["-C", clonePath, "add", "README.md"]);
        await fixture.RunGitAsync(["-C", clonePath, "commit", "-m", "M7 SSH push verification"]);
        await fixture.RunGitAsync(["-C", clonePath, "push", "origin", "HEAD:refs/heads/m7-ssh"]);

        var pushedHead = await fixture.RunGitForOutputAsync(
            ["--git-dir", fixture.BareRepositoryPath, "rev-parse", "refs/heads/m7-ssh"],
            useSsh: false);
        var localHead = await fixture.RunGitForOutputAsync(["-C", clonePath, "rev-parse", "HEAD"]);
        Assert.AreEqual(localHead, pushedHead);

        await fixture.AssertSshCommandRejectedAsync("whoami");
        await fixture.AssertSshCommandRejectedAsync("git-upload-pack '/git/private-demo.git'");
        await fixture.AssertSshCommandRejectedAsync("git-upload-pack '/m7-previous/private-old.git'");
        await fixture.AssertSshCommandRejectedAsync("git-upload-pack '/m7-owner/private-demo'");
    }

    private sealed class SshGitFixture : IAsyncDisposable
    {
        private SshGitFixture(
            WebApplication app,
            string tempRoot,
            string bareRepositoryPath,
            string seedWorkTree,
            string remoteUrl,
            string currentRemoteUrl,
            string sshCommand,
            string privateKeyPath,
            int sshPort)
        {
            App = app;
            TempRoot = tempRoot;
            BareRepositoryPath = bareRepositoryPath;
            SeedWorkTree = seedWorkTree;
            RemoteUrl = remoteUrl;
            CurrentRemoteUrl = currentRemoteUrl;
            SshCommand = sshCommand;
            PrivateKeyPath = privateKeyPath;
            SshPort = sshPort;
        }

        private WebApplication App { get; }

        private string SshCommand { get; }

        private string PrivateKeyPath { get; }

        private int SshPort { get; }

        public string TempRoot { get; }

        public string BareRepositoryPath { get; }

        public string SeedWorkTree { get; }

        public string RemoteUrl { get; }

        public string CurrentRemoteUrl { get; }

        public static async Task<SshGitFixture> CreateAsync()
        {
            var tempRoot = TestDirectory.Create();
            var repositoryRoot = Path.Combine(tempRoot, "Repositories");
            var bareRepositoryPath = Path.Combine(repositoryRoot, "private-demo.git");
            var seedWorkTree = Path.Combine(tempRoot, "seed-worktree");
            var privateKeyPath = Path.Combine(tempRoot, "id_rsa");
            var sshPort = GetAvailablePort();
            Directory.CreateDirectory(repositoryRoot);

            WebApplication? app = null;
            try
            {
                await RunProcessAsync(
                    OperatingSystem.IsWindows() ? "ssh-keygen.exe" : "ssh-keygen",
                    ["-q", "-t", "rsa", "-b", "3072", "-N", string.Empty, "-C", "gitcandy-m7", "-f", privateKeyPath]);
                var publicKeyParts = (await File.ReadAllTextAsync(privateKeyPath + ".pub"))
                    .Trim()
                    .Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
                Assert.AreEqual("ssh-rsa", publicKeyParts[0]);
                var publicKeyBytes = Convert.FromBase64String(publicKeyParts[1]);

                await RunGitProcessAsync(
                    ["init", "--bare", "--initial-branch=main", bareRepositoryPath],
                    sshCommand: null);
                await RunGitProcessAsync(
                    ["init", "--initial-branch=main", seedWorkTree],
                    sshCommand: null);
                await File.WriteAllTextAsync(
                    Path.Combine(seedWorkTree, "README.md"),
                    "# GitCandy M7\n",
                    Encoding.UTF8);
                await RunGitProcessAsync(
                    ["-C", seedWorkTree, "config", "user.name", "GitCandy M7 Bot"],
                    sshCommand: null);
                await RunGitProcessAsync(
                    ["-C", seedWorkTree, "config", "user.email", "m7@gitcandy.local"],
                    sshCommand: null);
                await RunGitProcessAsync(["-C", seedWorkTree, "add", "README.md"], sshCommand: null);
                await RunGitProcessAsync(
                    ["-C", seedWorkTree, "commit", "-m", "M7 initial commit"],
                    sshCommand: null);
                await RunGitProcessAsync(
                    ["-C", seedWorkTree, "remote", "add", "origin", bareRepositoryPath],
                    sshCommand: null);
                await RunGitProcessAsync(
                    ["-C", seedWorkTree, "push", "origin", "main"],
                    sshCommand: null);

                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    ContentRootPath = GetWebProjectRoot(),
                    EnvironmentName = Environments.Development
                });
                builder.WebHost.UseKestrel();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["ConnectionStrings:GitCandy"] =
                        $"Data Source={Path.Combine(tempRoot, "GitCandy.db")};Pooling=False",
                    ["GitCandy:Application:RepositoryPath"] = repositoryRoot,
                    ["GitCandy:Application:CachePath"] = Path.Combine(tempRoot, "Caches"),
                    ["GitCandy:Application:SshHostKeyPath"] = Path.Combine(tempRoot, "ssh-host-key.xml"),
                    ["GitCandy:Application:SshPort"] = sshPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["GitCandy:Application:EnableSsh"] = "true"
                });
                builder.Services.AddGitCandyWebShell(builder.Configuration);
                app = builder.Build();

                await using (var scope = app.Services.CreateAsyncScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
                    await dbContext.Database.EnsureCreatedAsync();
                    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
                    var owner = new GitCandyUser
                    {
                        UserName = "m7-owner",
                        Email = "m7-owner@example.com",
                        DisplayName = "M7 Owner"
                    };
                    var createResult = await userManager.CreateAsync(owner);
                    Assert.IsTrue(createResult.Succeeded);
                    var namespaceService = scope.ServiceProvider.GetRequiredService<INamespaceProvisioningService>();
                    var namespaceId = await namespaceService.EnsureUserNamespaceAsync(owner.Id);
                    Assert.IsNotNull(namespaceId);

                    dbContext.SshKeys.Add(new GitCandySshKey
                    {
                        UserId = owner.Id,
                        KeyType = publicKeyParts[0],
                        PublicKey = publicKeyParts[1],
                        Fingerprint = Convert.ToBase64String(SHA256.HashData(publicKeyBytes)).TrimEnd('='),
                        ImportedAtUtc = DateTime.UtcNow
                    });
                    var repository = new GitCandyRepository
                    {
                        NamespaceId = namespaceId.Value,
                        Name = "private-demo",
                        StorageName = "private-demo",
                        Description = "M7 real Git SSH fixture",
                        IsPrivate = true,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    repository.UserRoles.Add(new GitCandyUserRepositoryRole
                    {
                        UserId = owner.Id,
                        AllowRead = true,
                        AllowWrite = true,
                        IsOwner = true
                    });
                    dbContext.Repositories.Add(repository);
                    await dbContext.SaveChangesAsync();
                    var utcNow = DateTime.UtcNow;
                    var namespaceAlias = new GitCandyNamespaceAlias
                    {
                        NamespaceId = namespaceId.Value,
                        Slug = "m7-previous",
                        CreatedAtUtc = utcNow,
                        ExpiresAtUtc = utcNow.AddDays(365)
                    };
                    var repositoryAlias = new GitCandyRepositoryAlias
                    {
                        NamespaceId = namespaceId.Value,
                        RepositoryId = repository.Id,
                        Slug = "private-old",
                        CreatedAtUtc = utcNow,
                        ExpiresAtUtc = utcNow.AddDays(365)
                    };
                    dbContext.NamespaceAliases.Add(namespaceAlias);
                    dbContext.RepositoryAliases.Add(repositoryAlias);
                    await dbContext.SaveChangesAsync();
                    dbContext.NamespaceClaims.Add(new GitCandyNamespaceClaim
                    {
                        NormalizedSlug = "M7-PREVIOUS",
                        Slug = namespaceAlias.Slug,
                        ClaimType = NameClaimType.Alias,
                        NamespaceAliasId = namespaceAlias.Id
                    });
                    dbContext.RepositoryClaims.AddRange(new GitCandyRepositoryClaim
                    {
                        NamespaceId = namespaceId.Value,
                        NormalizedSlug = "PRIVATE-DEMO",
                        Slug = repository.Name,
                        ClaimType = NameClaimType.Current,
                        RepositoryId = repository.Id
                    }, new GitCandyRepositoryClaim
                    {
                        NamespaceId = namespaceId.Value,
                        NormalizedSlug = "PRIVATE-OLD",
                        Slug = repositoryAlias.Slug,
                        ClaimType = NameClaimType.Alias,
                        RepositoryAliasId = repositoryAlias.Id
                    });
                    dbContext.LegacyRepositoryRoutes.Add(new GitCandyLegacyRepositoryRoute
                    {
                        Project = repository.Name,
                        NormalizedProject = repository.NormalizedName,
                        RepositoryId = repository.Id,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    await dbContext.SaveChangesAsync();
                }

                ResetQuartzLogging();
                await app.StartAsync();

                var normalizedKeyPath = privateKeyPath.Replace('\\', '/');
                var knownHostsPath = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
                var sshCommand = string.Join(
                    ' ',
                    "ssh",
                    $"-i \"{normalizedKeyPath}\"",
                    "-o IdentitiesOnly=yes",
                    "-o BatchMode=yes",
                    "-o StrictHostKeyChecking=no",
                    $"-o UserKnownHostsFile={knownHostsPath}",
                    "-o LogLevel=ERROR");
                var remoteUrl = $"ssh://git@127.0.0.1:{sshPort}/m7-previous/private-old.git";
                var currentRemoteUrl = $"ssh://git@127.0.0.1:{sshPort}/m7-owner/private-demo.git";

                return new SshGitFixture(
                    app,
                    tempRoot,
                    bareRepositoryPath,
                    seedWorkTree,
                    remoteUrl,
                    currentRemoteUrl,
                    sshCommand,
                    privateKeyPath,
                    sshPort);
            }
            catch
            {
                if (app is not null)
                {
                    await app.DisposeAsync();
                }

                TestDirectory.Delete(tempRoot);
                throw;
            }
        }

        public Task<ProcessResult> RunGitAsync(IReadOnlyList<string> arguments, bool useSsh = true)
        {
            return RunGitProcessAsync(
                WithProtocolVersion(arguments, useSsh),
                useSsh ? SshCommand : null);
        }

        public async Task<string> RunGitForOutputAsync(
            IReadOnlyList<string> arguments,
            bool useSsh = true)
        {
            var result = await RunGitProcessAsync(
                WithProtocolVersion(arguments, useSsh),
                useSsh ? SshCommand : null);
            return result.StandardOutput.Trim();
        }

        public async Task AssertSshCommandRejectedAsync(string command)
        {
            var knownHostsPath = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            var result = await RunProcessForResultAsync(
                OperatingSystem.IsWindows() ? "ssh.exe" : "ssh",
                [
                    "-i", PrivateKeyPath,
                    "-p", SshPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-o", "IdentitiesOnly=yes",
                    "-o", "BatchMode=yes",
                    "-o", "StrictHostKeyChecking=no",
                    "-o", $"UserKnownHostsFile={knownHostsPath}",
                    "-o", "LogLevel=ERROR",
                    "git@127.0.0.1",
                    command
                ]);
            Assert.AreNotEqual(0, result.ExitCode, "The SSH server accepted a non-Git command.");
        }

        private static IReadOnlyList<string> WithProtocolVersion(
            IReadOnlyList<string> arguments,
            bool useSsh)
        {
            return useSsh
                ? ["-c", "protocol.version=2", .. arguments]
                : arguments;
        }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
            ResetQuartzLogging();
            TestDirectory.Delete(TempRoot);
        }
    }

    private static Task<ProcessResult> RunGitProcessAsync(
        IReadOnlyList<string> arguments,
        string? sshCommand)
    {
        var environment = sshCommand is null
            ? null
            : new Dictionary<string, string>
            {
                ["GIT_SSH_COMMAND"] = sshCommand,
                ["GIT_SSH_VARIANT"] = "ssh"
            };
        return RunProcessAsync(
            OperatingSystem.IsWindows() ? "git.exe" : "git",
            arguments,
            environment);
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var result = await RunProcessForResultAsync(fileName, arguments, environment);
        Assert.AreEqual(
            0,
            result.ExitCode,
            $"{fileName} {arguments.FirstOrDefault()} failed: {result.StandardError}");
        return result;
    }

    private static async Task<ProcessResult> RunProcessForResultAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "Never";
        if (environment is not null)
        {
            foreach (var item in environment)
            {
                startInfo.Environment[item.Key] = item.Value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        Assert.IsTrue(process.Start());
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.IPv6Any, 0);
        listener.Server.DualMode = true;
        listener.ExclusiveAddressUse = true;
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
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
        LogProvider.SetCurrentLogProvider(new NoopQuartzLogProvider());
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class NoopQuartzLogProvider : ILogProvider
    {
        public Logger GetLogger(string name)
        {
            return (_, _, _, _) => false;
        }

        public IDisposable OpenNestedContext(string message)
        {
            return NullScope.Instance;
        }

        public IDisposable OpenMappedContext(string key, object value, bool destructure)
        {
            return NullScope.Instance;
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
