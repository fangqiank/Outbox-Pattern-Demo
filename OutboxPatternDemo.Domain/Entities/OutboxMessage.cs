namespace OutboxPatternDemo.Domain.Entities
{
    public class OutboxMessage
    {
        public Guid Id { get; private set; }
        public string Type { get; private set; } = string.Empty; // 消息类型：OrderCreated, OrderProcessed等
        public string Content { get; private set; } = string.Empty; // JSON序列化的消息内容
        public DateTime CreatedAt { get; private set; }
        public DateTime? ProcessedAt { get; private set; }
        public OutboxMessageStatus Status { get; private set; }

        private OutboxMessage() { }

        public OutboxMessage(string type, object content)
        {
            Id = Guid.NewGuid();
            Type = type;
            Content = System.Text.Json.JsonSerializer.Serialize(content);
            CreatedAt = DateTime.UtcNow;
            Status = OutboxMessageStatus.Pending;
        }

        public void MarkAsProcessing()
        {
            Status = OutboxMessageStatus.Processing;
        }

        public void MarkAsProcessed()
        {
            Status = OutboxMessageStatus.Processed;
            ProcessedAt = DateTime.UtcNow;
        }

        public void MarkAsFailed(string error)
        {
            Status = OutboxMessageStatus.Failed;
            Content = $"{Content}\nError: {error}";
        }
    }

    public enum OutboxMessageStatus
    {
        Pending,
        Processing,
        Processed,
        Failed
    }
}
