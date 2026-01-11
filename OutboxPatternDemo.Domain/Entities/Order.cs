namespace OutboxPatternDemo.Domain.Entities
{
    public class Order
    {
        public Guid Id { get; private set; }
        public string CustomerName { get; private set; } = string.Empty;
        public decimal Amount { get; private set; }
        public OrderStatus Status { get; private set; }
        public DateTime CreatedAt { get; private set; }

        private Order() { } // EF Core 需要

        public Order(string customerName, decimal amount)
        {
            Id = Guid.NewGuid();
            CustomerName = customerName;
            Amount = amount;
            Status = OrderStatus.Pending;
            CreatedAt = DateTime.UtcNow;
        }

        public void MarkAsProcessed()
        {
            Status = OrderStatus.Processed;
        }

        public void SetStatus(OrderStatus newStatus)
        {
            Status = newStatus;
        }
    }

    public enum OrderStatus
    {
        Pending,
        Processed,
        Failed
    }
}
