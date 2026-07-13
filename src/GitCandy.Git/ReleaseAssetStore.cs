using System.Buffers;
using System.Security.Cryptography;
using GitCandy.Configuration;
using GitCandy.Releases;

namespace GitCandy.Git;

/// <summary>在配置 cache root 下以服务器生成 ID 原子保存 Release 附件。</summary>
public sealed class ReleaseAssetStore(IGitCandyApplicationPaths applicationPaths) : IReleaseAssetStore
{
    private const string RootDirectory = "release-assets";

    public async Task<StoredReleaseAsset?> StoreAsync(
        long repositoryId,
        long releaseId,
        string assetId,
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ValidateKey(repositoryId, releaseId, assetId);
        if (maxBytes <= 0) return null;
        var target = ResolvePath(repositoryId, releaseId, assetId);
        var directory = Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException("Release asset path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".tmp-{Guid.NewGuid():N}");
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        long length = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: buffer.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    var read = await content.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (read == 0) break;
                    length = checked(length + read);
                    if (length > maxBytes) return null;
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
            }
            File.Move(temporary, target, overwrite: false);
            return new StoredReleaseAsset(length, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public Task<Stream?> OpenReadAsync(
        long repositoryId,
        long releaseId,
        string assetId,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(repositoryId, releaseId, assetId);
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(repositoryId, releaseId, assetId);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(
        long repositoryId,
        long releaseId,
        string assetId,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(repositoryId, releaseId, assetId);
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(repositoryId, releaseId, assetId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<int> DeleteOrphansAsync(
        IReadOnlySet<string> activeAssetIds,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeAssetIds);
        var root = applicationPaths.ResolvePathWithinCacheRoot(RootDirectory);
        if (!Directory.Exists(root)) return Task.FromResult(0);
        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(path);
            if (file.LastWriteTimeUtc > olderThan.UtcDateTime) continue;
            var assetId = file.Name;
            if (!assetId.StartsWith(".tmp-", StringComparison.Ordinal)
                && activeAssetIds.Contains(assetId)) continue;
            file.Delete();
            deleted++;
        }
        return Task.FromResult(deleted);
    }

    private string ResolvePath(long repositoryId, long releaseId, string assetId) =>
        applicationPaths.ResolvePathWithinCacheRoot(Path.Combine(
            RootDirectory,
            repositoryId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            releaseId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            assetId));

    private static void ValidateKey(long repositoryId, long releaseId, string assetId)
    {
        if (repositoryId <= 0) throw new ArgumentOutOfRangeException(nameof(repositoryId));
        if (releaseId <= 0) throw new ArgumentOutOfRangeException(nameof(releaseId));
        if (assetId.Length != 32 || !assetId.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Release asset ID must be 32 hexadecimal characters.", nameof(assetId));
        }
    }
}
