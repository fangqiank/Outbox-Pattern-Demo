using Microsoft.EntityFrameworkCore;
using OutboxPatternDemo.Domain.Entities;

namespace OutboxPatternDemo.Infrastructure.Data
{
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<OutboxMessage> OutboxMessages { get; set; }

        override protected void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 订单配置
            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.CustomerName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Amount).HasPrecision(18, 2);
                entity.Property(e => e.Status).HasConversion<string>();
            });

            // 发件箱消息配置
            modelBuilder.Entity<OutboxMessage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.Status, e.CreatedAt });
                entity.Property(e => e.Type).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Status).HasConversion<string>();
            });
        }
    }
}

   
