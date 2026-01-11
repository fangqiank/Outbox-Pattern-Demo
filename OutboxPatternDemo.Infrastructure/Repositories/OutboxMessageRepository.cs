using Microsoft.EntityFrameworkCore;
using OutboxPatternDemo.Domain;
using OutboxPatternDemo.Domain.Entities;
using OutboxPatternDemo.Infrastructure.Data;

namespace OutboxPatternDemo.Infrastructure.Repositories
{
    public class OutboxMessageRepository(
        AppDbContext db
        ):IOutboxMessageRepository
    {
        public async Task AddAsync(OutboxMessage message)
        {
            await db.OutboxMessages.AddAsync(message);
        }

        public async Task<List<OutboxMessage>> GetPendingMessagesAsync(int batchSize = 20)
        {
            return await db.OutboxMessages
                .Where(m => m.Status == OutboxMessageStatus.Pending)
                .OrderBy(m => m.CreatedAt)
                .Take(batchSize)
                .ToListAsync();
        }

        public async Task UpdateAsync(OutboxMessage message)
        {
            db.OutboxMessages.Update(message);
            await db.SaveChangesAsync();
        }
    }
}
