using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace OutboxPatternDemo.Services.RabbitMQ;

public class OrderEventConsumer(
    ILogger<OrderEventConsumer> logger,
    ConnectionProvider connectionProvider,
    IOptions<RabbitMQOptions> options
) : BackgroundService
{
    private readonly RabbitMQOptions _options = options.Value;
    private const string QueueName = "order.events.queue";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("订单事件消费者启动");

        var channel = await connectionProvider.CreateChannelAsync(stoppingToken);

        // 声明队列
        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // 绑定所有订单事件（使用通配符）
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

                logger.LogInformation("收到订单事件: {MessageType}, 内容: {Message}", messageType, message);

                var orderData = JsonSerializer.Deserialize<JsonElement>(message);
                var orderId = orderData.GetProperty("OrderId").GetString();

                // 根据消息类型处理不同事件
                switch (messageType)
                {
                    case "ordercreated":
                        await HandleOrderCreatedAsync(orderData);
                        break;

                    case "orderstatusupdated":
                        await HandleOrderStatusUpdatedAsync(orderData);
                        break;

                    case "orderdeleted":
                        await HandleOrderDeletedAsync(orderData);
                        break;

                    default:
                        logger.LogWarning("未知消息类型: {MessageType}", messageType);
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

        // 保持运行直到取消
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private Task HandleOrderCreatedAsync(JsonElement orderData)
    {
        var orderId = orderData.GetProperty("OrderId").GetString();
        var customerName = orderData.GetProperty("CustomerName").GetString();
        var amount = orderData.GetProperty("Amount").GetDecimal();

        logger.LogInformation(
            "[订单创建] ID={OrderId}, Customer={Customer}, Amount={Amount}",
            orderId, customerName, amount);

        // 业务逻辑：发送欢迎邮件、更新统计等

        return Task.CompletedTask;
    }

    private Task HandleOrderStatusUpdatedAsync(JsonElement orderData)
    {
        var orderId = orderData.GetProperty("OrderId").GetString();
        var oldStatus = orderData.GetProperty("OldStatus").GetString();
        var newStatus = orderData.GetProperty("NewStatus").GetString();

        logger.LogInformation(
            "[状态更新] ID={OrderId}, {OldStatus} -> {NewStatus}",
            orderId, oldStatus, newStatus);

        // 业务逻辑：状态变更通知、触发后续流程等

        return Task.CompletedTask;
    }

    private Task HandleOrderDeletedAsync(JsonElement orderData)
    {
        var orderId = orderData.GetProperty("OrderId").GetString();
        var customerName = orderData.GetProperty("CustomerName").GetString();
        var status = orderData.GetProperty("Status").GetString();

        logger.LogInformation(
            "[订单删除] ID={OrderId}, Customer={Customer}, Status={Status}",
            orderId, customerName, status);

        // 业务逻辑：归档记录、通知客户等

        return Task.CompletedTask;
    }
}
