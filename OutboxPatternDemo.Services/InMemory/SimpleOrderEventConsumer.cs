using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace OutboxPatternDemo.Services.InMemory;

/// <summary>
/// 简化的订单事件消费者，从 RabbitMQ 消费消息
/// 处理逻辑在内存中执行，适合演示和测试
/// </summary>
public class SimpleOrderEventConsumer(
    ILogger<SimpleOrderEventConsumer> logger,
    IServiceProvider serviceProvider,
    IOptions<RabbitMQ.RabbitMQOptions> options
) : BackgroundService
{
    private readonly RabbitMQ.RabbitMQOptions _options = options.Value;
    private const string QueueName = "order.events.simple.queue";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("简化订单事件消费者启动");

        // 获取 ConnectionProvider
        var connectionProvider = serviceProvider.GetRequiredService<RabbitMQ.ConnectionProvider>();
        var channel = await connectionProvider.CreateChannelAsync(stoppingToken);

        // 声明队列
        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // 绑定所有订单事件
        var routingKeys = new[] { "ordercreated", "orderstatusupdated", "orderdeleted" };
        foreach (var routingKey in routingKeys)
        {
            await channel.QueueBindAsync(
                queue: QueueName,
                exchange: _options.ExchangeName,
                routingKey: routingKey,
                cancellationToken: stoppingToken);
        }

        // 设置 prefetch
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (sender, args) =>
        {
            try
            {
                var message = Encoding.UTF8.GetString(args.Body.ToArray());
                var messageType = args.RoutingKey;

                logger.LogInformation("收到订单事件: {MessageType}", messageType);

                var orderData = JsonSerializer.Deserialize<JsonElement>(message);
                var orderId = orderData.GetProperty("OrderId").GetString();

                // 简化的处理逻辑
                switch (messageType)
                {
                    case "ordercreated":
                        HandleOrderCreated(orderId, orderData);
                        break;

                    case "orderstatusupdated":
                        HandleOrderStatusUpdated(orderId, orderData);
                        break;

                    case "orderdeleted":
                        HandleOrderDeleted(orderId, orderData);
                        break;
                }

                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "处理订单消息失败");
                await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private void HandleOrderCreated(string? orderId, JsonElement orderData)
    {
        var customerName = orderData.GetProperty("CustomerName").GetString();
        var amount = orderData.GetProperty("Amount").GetDecimal();

        logger.LogInformation(
            "[简化-订单创建] ID={OrderId}, Customer={Customer}, Amount={Amount}",
            orderId, customerName, amount);

        // 简化处理：仅记录日志
    }

    private void HandleOrderStatusUpdated(string? orderId, JsonElement orderData)
    {
        var oldStatus = orderData.GetProperty("OldStatus").GetString();
        var newStatus = orderData.GetProperty("NewStatus").GetString();

        logger.LogInformation(
            "[简化-状态更新] ID={OrderId}, {OldStatus} -> {NewStatus}",
            orderId, oldStatus, newStatus);
    }

    private void HandleOrderDeleted(string? orderId, JsonElement orderData)
    {
        var customerName = orderData.GetProperty("CustomerName").GetString();
        var status = orderData.GetProperty("Status").GetString();

        logger.LogInformation(
            "[简化-订单删除] ID={OrderId}, Customer={Customer}, Status={Status}",
            orderId, customerName, status);
    }
}
