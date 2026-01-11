namespace OutboxPatternDemo.API.Caching
{
    public interface ICacheService
    {
        // 获取或创建缓存项
        Task<T?> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default);

        // 设置缓存项
        Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default);

        // 删除缓存项
        Task RemoveAsync(string key, CancellationToken cancellationToken = default);

        // 批量删除模式匹配的key
        Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

        // 带版本控制的缓存
        Task<(T? Value, string? Version)> GetWithVersionAsync<T>(
            string key,
            CancellationToken cancellationToken = default);

        Task<bool> SetWithVersionAsync<T>(
            string key,
            T value,
            string version,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default);
    }
}