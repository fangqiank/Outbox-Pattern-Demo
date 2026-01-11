using Microsoft.EntityFrameworkCore;
using OutboxPatternDemo.Domain.Entities;
using OutboxPatternDemo.Infrastructure.Data;
using OutboxPatternDemo.Services;

namespace OutboxPatternDemo.API.Caching
{
    public class CacheAwareOrderService(
        AppDbContext context,
        ICacheService cache,
        ILogger<CacheAwareOrderService> logger
        ) : IOrderService
    {
        public async Task<Guid> CreateOrderAsync(string customerName, decimal amount)
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // 1. 创建订单
                var order = new Order(customerName, amount);
                await context.Orders.AddAsync(order);

                // 2. 创建Outbox消息
                var outboxMessage = new OutboxMessage(
                    "OrderCreated",
                    new
                    {
                        OrderId = order.Id,
                        CustomerName = order.CustomerName,
                        Amount = order.Amount,
                        CreatedAt = order.CreatedAt
                    });

                await context.OutboxMessages.AddAsync(outboxMessage);

                // 3. 提交事务
                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                // 4. 缓存订单（异步，不影响主事务）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await cache.SetAsync(
                            CacheKeys.OrderById(order.Id),
                            order,
                            TimeSpan.FromMinutes(10));

                        // 使客户订单列表缓存失效
                        await cache.RemoveAsync(CacheKeys.OrdersByCustomer(customerName));

                        logger.LogDebug("订单 {OrderId} 已缓存", order.Id);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "缓存订单失败，可降级处理");
                    }
                });

                logger.LogInformation("订单创建成功: {OrderId}", order.Id);
                return order.Id;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                logger.LogError(ex, "创建订单失败");
                throw;
            }
        }

        public async Task<Order?> GetOrderAsync(Guid orderId)
        {
            // 使用HybridCache的GetOrCreateAsync模式
            return await cache.GetOrCreateAsync(
                CacheKeys.OrderById(orderId),
                async (cancellationToken) =>
                {
                    logger.LogInformation("缓存未命中，从数据库加载订单: {OrderId}", orderId);
                    return await context.Orders.FindAsync(new object[] { orderId }, cancellationToken);
                },
                TimeSpan.FromMinutes(10));
        }

        public async Task<List<Order>> GetOrdersByCustomerAsync(string customerName)
        {
            var cacheKey = CacheKeys.OrdersByCustomer(customerName);

            var result = await cache.GetOrCreateAsync(
                cacheKey,
                async (cancellationToken) =>
                {
                    logger.LogInformation("缓存未命中，从数据库查询客户订单: {Customer}", customerName);

                    return await context.Orders
                        .Where(o => o.CustomerName == customerName)
                        .OrderByDescending(o => o.CreatedAt)
                        .Take(50)
                        .ToListAsync(cancellationToken);
                },
                TimeSpan.FromMinutes(5));

            return result ?? [];
        }

        public async Task UpdateOrderStatusAsync(Guid orderId, OrderStatus newStatus)
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                var order = await context.Orders.FindAsync(orderId);
                if (order == null)
                    throw new ArgumentException($"订单 {orderId} 不存在");

                var oldStatus = order.Status;
                order.SetStatus(newStatus);

                // 创建 Outbox 消息
                var outboxMessage = new OutboxMessage(
                    "OrderStatusUpdated",
                    new
                    {
                        OrderId = order.Id,
                        CustomerName = order.CustomerName,
                        Amount = order.Amount,
                        OldStatus = oldStatus.ToString(),
                        NewStatus = newStatus.ToString(),
                        UpdatedAt = DateTime.UtcNow
                    });

                await context.OutboxMessages.AddAsync(outboxMessage);
                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                // 更新缓存（异步，不影响主事务）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await cache.SetAsync(
                            CacheKeys.OrderById(orderId),
                            order,
                            TimeSpan.FromMinutes(10));

                        await cache.RemoveAsync(CacheKeys.OrdersByCustomer(order.CustomerName));
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "更新缓存失败");
                    }
                });

                logger.LogInformation(
                    "订单状态更新: {OrderId} {OldStatus} -> {NewStatus}",
                    orderId, oldStatus, newStatus);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                logger.LogError(ex, "更新订单状态失败");
                throw;
            }
        }

        public async Task DeleteOrderAsync(Guid orderId)
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                var order = await context.Orders.FindAsync(orderId);
                if (order == null)
                    throw new ArgumentException($"订单 {orderId} 不存在");

                // 创建 Outbox 消息（在删除前记录）
                var outboxMessage = new OutboxMessage(
                    "OrderDeleted",
                    new
                    {
                        OrderId = order.Id,
                        CustomerName = order.CustomerName,
                        Amount = order.Amount,
                        Status = order.Status.ToString(),
                        DeletedAt = DateTime.UtcNow
                    });

                await context.OutboxMessages.AddAsync(outboxMessage);

                // 删除订单
                context.Orders.Remove(order);

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                // 清除缓存（异步，不影响主事务）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await cache.RemoveAsync(CacheKeys.OrderById(orderId));
                        await cache.RemoveAsync(CacheKeys.OrdersByCustomer(order.CustomerName));
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "清除缓存失败");
                    }
                });

                logger.LogInformation("订单已删除: {OrderId}", orderId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                logger.LogError(ex, "删除订单失败");
                throw;
            }
        }
    }
}
