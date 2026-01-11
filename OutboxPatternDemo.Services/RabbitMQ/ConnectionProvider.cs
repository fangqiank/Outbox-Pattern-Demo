using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace OutboxPatternDemo.Services.RabbitMQ;

public class ConnectionProvider(
    ILogger<ConnectionProvider> logger,
    IOptions<RabbitMQOptions> options
) : IDisposable
{
    private readonly RabbitMQOptions _options = options.Value;
    private IConnection? _connection;
    private readonly object _lock = new();

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        lock (_lock)
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }
        }

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost
        };

        logger.LogInformation("连接 RabbitMQ: {Host}:{Port}/{VirtualHost}",
            _options.HostName, _options.Port, _options.VirtualHost);

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        return _connection;
    }

    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync();

        // 声明交换机
        await channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: _options.ExchangeType,
            durable: true,
            autoDelete: false);

        return channel;
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
