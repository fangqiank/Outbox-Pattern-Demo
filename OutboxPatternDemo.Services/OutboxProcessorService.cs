using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OutboxPatternDemo.Domain;

namespace OutboxPatternDemo.Services
{
    public class OutboxProcessorService(
        ILogger<OutboxProcessorService> logger,
        IServiceScopeFactory scopeFactory,
        IMessagePublisher messagePublisher
        ) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("发件箱处理器服务启动");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingMessagesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "处理发件箱消息时出错");
                }

                // 每5秒检查一次
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        private async Task ProcessPendingMessagesAsync(CancellationToken stoppingToken)
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IOutboxMessageRepository>();

            var pendingMessages = await repository.GetPendingMessagesAsync();

            if (!pendingMessages.Any())
                return;

            logger.LogInformation("找到 {Count} 条待处理消息", pendingMessages.Count);

            foreach (var message in pendingMessages)
            {
                try
                {
                    // 标记为处理中（防止并发处理）
                    message.MarkAsProcessing();
                    await repository.UpdateAsync(message);

                    // 发布消息到消息队列
                    var success = await messagePublisher.PublishAsync(
                        message.Type,
                        message.Content,
                        stoppingToken);

                    if (success)
                    {
                        // 标记为已处理
                        message.MarkAsProcessed();
                        await repository.UpdateAsync(message);

                        logger.LogInformation(
                            "消息 {MessageId} ({Type}) 发布成功",
                            message.Id, message.Type);
                    }
                    else
                    {
                        message.MarkAsFailed("发布到消息队列失败");
                        await repository.UpdateAsync(message);
                    }
                }
                catch (Exception ex)
                {
                        logger.LogError(ex,
                            "处理消息 {MessageId} 时出错", message.Id);

                        message.MarkAsFailed(ex.Message);
                        await repository.UpdateAsync(message);
                }
            }
        }
    }
}
