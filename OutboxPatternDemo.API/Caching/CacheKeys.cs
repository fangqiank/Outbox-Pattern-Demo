namespace OutboxPatternDemo.API.Caching
{
    public static class CacheKeys
    {
        public static string OrderById(Guid orderId) => $"order:{orderId}";
        public static string OrdersByCustomer(string customerName) => $"orders:customer:{customerName}";
        public static string OutboxStats => "outbox:stats";
        public static string InboxProcessed(string messageId) => $"inbox:processed:{messageId}";
    }
}