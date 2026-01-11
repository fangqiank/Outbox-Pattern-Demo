using Microsoft.Extensions.Caching.Hybrid;

namespace OutboxPatternDemo.API.Caching
{
    public class CacheService(
        HybridCache cache,
        ILogger<CacheService> logger
        ) : ICacheService
    {
        public async Task<T?> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await cache.GetOrCreateAsync<T>(
                    key,
                    async (cancel) =>
                    {
                        logger.LogDebug("缓存未命中，执行工厂方法: {Key}", key);
                        return await factory(cancel);
                    },
                    new HybridCacheEntryOptions
                    {
                        Expiration = expiration ?? TimeSpan.FromMinutes(5)
                    },
                    tags: null,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "获取或创建缓存失败: {Key}", key);
                // 降级策略：直接执行工厂方法
                return await factory(cancellationToken);
            }
        }

        public async Task<(T? Value, string? Version)> GetWithVersionAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await cache.GetOrCreateAsync<T>(
                    key,
                    (cancel) => ValueTask.FromResult<T>(default!),
                    new HybridCacheEntryOptions
                    {
                        Expiration = TimeSpan.FromMinutes(5)
                    },
                    tags: null,
                    cancellationToken);

                return (result, null);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "获取带版本的缓存失败: {Key}", key);
                return (default, null);
            }
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                await cache.RemoveAsync(key, cancellationToken);
                logger.LogDebug("缓存删除成功: {Key}", key);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "删除缓存失败: {Key}", key);
            }
        }

        public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
        {
            logger.LogWarning("模式删除需要自定义实现: {Pattern}", pattern);
            return Task.CompletedTask;
        }

        public async Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await cache.SetAsync(
                    key,
                    value,
                    new HybridCacheEntryOptions
                    {
                        Expiration = expiration ?? TimeSpan.FromMinutes(5)
                    },
                    tags: null,
                    cancellationToken);

                logger.LogDebug("缓存设置成功: {Key}", key);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "设置缓存失败: {Key}", key);
            }
        }

        public async Task<bool> SetWithVersionAsync<T>(
            string key,
            T value,
            string version,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await cache.SetAsync(
                    key,
                    value,
                    new HybridCacheEntryOptions
                    {
                        Expiration = expiration ?? TimeSpan.FromMinutes(5)
                    },
                    tags: null,
                    cancellationToken);

                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "设置带版本的缓存失败: {Key}", key);
                return false;
            }
        }
    }
}
