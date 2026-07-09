using Microsoft.Extensions.Caching.Memory;

namespace GitCandy.Caching;

/// <summary>
/// 基于 ASP.NET Core <see cref="IMemoryCache"/> 的应用内缓存实现。
/// </summary>
/// <param name="memoryCache">ASP.NET Core 内存缓存。</param>
public sealed class MemoryApplicationCache(IMemoryCache memoryCache) : IApplicationCache
{
    private readonly IMemoryCache _memoryCache = memoryCache
        ?? throw new ArgumentNullException(nameof(memoryCache));

    /// <inheritdoc />
    public bool TryGetValue<TValue>(string key, out TValue? value)
    {
        ValidateKey(key);

        return _memoryCache.TryGetValue(key, out value);
    }

    /// <inheritdoc />
    public void Set<TValue>(string key, TValue value, DateTimeOffset absoluteExpiration)
    {
        ValidateKey(key);

        _memoryCache.Set(key, value, absoluteExpiration);
    }

    /// <inheritdoc />
    public void Set<TValue>(string key, TValue value, TimeSpan absoluteExpirationRelativeToNow)
    {
        ValidateKey(key);

        if (absoluteExpirationRelativeToNow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(absoluteExpirationRelativeToNow),
                absoluteExpirationRelativeToNow,
                "Cache expiration must be greater than zero.");
        }

        _memoryCache.Set(key, value, absoluteExpirationRelativeToNow);
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        ValidateKey(key);

        _memoryCache.Remove(key);
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
    }
}
