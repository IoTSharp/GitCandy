namespace GitCandy.Caching;

/// <summary>
/// 表示 GitCandy 迁移宿主中的应用内缓存入口。
/// </summary>
public interface IApplicationCache
{
    /// <summary>
    /// 尝试读取缓存值。
    /// </summary>
    /// <typeparam name="TValue">缓存值类型。</typeparam>
    /// <param name="key">缓存键。</param>
    /// <param name="value">读取到的缓存值。</param>
    /// <returns>缓存命中时为 <see langword="true"/>，否则为 <see langword="false"/>。</returns>
    bool TryGetValue<TValue>(string key, out TValue? value);

    /// <summary>
    /// 写入带绝对过期时间的缓存值。
    /// </summary>
    /// <typeparam name="TValue">缓存值类型。</typeparam>
    /// <param name="key">缓存键。</param>
    /// <param name="value">缓存值。</param>
    /// <param name="absoluteExpiration">绝对过期时间。</param>
    void Set<TValue>(string key, TValue value, DateTimeOffset absoluteExpiration);

    /// <summary>
    /// 写入带相对过期时间的缓存值。
    /// </summary>
    /// <typeparam name="TValue">缓存值类型。</typeparam>
    /// <param name="key">缓存键。</param>
    /// <param name="value">缓存值。</param>
    /// <param name="absoluteExpirationRelativeToNow">相对当前时间的过期时间。</param>
    void Set<TValue>(string key, TValue value, TimeSpan absoluteExpirationRelativeToNow);

    /// <summary>
    /// 移除缓存值。
    /// </summary>
    /// <param name="key">缓存键。</param>
    void Remove(string key);
}
