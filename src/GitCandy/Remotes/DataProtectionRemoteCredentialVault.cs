using System.Text;
using System.Text.Json;
using GitCandy.Configuration;
using GitCandy.Remotes;
using Microsoft.AspNetCore.DataProtection;

namespace GitCandy.Web.Remotes;

internal sealed class DataProtectionRemoteCredentialVault : IRemoteCredentialVault, IDisposable
{
    private const string ReferenceScheme = "dp-file:";
    private const int MaximumCredentialFileBytes = 256 * 1024;
    private const int MaximumSecretCharacters = 16 * 1024;
    private const int MaximumScopeCharacters = 2000;
    private readonly IDataProtector _protector;
    private readonly string _vaultPath;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DataProtectionRemoteCredentialVault(
        IDataProtectionProvider dataProtectionProvider,
        IGitCandyApplicationPaths paths,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _protector = dataProtectionProvider.CreateProtector(
            "GitCandy.Remotes.CredentialVault",
            "v1");
        _vaultPath = ResolvePathWithinRoot(
            paths.DataProtectionKeysPath,
            Path.Combine(paths.DataProtectionKeysPath, "remote-credentials"));
        _timeProvider = timeProvider;
    }

    public async Task<RemoteCredentialMetadata> StoreAsync(
        RemoteConnectionOwner owner,
        RemoteCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(credential);
        ValidateCredential(credential);

        var id = Guid.NewGuid();
        var now = _timeProvider.GetUtcNow();
        var payload = new CredentialPayload(
            owner.Kind,
            owner.StableId,
            credential.AuthenticationKind,
            credential.Secret.Value,
            credential.GrantedScopes.Order(StringComparer.Ordinal).ToArray(),
            now,
            credential.ExpiresAt,
            null);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_vaultPath);
            await WriteNewAsync(GetCredentialPath(id), Protect(payload), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return ToMetadata(id, payload);
    }

    public async ValueTask<RemoteCredential?> ResolveAsync(
        RemoteSecretReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!TryParseReference(reference, out var id))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var payload = await ReadAsync(GetCredentialPath(id), cancellationToken);
            if (payload is null
                || payload.RevokedAt is not null
                || payload.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                return null;
            }

            return new RemoteCredential(
                payload.AuthenticationKind,
                new RemoteSecret(payload.Secret),
                payload.GrantedScopes,
                payload.ExpiresAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RemoteCredentialMetadata?> RotateAsync(
        RemoteSecretReference reference,
        RemoteCredential replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(replacement);
        ValidateCredential(replacement);
        if (!TryParseReference(reference, out var id))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = GetCredentialPath(id);
            var current = await ReadAsync(path, cancellationToken);
            if (current is null || current.RevokedAt is not null)
            {
                return null;
            }

            var payload = current with
            {
                AuthenticationKind = replacement.AuthenticationKind,
                Secret = replacement.Secret.Value,
                GrantedScopes = replacement.GrantedScopes.Order(StringComparer.Ordinal).ToArray(),
                ExpiresAt = replacement.ExpiresAt
            };
            await ReplaceAsync(path, Protect(payload), cancellationToken);
            return ToMetadata(id, payload);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RevokeAsync(
        RemoteSecretReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!TryParseReference(reference, out var id))
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = GetCredentialPath(id);
            var current = await ReadAsync(path, cancellationToken);
            if (current is null)
            {
                return false;
            }

            if (current.RevokedAt is not null)
            {
                return true;
            }

            var revoked = current with
            {
                Secret = string.Empty,
                RevokedAt = _timeProvider.GetUtcNow()
            };
            await ReplaceAsync(path, Protect(revoked), cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private static void ValidateCredential(RemoteCredential credential)
    {
        if (credential.Secret.Value.Length > MaximumSecretCharacters
            || credential.GrantedScopes.Sum(static scope => scope.Length + 3) > MaximumScopeCharacters)
        {
            throw new ArgumentException("The remote credential exceeds the vault size limit.", nameof(credential));
        }
    }

    private string Protect(CredentialPayload payload) =>
        _protector.Protect(JsonSerializer.Serialize(payload));

    private async Task<CredentialPayload?> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length is <= 0 or > MaximumCredentialFileBytes)
        {
            return null;
        }

        try
        {
            var protectedPayload = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
            var json = _protector.Unprotect(protectedPayload);
            return JsonSerializer.Deserialize<CredentialPayload>(json);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Security.Cryptography.CryptographicException
            or JsonException)
        {
            return null;
        }
    }

    private async Task WriteNewAsync(
        string path,
        string protectedPayload,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough
            });
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(protectedPayload.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private async Task ReplaceAsync(
        string path,
        string protectedPayload,
        CancellationToken cancellationToken)
    {
        var temporaryPath = ResolvePathWithinRoot(
            _vaultPath,
            Path.Combine(_vaultPath, $".{Guid.NewGuid():N}.tmp"));
        try
        {
            await WriteNewAsync(temporaryPath, protectedPayload, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string GetCredentialPath(Guid id) => ResolvePathWithinRoot(
        _vaultPath,
        Path.Combine(_vaultPath, $"{id:N}.credential"));

    private static bool TryParseReference(RemoteSecretReference reference, out Guid id)
    {
        var value = reference.Value;
        id = default;
        return value.StartsWith(ReferenceScheme, StringComparison.Ordinal)
            && Guid.TryParseExact(value[ReferenceScheme.Length..], "N", out id);
    }

    private static RemoteCredentialMetadata ToMetadata(Guid id, CredentialPayload payload) => new(
        new RemoteSecretReference($"{ReferenceScheme}{id:N}"),
        payload.AuthenticationKind,
        new HashSet<string>(payload.GrantedScopes, StringComparer.Ordinal),
        payload.CreatedAt,
        payload.ExpiresAt,
        payload.RevokedAt);

    private static string ResolvePathWithinRoot(string rootPath, string path)
    {
        var normalizedRoot = Path.GetFullPath(rootPath);
        var normalizedPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(normalizedRoot, normalizedPath);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The remote credential path escapes the Data Protection key root.");
        }

        return normalizedPath;
    }

    private sealed record CredentialPayload(
        RemoteConnectionOwnerKind OwnerKind,
        string OwnerStableId,
        RemoteAuthenticationKind AuthenticationKind,
        string Secret,
        string[] GrantedScopes,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? RevokedAt);
}
