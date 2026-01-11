using Microsoft.EntityFrameworkCore;
using OutboxPatternDemo.Domain;
using OutboxPatternDemo.Domain.Entities;
using OutboxPatternDemo.Infrastructure.Data;
using OutboxPatternDemo.Services;
using System.Text.Json;

namespace OutboxPatternDemo.API.Caching
{
    public class CacheAwareOutboxProcessor(
        ILogger<CacheAwareOutboxProcessor> logger,
        IServiceScopeFactory scopeFactory
        ) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("缓存感知的发件箱处理器启动");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 检查缓存中的统计信息，决定是否处理
                    var stats = await GetOutboxStatsAsync();
                    if (stats.PendingCount > 0)
                    {
                        await ProcessWithCacheAwarenessAsync(stoppingToken);
                    }

                    // 更新缓存统计
                    await UpdateOutboxStatsCacheAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "处理发件箱消息时出错");
                }

                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }

        private async Task UpdateOutboxStatsCacheAsync()
        {
            using var scope = scopeFactory.CreateScope();
            var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
            await cache.RemoveAsync(CacheKeys.OutboxStats);
        }

        private async Task<OutboxStats> GetOutboxStatsAsync()
        {
            using var scope = scopeFactory.CreateScope();
            var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var result = await cache.GetOrCreateAsync(
            CacheKeys.OutboxStats,
            async (cancellationToken) =>
            {
                var pendingCount = await context.OutboxMessages
                    .CountAsync(m => m.Status == OutboxMessageStatus.Pending, cancellationToken: cancellationToken);

                var failedCount = await context.OutboxMessages
                    .CountAsync(m => m.Status == OutboxMessageStatus.Failed, cancellationToken: cancellationToken);

                return new OutboxStats(pendingCount, failedCount);
            },
            TimeSpan.FromSeconds(30));

            return result ?? new OutboxStats(0, 0);
        }

        private async Task ProcessWithCacheAwarenessAsync(CancellationToken stoppingToken)
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IOutboxMessageRepository>();
            var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
            var messagePublisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();

            // 使用缓存锁防止并发处理（简化版）
            var lockKey = "outbox:processing:lock";
            var lockAcquired = await TryAcquireLockAsync(cache, lockKey, TimeSpan.FromSeconds(10));

            if (!lockAcquired)
            {
                logger.LogDebug("未获取处理锁，跳过本次处理");
                return;
            }

            try
            {
                var pendingMessages = await repository.GetPendingMessagesAsync(50);

                foreach (var message in pendingMessages)
                {
                    // 检查缓存是否已处理过（幂等性检查）
                    var processedCacheKey = CacheKeys.InboxProcessed(message.Id.ToString());
                    var alreadyProcessed = await cache.GetOrCreateAsync(
                        processedCacheKey,
                        async (ct) => false, // 默认未处理
                        TimeSpan.FromHours(24), stoppingToken);

                    if (alreadyProcessed)
                    {
                        logger.LogDebug("消息 {MessageId} 已处理，跳过", message.Id);
                        continue;
                    }

                    // 处理消息
                    message.MarkAsProcessing();
                    await repository.UpdateAsync(message);

                    var success = await messagePublisher.PublishAsync(
                        message.Type,
                        message.Content,
                        stoppingToken);

                    if (success)
                    {
                        message.MarkAsProcessed();
                        await repository.UpdateAsync(message);

                        // 缓存处理状态（24小时）
                        await cache.SetAsync(processedCacheKey, true, TimeSpan.FromHours(24), stoppingToken);

                        // 根据消息类型处理缓存
                        await HandleMessageCacheUpdatesAsync(cache, message);
                    }
                    else
                    {
                        message.MarkAsFailed("发布失败");
                        await repository.UpdateAsync(message);
                    }
                }
            }
            finally
            {
                await ReleaseLockAsync(cache, lockKey);
            }
        }

        private async Task ReleaseLockAsync(ICacheService cache, string key)
        {
            await cache.RemoveAsync(key);
        }

        private async Task HandleMessageCacheUpdatesAsync(ICacheService cache, OutboxMessage message)
        {
            try
            {
                var messageData = JsonSerializer.Deserialize<JsonElement>(message.Content);

                switch (message.Type)
                {
                    case "OrderCreated":
                        if (messageData.TryGetProperty("OrderId", out var orderIdElement) &&
                            Guid.TryParse(orderIdElement.GetString(), out var orderId))
                        {
                            // 使订单相关缓存失效，下次读取时重新加载
                            await cache.RemoveAsync(CacheKeys.OrderById(orderId));
                        }
                        break;

                    case "OrderStatusUpdated":
                        // 处理订单状态更新时的缓存
                        break;

                        // 其他消息类型的缓存处理...
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "处理消息缓存更新失败: {MessageId}", message.Id);
            }
        }

        private async Task<bool> TryAcquireLockAsync(ICacheService cache, string key, TimeSpan expiry)
        {
            try
            {
                var lockValue = Guid.NewGuid().ToString();
                await cache.SetAsync(key, lockValue, expiry);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public record OutboxStats(int PendingCount, int FailedCount);
}
