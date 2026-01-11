using OutboxPatternDemo.Domain.Entities;

namespace OutboxPatternDemo.Services
{
    public interface IOrderService
    {
        Task<Guid> CreateOrderAsync(string customerName, decimal amount);
        Task<Order?> GetOrderAsync(Guid orderId);
        Task<List<Order>> GetOrdersByCustomerAsync(string customerName);
        Task UpdateOrderStatusAsync(Guid orderId, OrderStatus newStatus);
        Task DeleteOrderAsync(Guid orderId);
    }
}
