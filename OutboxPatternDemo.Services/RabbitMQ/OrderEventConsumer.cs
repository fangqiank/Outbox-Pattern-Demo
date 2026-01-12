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
    private const string ConsumerTag = "order-event-consumer-v1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("========================================");
        logger.LogInformation("RabbitMQ 订单事件消费者启动中...");
        logger.LogInformation("Exchange: {Exchange}", _options.ExchangeName);
        logger.LogInformation("Queue: {Queue}", QueueName);
        logger.LogInformation("Consumer Tag: {ConsumerTag}", ConsumerTag);
        logger.LogInformation("========================================");

        var channel = await connectionProvider.CreateChannelAsync(stoppingToken);

        // 显示 Channel 信息
        logger.LogInformation("[RabbitMQ Consumer] Channel #{ChannelNumber} 已创建",
            channel.ChannelNumber);

        // 声明队列
        var queueDeclareResult = await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        logger.LogInformation("[RabbitMQ Consumer] 队列已声明: {QueueName} (消息数: {MessageCount}, 消费者数: {ConsumerCount})",
            queueDeclareResult.QueueName,
            queueDeclareResult.MessageCount,
            queueDeclareResult.ConsumerCount);

        // 绑定所有订单事件（使用通配符）
        var routingKeys = new[] { "ordercreated", "orderstatusupdated", "orderdeleted" };
        foreach (var routingKey in routingKeys)
        {
            await channel.QueueBindAsync(
                queue: QueueName,
                exchange: _options.ExchangeName,
                routingKey: routingKey,
                cancellationToken: stoppingToken);

            logger.LogInformation("[RabbitMQ Consumer] 队列绑定: {Queue} <- {RoutingKey} (@{Exchange})",
                QueueName, routingKey, _options.ExchangeName);
        }

        // 设置 prefetch - 控制 RabbitMQ 分发消息的数量
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, cancellationToken: stoppingToken);
        logger.LogInformation("[RabbitMQ Consumer] QoS 设置: PrefetchCount=10 (每次预取 10 条消息)");

        // 创建 RabbitMQ 自带的 AsyncEventingBasicConsumer
        var consumer = new AsyncEventingBasicConsumer(channel);
        logger.LogInformation("[RabbitMQ Consumer] AsyncEventingBasicConsumer 已创建");

        // 订阅 Consumer 的事件
        consumer.ReceivedAsync += async (sender, args) =>
        {
            await OnMessageReceivedAsync(channel, args, stoppingToken);
        };

        consumer.ShutdownAsync += (sender, args) =>
        {
            logger.LogWarning("[RabbitMQ Consumer] Consumer 已关闭: {ReplyCode} - {ReplyText}",
                args.ReplyCode, args.ReplyText);
            return Task.CompletedTask;
        };

        consumer.UnregisteredAsync += (sender, args) =>
        {
            logger.LogInformation("[RabbitMQ Consumer] Consumer 已注销");
            return Task.CompletedTask;
        };

        // 开始消费（autoAck: false 表示需要手动确认）
        await channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumerTag: ConsumerTag,
            consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation("========================================");
        logger.LogInformation("[RabbitMQ Consumer] 开始消费消息 (AutoAck: False, 手动确认模式)");
        logger.LogInformation("等待接收订单事件...");
        logger.LogInformation("========================================");

        // 保持运行直到取消
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private async Task OnMessageReceivedAsync(IChannel channel, BasicDeliverEventArgs args, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var deliveryTag = args.DeliveryTag;

        logger.LogInformation("");
        logger.LogInformation("----------------------------------------");
        logger.LogInformation("[RabbitMQ Consumer] 收到消息 (DeliveryTag: {DeliveryTag})", deliveryTag);
        logger.LogInformation("  Exchange: {Exchange}, RoutingKey: {RoutingKey}",
            args.Exchange, args.RoutingKey);
        logger.LogInformation("  ConsumerTag: {ConsumerTag}", args.ConsumerTag);
        logger.LogInformation("  Redelivered: {Redelivered}", args.Redelivered);

        // 显示消息属性
        if (args.BasicProperties != null)
        {
            logger.LogInformation("  MessageId: {MessageId}", args.BasicProperties.MessageId);
            logger.LogInformation("  ContentType: {ContentType}", args.BasicProperties.ContentType);
            logger.LogInformation("  DeliveryMode: {DeliveryMode} ({Persistent})",
                args.BasicProperties.DeliveryMode,
                args.BasicProperties.DeliveryMode == DeliveryModes.Persistent ? "持久化" : "非持久化");
            logger.LogInformation("  Timestamp: {Timestamp}",
                args.BasicProperties.Timestamp.UnixTime > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(args.BasicProperties.Timestamp.UnixTime).ToLocalTime()
                    : "未设置");
            logger.LogInformation("  BodySize: {BodySize} bytes", args.Body.Length);
        }

        try
        {
            var message = Encoding.UTF8.GetString(args.Body.ToArray());
            var messageType = args.RoutingKey;

            logger.LogInformation("  Message: {Message}", message);

            var orderData = JsonSerializer.Deserialize<JsonElement>(message);

            // 根据消息类型处理不同事件
            var result = messageType switch
            {
                "ordercreated" => HandleOrderCreatedAsync(orderData),
                "orderstatusupdated" => HandleOrderStatusUpdatedAsync(orderData),
                "orderdeleted" => HandleOrderDeletedAsync(orderData),
                _ => HandleUnknownMessage(messageType)
            };

            // 手动确认消息 (BasicAck) - 通知 RabbitMQ 消息已成功处理
            await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken: cancellationToken);

            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            logger.LogInformation("[RabbitMQ Consumer] 消息已确认 (BasicAck Delivertag: {DeliveryTag}) - 耗时: {Elapsed}ms ✓",
                deliveryTag, elapsed);
        }
        catch (Exception ex)
        {
            // 拒绝消息并重新入队 (BasicNack with requeue: true)
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true, cancellationToken: cancellationToken);

            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            logger.LogError(ex,
                "[RabbitMQ Consumer] 消息处理失败，已重新入队 (BasicNack DeliveryTag: {DeliveryTag}, Requeue: true) - 耗时: {Elapsed}ms ✗",
                deliveryTag, elapsed);
        }
        finally
        {
            logger.LogInformation("----------------------------------------");
        }
    }

    private Task<string> HandleOrderCreatedAsync(JsonElement orderData)
    {
        var orderId = orderData.GetProperty("OrderId").GetString();
        var customerName = orderData.GetProperty("CustomerName").GetString();
        var amount = orderData.GetProperty("Amount").GetDecimal();

        logger.LogInformation("  [订单创建事件] ID={OrderId}, Customer={Customer}, Amount={Amount:C2}",
            orderId, customerName, amount);
        logger.LogInformation("  [业务处理] 发送欢迎邮件、更新订单统计...");

        // 模拟业务处理
        // await _emailService.SendWelcomeEmailAsync(customerName);
        // await _statisticsService.IncrementOrderCountAsync();

        return Task.FromResult("订单创建处理完成");
    }

    private Task<string> HandleOrderStatusUpdatedAsync(JsonElement orderData)
    {
        var orderId = orderData.GetProperty("OrderId").GetString();
        var oldStatus = orderData.GetProperty("OldStatus").GetString();
        var newStatus = orderData.GetProperty("NewStatus").GetString();

        logger.LogInformation("  [状态更新事件] ID={OrderId}, {OldStatus} → {NewStatus}",
            orderId, oldStatus, newStatus);
        logger.LogInformation("  [业务处理] 发送状态变更通知、触发后续流程...");

        // 模拟业务处理
        // await _notificationService.NotifyStatusChangeAsync(orderId, newStatus);
        // await _workflowService.TriggerOrderWorkflowAsync(orderId, newStatus);

        return Task.FromResult("状态更新处理完成");
    }

    private Task<string> HandleOrderDeletedAsync(JsonElement orderData)
    {
        var orderId = orderData.GetProperty("OrderId").GetString();
        var customerName = orderData.GetProperty("CustomerName").GetString();
        var status = orderData.GetProperty("Status").GetString();

        logger.LogInformation("  [订单删除事件] ID={OrderId}, Customer={Customer}, Status={Status}",
            orderId, customerName, status);
        logger.LogInformation("  [业务处理] 归档订单记录、清理相关数据...");

        // 模拟业务处理
        // await _archiveService.ArchiveOrderAsync(orderId);
        // await _cleanupService.CleanupOrderRelatedDataAsync(orderId);

        return Task.FromResult("订单删除处理完成");
    }

    private Task<string> HandleUnknownMessage(string messageType)
    {
        logger.LogWarning("  [未知消息类型] {MessageType}", messageType);
        return Task.FromResult("未知消息类型");
    }
}
