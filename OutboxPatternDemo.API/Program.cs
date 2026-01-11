using Microsoft.EntityFrameworkCore;
using OutboxPatternDemo.API.Caching;
using OutboxPatternDemo.Domain;
using OutboxPatternDemo.Infrastructure.Data;
using OutboxPatternDemo.Infrastructure.Repositories;
using OutboxPatternDemo.Services;
using OutboxPatternDemo.Services.RabbitMQ;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    //options.UseInMemoryDatabase("OutboxPatternDemo");
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// 配置缓存
builder.Services.AddHybridCaching(builder.Configuration);

// 配置 RabbitMQ
builder.Services.Configure<RabbitMQOptions>(
    builder.Configuration.GetSection(RabbitMQOptions.SectionName));
builder.Services.AddSingleton<ConnectionProvider>();

builder.Services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();
builder.Services.AddScoped<IOrderService, CacheAwareOrderService>();
builder.Services.AddScoped<IMessagePublisher, RabbitMQMessagePublisher>();

builder.Services.AddHostedService<CacheAwareOutboxProcessor>();
builder.Services.AddHostedService<OrderEventConsumer>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    context.Database.EnsureCreated();
}

app.Run();
