using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace OutboxPatternDemo.Services.RabbitMQ;

public class RabbitMQMessagePublisher(
    ILogger<RabbitMQMessagePublisher> logger,
    ConnectionProvider connectionProvider,
    IOptions<RabbitMQOptions> options
) : IMessagePublisher
{
    private readonly RabbitMQOptions _options = options.Value;

    public async Task<bool> PublishAsync(
        string messageType,
        string content,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var channel = await connectionProvider.CreateChannelAsync(cancellationToken);

            var routingKey = messageType.ToLowerInvariant();
            var body = System.Text.Encoding.UTF8.GetBytes(content);

            logger.LogInformation("");
            logger.LogInformation("[RabbitMQ Publisher] 准备发布消息...");
            logger.LogInformation("  Exchange: {Exchange}", _options.ExchangeName);
            logger.LogInformation("  RoutingKey: {RoutingKey}", routingKey);
            logger.LogInformation("  MessageType: {MessageType}", messageType);
            logger.LogInformation("  BodySize: {BodySize} bytes", body.Length);

            var properties = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent,
                ContentType = "application/json",
                MessageId = Guid.NewGuid().ToString(),
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                AppId = "OutboxPatternDemo.API",
                Type = messageType
            };

            logger.LogInformation("  MessageId: {MessageId}", properties.MessageId);
            logger.LogInformation("  DeliveryMode: {DeliveryMode} (Persistent)",
                properties.DeliveryMode);
            logger.LogInformation("  Timestamp: {Timestamp}",
                DateTimeOffset.FromUnixTimeSeconds(properties.Timestamp.UnixTime).ToLocalTime());

            await channel.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            logger.LogInformation("[RabbitMQ Publisher] 消息已发布成功 (Channel #{ChannelNumber})",
                channel.ChannelNumber);
            logger.LogInformation("");

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[RabbitMQ Publisher] 发布消息失败: {MessageType}", messageType);
            return false;
        }
    }
}
