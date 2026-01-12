using Microsoft.EntityFrameworkCore;
using OutboxPatternDemo.API.Caching;
using OutboxPatternDemo.Domain;
using OutboxPatternDemo.Infrastructure.Data;
using OutboxPatternDemo.Infrastructure.Repositories;
using OutboxPatternDemo.Services;
using OutboxPatternDemo.Services.InMemory;
using OutboxPatternDemo.Services.RabbitMQ;

namespace OutboxPatternDemo.API.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 配置 DbContext
        services.AddDbContext<AppDbContext>(options =>
        {
            //options.UseInMemoryDatabase("OutboxPatternDemo");
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
        });

        // 配置缓存
        services.AddHybridCaching(configuration);

        // 配置 RabbitMQ
        services.Configure<RabbitMQOptions>(
            configuration.GetSection(RabbitMQOptions.SectionName));

        services.AddSingleton<ConnectionProvider>();

        // 仓储层
        services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();

        // 服务层
        services.AddScoped<IOrderService, CacheAwareOrderService>();
        services.AddScoped<IMessagePublisher, RabbitMQMessagePublisher>();

        // 后台服务
        services.AddHostedService<CacheAwareOutboxProcessor>();

        // 根据配置选择 Consumer
        var enableSimpleConsumer = configuration.GetValue<bool>("ConsumerSettings:EnableSimpleConsumer", false);
        if (enableSimpleConsumer)
        {
            services.AddHostedService<SimpleOrderEventConsumer>();
        }
        else
        {
            services.AddHostedService<OrderEventConsumer>();
        }

        return services;
    }
}
