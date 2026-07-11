using System.Collections.Concurrent;
using System.Security.Cryptography;
using GitCandy.Configuration;
using Microsoft.Extensions.Options;

namespace GitCandy.Git;

/// <summary>
/// 基于 cache 根目录的 Git LFS 文件对象存储。
/// </summary>
public sealed class GitLfsObjectStore(
    IGitCandyApplicationPaths applicationPaths,
    IOptions<GitLfsOptions> options) : IGitLfsObjectStore
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _repositoryLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IGitCandyApplicationPaths _applicationPaths = applicationPaths;
    private readonly GitLfsOptions _options = options.Value;

    /// <inheritdoc />
    public GitLfsObjectInfo? GetInfo(string repositoryName, string oid)
    {
        var normalizedOid = NormalizeOid(oid);
        var path = ResolveObjectPath(repositoryName, normalizedOid);
        return File.Exists(path)
            ? new GitLfsObjectInfo(normalizedOid, new FileInfo(path).Length)
            : null;
    }

    /// <inheritdoc />
    public bool CanStore(string repositoryName, long size)
    {
        if (size < 0 || size > _options.MaxObjectBytes)
        {
            return false;
        }

        if (_options.RepositoryQuotaBytes == 0)
        {
            return true;
        }

        var repositoryPath = ResolveRepositoryLfsPath(repositoryName);
        var used = Directory.Exists(repositoryPath)
            ? Directory.EnumerateFiles(repositoryPath, "*", SearchOption.AllDirectories)
                .Where(static path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}.tmp{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                .Sum(static path => new FileInfo(path).Length)
            : 0;
        return used <= _options.RepositoryQuotaBytes - size;
    }

    /// <inheritdoc />
    public async Task<GitLfsObjectInfo> WriteAsync(
        string repositoryName,
        string oid,
        long? expectedSize,
        Stream input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var normalizedOid = NormalizeOid(oid);
        if (expectedSize is < 0 || expectedSize > _options.MaxObjectBytes)
        {
            throw new InvalidDataException("The LFS object exceeds the configured size limit.");
        }

        var repositoryLock = _repositoryLocks.GetOrAdd(
            repositoryName,
            static _ => new SemaphoreSlim(1, 1));
        await repositoryLock.WaitAsync(cancellationToken);
        try
        {
            var existing = GetInfo(repositoryName, normalizedOid);
            if (existing is not null)
            {
                if (expectedSize is null || existing.Size == expectedSize)
                {
                    return existing;
                }

                throw new InvalidDataException("The existing LFS object has a different size.");
            }

            if (expectedSize is long size && !CanStore(repositoryName, size))
            {
                throw new InvalidDataException("The repository LFS quota would be exceeded.");
            }

            var objectPath = ResolveObjectPath(repositoryName, normalizedOid);
            var tempRoot = Path.Combine(ResolveRepositoryLfsPath(repositoryName), ".tmp");
            Directory.CreateDirectory(tempRoot);
            var tempPath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}.upload");
            try
            {
                long written = 0;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var destination = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    _options.StreamBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[_options.StreamBufferSize];
                    while (true)
                    {
                        var read = await input.ReadAsync(buffer, cancellationToken);
                        if (read == 0)
                        {
                            break;
                        }

                        written = checked(written + read);
                        if (written > _options.MaxObjectBytes)
                        {
                            throw new InvalidDataException("The LFS object exceeds the configured size limit.");
                        }

                        hash.AppendData(buffer, 0, read);
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }

                    await destination.FlushAsync(cancellationToken);
                }

                if (expectedSize is long requiredSize && written != requiredSize)
                {
                    throw new InvalidDataException("The uploaded LFS object size does not match the batch request.");
                }

                var actualOid = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!string.Equals(actualOid, normalizedOid, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The uploaded LFS object SHA-256 does not match its OID.");
                }

                if (!CanStore(repositoryName, written))
                {
                    throw new InvalidDataException("The repository LFS quota would be exceeded.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(objectPath)
                    ?? throw new InvalidOperationException("The LFS object path has no parent directory."));
                File.Move(tempPath, objectPath);
                return new GitLfsObjectInfo(normalizedOid, written);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        finally
        {
            repositoryLock.Release();
        }
    }

    /// <inheritdoc />
    public Stream OpenRead(string repositoryName, string oid)
    {
        var path = ResolveObjectPath(repositoryName, NormalizeOid(oid));
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            _options.StreamBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    /// <inheritdoc />
    public async Task DeleteRepositoryAsync(
        string repositoryName,
        CancellationToken cancellationToken = default)
    {
        var repositoryLock = _repositoryLocks.GetOrAdd(
            repositoryName,
            static _ => new SemaphoreSlim(1, 1));
        await repositoryLock.WaitAsync(cancellationToken);
        try
        {
            var path = ResolveRepositoryLfsPath(repositoryName);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        finally
        {
            repositoryLock.Release();
        }
    }

    private string ResolveObjectPath(string repositoryName, string oid)
    {
        return _applicationPaths.ResolvePathWithinCacheRoot(
            Path.Combine("lfs", NormalizeRepositoryName(repositoryName), oid[..2], oid[2..4], oid));
    }

    private string ResolveRepositoryLfsPath(string repositoryName)
    {
        return _applicationPaths.ResolvePathWithinCacheRoot(
            Path.Combine("lfs", NormalizeRepositoryName(repositoryName)));
    }

    private static string NormalizeRepositoryName(string repositoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        var value = repositoryName.Trim();
        if (value is "." or ".." || value.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException("Repository name must be one path segment.", nameof(repositoryName));
        }

        return value.ToUpperInvariant();
    }

    private static string NormalizeOid(string oid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oid);
        var value = oid.Trim().ToLowerInvariant();
        if (value.Length != 64 || value.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("A Git LFS OID must be a 64-character SHA-256 value.", nameof(oid));
        }

        return value;
    }
}
