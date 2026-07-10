using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Ssh;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz.Logging;

namespace GitCandy.Tests;

[TestClass]
public sealed class SshRuntimeTests
{
    [TestMethod]
    public async Task GetHostKeysAsync_WithLegacyConfiguration_ImportsAndPersistsRsaHostKey()
    {
        var tempRoot = TestDirectory.Create();
        try
        {
            using var rsa = RSA.Create(2048);
            var privateKeyXml = rsa.ToXmlString(includePrivateParameters: true);
            var legacyPath = Path.Combine(tempRoot, "legacy-config.xml");
            var migratedPath = Path.Combine(tempRoot, "migrated-host-key.xml");
            var legacyDocument = new XDocument(
                new XElement(
                    "UserConfiguration",
                    new XElement(
                        "HostKeys",
                        new XElement(
                            "HostKey",
                            new XElement("KeyType", "ssh-rsa"),
                            new XElement("KeyXml", privateKeyXml)))));
            await using (var legacyStream = new FileStream(
                legacyPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                await legacyDocument.SaveAsync(
                    legacyStream,
                    SaveOptions.None,
                    CancellationToken.None);
            }

            var builder = CreateBuilder(tempRoot, enableSsh: false, sshPort: 2022);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Application:UserConfigurationPath"] = legacyPath,
                ["GitCandy:Application:SshHostKeyPath"] = migratedPath
            });
            builder.Services.AddGitCandyWebShell(builder.Configuration);
            await using var app = builder.Build();

            var keys = await app.Services
                .GetRequiredService<ISshHostKeyProvider>()
                .GetHostKeysAsync();

            Assert.AreEqual(1, keys.Count);
            Assert.AreEqual("ssh-rsa", keys[0].KeyType);
            Assert.AreEqual(privateKeyXml, keys[0].PrivateKeyXml);
            Assert.IsTrue(File.Exists(migratedPath));
        }
        finally
        {
            TestDirectory.Delete(tempRoot);
        }
    }

    [TestMethod]
    public async Task AuthenticateAsync_WithIdentitySshKey_UsesFactoryContextAndRepositoryPermissions()
    {
        var tempRoot = TestDirectory.Create();
        try
        {
            var publicKey = RandomNumberGenerator.GetBytes(128);
            var fingerprint = Convert.ToBase64String(SHA256.HashData(publicKey)).TrimEnd('=');
            var builder = CreateBuilder(tempRoot, enableSsh: false, sshPort: 2022);
            builder.Services.AddGitCandyWebShell(builder.Configuration);
            await using var app = builder.Build();

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
                await dbContext.Database.EnsureCreatedAsync();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
                var user = new GitCandyUser
                {
                    UserName = "ssh-reader",
                    Email = "ssh-reader@example.com"
                };
                var createResult = await userManager.CreateAsync(user);
                Assert.IsTrue(createResult.Succeeded);

                dbContext.SshKeys.Add(new GitCandySshKey
                {
                    UserId = user.Id,
                    KeyType = "ssh-rsa",
                    PublicKey = Convert.ToBase64String(publicKey),
                    Fingerprint = fingerprint,
                    ImportedAtUtc = DateTime.UtcNow
                });
                var repository = new GitCandyRepository
                {
                    Name = "private-demo",
                    Description = "SSH access test",
                    IsPrivate = true,
                    CreatedAtUtc = DateTime.UtcNow
                };
                repository.UserRoles.Add(new GitCandyUserRepositoryRole
                {
                    UserId = user.Id,
                    AllowRead = true,
                    AllowWrite = false
                });
                dbContext.Repositories.Add(repository);
                await dbContext.SaveChangesAsync();
            }

            var accessService = app.Services.GetRequiredService<ISshAccessService>();
            var principal = await accessService.AuthenticateAsync("ssh-rsa", publicKey);

            Assert.IsNotNull(principal);
            Assert.AreEqual("ssh-reader", principal.UserName);
            Assert.IsTrue(await accessService.CanAccessRepositoryAsync(
                principal,
                "PRIVATE-DEMO",
                requiresWrite: false));
            Assert.IsFalse(await accessService.CanAccessRepositoryAsync(
                principal,
                "private-demo",
                requiresWrite: true));

            await using var verificationScope = app.Services.CreateAsyncScope();
            var verificationContext = verificationScope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            var storedKey = await verificationContext.SshKeys.FindAsync(1L);
            Assert.IsNotNull(storedKey?.LastUsedAtUtc);
        }
        finally
        {
            TestDirectory.Delete(tempRoot);
        }
    }

    [TestMethod]
    public async Task BuiltInSshRuntime_WithHostLifecycle_ListensAndReleasesPort()
    {
        var tempRoot = TestDirectory.Create();
        var sshPort = GetAvailablePort();
        try
        {
            var builder = CreateBuilder(tempRoot, enableSsh: true, sshPort);
            builder.Services.AddGitCandyWebShell(builder.Configuration);
            await using var app = builder.Build();

            ResetQuartzLogging();
            await app.StartAsync();
            try
            {
                using var client = new TcpClient(AddressFamily.InterNetworkV6);
                await client.ConnectAsync(IPAddress.IPv6Loopback, sshPort)
                    .WaitAsync(TimeSpan.FromSeconds(5));
                await using var stream = client.GetStream();
                using var reader = new StreamReader(
                    stream,
                    Encoding.ASCII,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);
                var banner = await reader.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5));
                StringAssert.StartsWith(banner, "SSH-2.0-FxSsh");
            }
            finally
            {
                await app.StopAsync();
            }

            Assert.IsTrue(File.Exists(Path.Combine(tempRoot, "ssh-host-key.xml")));
            using var replacement = CreateListener(sshPort);
        }
        finally
        {
            TestDirectory.Delete(tempRoot);
        }
    }

    [TestMethod]
    public async Task BuiltInSshRuntime_WithOccupiedPort_FailsHostStartup()
    {
        var tempRoot = TestDirectory.Create();
        using var occupiedListener = CreateListener(port: 0);
        var sshPort = ((IPEndPoint)occupiedListener.LocalEndpoint).Port;
        try
        {
            var builder = CreateBuilder(tempRoot, enableSsh: true, sshPort);
            builder.Services.AddGitCandyWebShell(builder.Configuration);
            await using var app = builder.Build();

            ResetQuartzLogging();
            Exception? startupException = null;
            try
            {
                await app.StartAsync();
            }
            catch (Exception exception)
            {
                startupException = exception;
            }

            Assert.IsNotNull(startupException);
            Assert.IsTrue(
                ContainsException<SocketException>(startupException),
                startupException.ToString());
        }
        finally
        {
            TestDirectory.Delete(tempRoot);
        }
    }

    private static WebApplicationBuilder CreateBuilder(
        string tempRoot,
        bool enableSsh,
        int sshPort)
    {
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
            ["GitCandy:Application:RepositoryPath"] = Path.Combine(tempRoot, "Repositories"),
            ["GitCandy:Application:CachePath"] = Path.Combine(tempRoot, "Caches"),
            ["GitCandy:Application:LogPathFormat"] = Path.Combine(tempRoot, "Logs", "{0}.log"),
            ["GitCandy:Application:SshHostKeyPath"] = Path.Combine(tempRoot, "ssh-host-key.xml"),
            ["GitCandy:Application:SshPort"] = sshPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["GitCandy:Application:EnableSsh"] = enableSsh.ToString()
        });
        return builder;
    }

    private static TcpListener CreateListener(int port)
    {
        var listener = new TcpListener(IPAddress.IPv6Any, port);
        listener.Server.DualMode = true;
        listener.ExclusiveAddressUse = true;
        listener.Start();
        return listener;
    }

    private static int GetAvailablePort()
    {
        using var listener = CreateListener(port: 0);
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static bool ContainsException<TException>(Exception exception)
        where TException : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException)
            {
                return true;
            }
        }

        return false;
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
