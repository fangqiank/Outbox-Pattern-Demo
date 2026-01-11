using OutboxPatternDemo.Domain.Entities;

namespace OutboxPatternDemo.Domain
{
    public interface IOutboxMessageRepository
    {
        Task AddAsync(OutboxMessage message);
        Task<List<OutboxMessage>> GetPendingMessagesAsync(int batchSize = 20);
        Task UpdateAsync(OutboxMessage message);
    }
}
