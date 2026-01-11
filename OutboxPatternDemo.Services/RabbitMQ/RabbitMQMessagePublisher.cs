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

            var properties = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent,
                ContentType = "application/json",
                MessageId = Guid.NewGuid().ToString(),
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            };

            await channel.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            logger.LogInformation(
                "RabbitMQ 消息已发布: {MessageType} -> {RoutingKey} (Length: {Length})",
                messageType, routingKey, body.Length);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RabbitMQ 发布消息失败: {MessageType}", messageType);
            return false;
        }
    }
}
