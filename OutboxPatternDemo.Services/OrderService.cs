using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OutboxPatternDemo.Domain.Entities;
using OutboxPatternDemo.Infrastructure.Data;

namespace OutboxPatternDemo.Services
{
    public class OrderService(
        AppDbContext context,
        ILogger<OrderService> logger
        ) : IOrderService
    {
        public async Task<Guid> CreateOrderAsync(string customerName, decimal amount)
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // 1. 创建业务实体
                var order = new Order(customerName, amount);
                await context.Orders.AddAsync(order);

                // 2. 创建发件箱消息（在同一事务中）
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

                // 3. 提交事务（保证订单和消息的原子性保存）
                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                logger.LogInformation(
                    "订单 {OrderId} 创建成功，发件箱消息 {MessageId} 已保存",
                    order.Id, outboxMessage.Id);

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
            return await context.Orders.FindAsync(orderId);
        }

        public async Task<List<Order>> GetOrdersByCustomerAsync(string customerName)
        {
            return await context.Orders
                .Where(o => o.CustomerName == customerName)
                .OrderByDescending(o => o.CreatedAt)
                .Take(50)
                .ToListAsync();
        }

        public async Task UpdateOrderStatusAsync(Guid orderId, OrderStatus newStatus)
        {
            var order = await context.Orders.FindAsync(orderId);
            if (order == null)
                throw new ArgumentException($"订单 {orderId} 不存在");

            order.SetStatus(newStatus);
            await context.SaveChangesAsync();

            logger.LogInformation(
                "订单状态更新: {OrderId} -> {NewStatus}",
                orderId, newStatus);
        }

        public async Task DeleteOrderAsync(Guid orderId)
        {
            var order = await context.Orders.FindAsync(orderId);
            if (order == null)
                throw new ArgumentException($"订单 {orderId} 不存在");

            context.Orders.Remove(order);
            await context.SaveChangesAsync();

            logger.LogInformation("订单已删除: {OrderId}", orderId);
        }
    }
}
