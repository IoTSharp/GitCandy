using GitCandy.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace GitCandy.Tests;

[TestClass]
public sealed class ApplicationCacheTests
{
    [TestMethod]
    public void TryGetValue_WithCachedValue_ReturnsValue()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var applicationCache = new MemoryApplicationCache(memoryCache);

        applicationCache.Set("token:active", "alice", DateTimeOffset.UtcNow.AddMinutes(5));

        var found = applicationCache.TryGetValue<string>("token:active", out var value);

        Assert.IsTrue(found);
        Assert.AreEqual("alice", value);
    }

    [TestMethod]
    public void TryGetValue_WithExpiredValue_ReturnsFalse()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var applicationCache = new MemoryApplicationCache(memoryCache);

        applicationCache.Set("token:expired", "alice", DateTimeOffset.UtcNow.AddTicks(-1));

        var found = applicationCache.TryGetValue<string>("token:expired", out var value);

        Assert.IsFalse(found);
        Assert.IsNull(value);
    }

    [TestMethod]
    public void Remove_WithCachedValue_EvictsValue()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var applicationCache = new MemoryApplicationCache(memoryCache);

        applicationCache.Set("token:remove", "alice", TimeSpan.FromMinutes(5));
        applicationCache.Remove("token:remove");

        var found = applicationCache.TryGetValue<string>("token:remove", out var value);

        Assert.IsFalse(found);
        Assert.IsNull(value);
    }
}
