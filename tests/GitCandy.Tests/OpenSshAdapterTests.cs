using System.Text;
using GitCandy.Application;
using GitCandy.Configuration;
using GitCandy.Git;
using GitCandy.Ssh;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GitCandy.Tests;

[TestClass]
public sealed class OpenSshAdapterTests
{
    private const string Fingerprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string PublicKey = "AQIDBA==";

    [TestMethod]
    public async Task WriteAuthorizedKeyAsync_WithRegisteredKey_EmitsRestrictedForcedCommand()
    {
        var accessService = new FakeSshAccessService(CreateKey());
        var adapter = CreateAdapter(accessService, new RecordingTransportBackend());
        using var output = new StringWriter();

        var exitCode = await adapter.WriteAuthorizedKeyAsync($"SHA256:{Fingerprint}", output);

        Assert.AreEqual(0, exitCode);
        var line = output.ToString();
        StringAssert.StartsWith(line, "restrict,command=\"");
        StringAssert.Contains(line, $"--openssh-forced-command SHA256:{Fingerprint}");
        StringAssert.Contains(line, $"\" ssh-ed25519 {PublicKey}");
        Assert.IsFalse(line.Contains('\r') && line.IndexOf('\r') < line.Length - 2);
    }

    [TestMethod]
    public async Task ExecuteForcedCommandAsync_WithUploadPack_UsesReadAuthorizationAndSharedBackend()
    {
        var accessService = new FakeSshAccessService(CreateKey()) { CanAccess = true };
        var backend = new RecordingTransportBackend();
        var adapter = CreateAdapter(accessService, backend);
        await using var input = new MemoryStream(Encoding.ASCII.GetBytes("request"));
        await using var output = new MemoryStream();
        using var error = new StringWriter();

        var exitCode = await adapter.ExecuteForcedCommandAsync(
            $"SHA256:{Fingerprint}",
            "git-upload-pack '/git/demo.git'",
            "version=2",
            input,
            output,
            error);

        Assert.AreEqual(0, exitCode);
        Assert.IsFalse(accessService.LastRequiresWrite);
        Assert.IsTrue(accessService.RecordedUsage);
        Assert.IsNotNull(backend.Request);
        Assert.AreEqual(GitTransportService.UploadPack, backend.Request.Service);
        Assert.AreEqual("version=2", backend.Request.ProtocolVersion);
        Assert.AreEqual("request", Encoding.ASCII.GetString(output.ToArray()));
        StringAssert.Contains(error.ToString(), "Repository address changed");
    }

    [TestMethod]
    public async Task ExecuteForcedCommandAsync_WithReceivePack_RequiresWriteAuthorization()
    {
        var accessService = new FakeSshAccessService(CreateKey()) { CanAccess = false };
        var backend = new RecordingTransportBackend();
        var adapter = CreateAdapter(accessService, backend);
        using var error = new StringWriter();

        var exitCode = await adapter.ExecuteForcedCommandAsync(
            Fingerprint,
            "git-receive-pack '/git/demo.git'",
            gitProtocol: null,
            Stream.Null,
            Stream.Null,
            error);

        Assert.AreEqual(1, exitCode);
        Assert.IsTrue(accessService.LastRequiresWrite);
        Assert.IsNull(backend.Request);
        StringAssert.Contains(error.ToString(), "denied");
    }

    [TestMethod]
    public async Task ExecuteForcedCommandAsync_WithShellOrUnsupportedProtocol_RejectsBeforeTransport()
    {
        var accessService = new FakeSshAccessService(CreateKey()) { CanAccess = true };
        var backend = new RecordingTransportBackend();
        var adapter = CreateAdapter(accessService, backend);
        using var shellError = new StringWriter();
        using var protocolError = new StringWriter();

        var shellExitCode = await adapter.ExecuteForcedCommandAsync(
            Fingerprint,
            "whoami",
            gitProtocol: null,
            Stream.Null,
            Stream.Null,
            shellError);
        var protocolExitCode = await adapter.ExecuteForcedCommandAsync(
            Fingerprint,
            "git-upload-pack '/git/demo.git'",
            "version=1",
            Stream.Null,
            Stream.Null,
            protocolError);

        Assert.AreEqual(1, shellExitCode);
        Assert.AreEqual(1, protocolExitCode);
        Assert.IsNull(backend.Request);
        Assert.AreEqual(0, accessService.FindCount);
    }

    [TestMethod]
    public async Task Adapter_WithDisabledConfiguration_ProducesNoAuthorizedKeyOrTransport()
    {
        var accessService = new FakeSshAccessService(CreateKey()) { CanAccess = true };
        var backend = new RecordingTransportBackend();
        var adapter = CreateAdapter(accessService, backend, enabled: false);
        using var keyOutput = new StringWriter();
        using var error = new StringWriter();

        var keyExitCode = await adapter.WriteAuthorizedKeyAsync(Fingerprint, keyOutput);
        var commandExitCode = await adapter.ExecuteForcedCommandAsync(
            Fingerprint,
            "git-upload-pack '/git/demo.git'",
            gitProtocol: null,
            Stream.Null,
            Stream.Null,
            error);

        Assert.AreEqual(1, keyExitCode);
        Assert.AreEqual(1, commandExitCode);
        Assert.AreEqual(string.Empty, keyOutput.ToString());
        Assert.IsNull(backend.Request);
    }

    private static OpenSshAdapter CreateAdapter(
        FakeSshAccessService accessService,
        RecordingTransportBackend backend,
        bool enabled = true)
    {
        var executablePath = Path.GetFullPath(Path.Combine("tools", "GitCandy"));
        return new OpenSshAdapter(
            accessService,
            new FakeGitServiceFactory(),
            backend,
            Options.Create(new GitCandyOpenSshOptions
            {
                Enabled = enabled,
                ExecutablePath = executablePath
            }),
            NullLogger<OpenSshAdapter>.Instance);
    }

    private static SshAuthorizedKey CreateKey()
    {
        return new SshAuthorizedKey(
            new SshPrincipal("user-id", "git-owner", IsAdministrator: false),
            "ssh-ed25519",
            PublicKey,
            Fingerprint);
    }

    private sealed class FakeSshAccessService(SshAuthorizedKey? key) : ISshAccessService
    {
        private readonly SshAuthorizedKey? _key = key;

        public bool CanAccess { get; init; }

        public int FindCount { get; private set; }

        public bool LastRequiresWrite { get; private set; }

        public bool RecordedUsage { get; private set; }

        public Task<SshPrincipal?> AuthenticateAsync(
            string keyType,
            byte[] publicKey,
            bool recordUsage = true,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_key?.Principal);
        }

        public Task<SshAuthorizedKey?> FindAuthorizedKeyAsync(
            string fingerprint,
            bool recordUsage = false,
            CancellationToken cancellationToken = default)
        {
            FindCount++;
            RecordedUsage = recordUsage;
            return Task.FromResult(_key);
        }

        public Task<bool> CanAccessRepositoryAsync(
            SshPrincipal principal,
            long repositoryId,
            bool requiresWrite,
            CancellationToken cancellationToken = default)
        {
            LastRequiresWrite = requiresWrite;
            return Task.FromResult(CanAccess);
        }

        public Task<RepositoryAddressResolution?> ResolveRepositoryAsync(
            string? namespaceSlug,
            string repositorySlug,
            bool legacy,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<RepositoryAddressResolution?>(new RepositoryAddressResolution(
                1,
                1,
                namespaceSlug ?? "legacy",
                repositorySlug,
                repositorySlug,
                IsPrivate: true,
                UsedNamespaceAlias: false,
                UsedRepositoryAlias: false,
                UsedLegacyRoute: legacy));
        }
    }

    private sealed class FakeGitServiceFactory : IGitServiceFactory
    {
        public GitRepositoryContext Create(string repositoryName)
        {
            return new GitRepositoryContext(repositoryName, Path.GetFullPath(repositoryName));
        }
    }

    private sealed class RecordingTransportBackend : IGitTransportBackend
    {
        public GitTransportRequest? Request { get; private set; }

        public void EnsureRepositoryExists(GitRepositoryContext repository)
        {
        }

        public async Task ExecuteAsync(
            GitTransportRequest request,
            Stream input,
            Stream output,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            await input.CopyToAsync(output, cancellationToken);
        }
    }
}
