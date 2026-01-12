using Microsoft.AspNetCore.Mvc;
using OutboxPatternDemo.API.Caching;
using OutboxPatternDemo.Domain.Entities;
using OutboxPatternDemo.Services;

namespace OutboxPatternDemo.API.Endpoints;

public static class OrdersEndpoints
{
    private static ILogger CreateLogger(ILoggerFactory factory) => factory.CreateLogger("OrdersEndpoints");

    public static void MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders")
            .WithTags("Orders");

        // POST /api/orders - 创建订单
        group.MapPost("/", CreateOrder);

        // GET /api/orders/{orderId} - 获取单个订单
        group.MapGet("/{orderId}", GetOrder);

        // GET /api/orders/customer/{customerName} - 获取客户订单列表
        group.MapGet("/customer/{customerName}", GetCustomerOrders);

        // PUT /api/orders/{orderId}/status - 更新订单状态
        group.MapPut("/{orderId}/status", UpdateOrderStatus);

        // DELETE /api/orders/{orderId} - 删除订单
        group.MapDelete("/{orderId}", DeleteOrder);

        // POST /api/orders/cache/clear/{pattern} - 清理缓存
        group.MapPost("/cache/clear/{pattern}", ClearCache);

        // GET /api/orders/cache/stats - 缓存统计信息
        group.MapGet("/cache/stats", GetCacheStats);
    }

    private static async Task<IResult> CreateOrder(
        [FromBody] CreateOrderRequest request,
        [FromServices] IOrderService orderService,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = CreateLogger(loggerFactory);
        try
        {
            var orderId = await orderService.CreateOrderAsync(request.CustomerName, request.Amount);
            return TypedResults.Ok(new { OrderId = orderId, Message = "订单创建成功，缓存已更新" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "创建订单API失败");
            return TypedResults.StatusCode(500);
        }
    }

    private static async Task<IResult> GetOrder(
        Guid orderId,
        [FromServices] IOrderService orderService)
    {
        var order = await orderService.GetOrderAsync(orderId);
        return order is null ? TypedResults.NotFound() : TypedResults.Ok(order);
    }

    private static async Task<IResult> GetCustomerOrders(
        string customerName,
        [FromServices] IOrderService orderService)
    {
        var orders = await orderService.GetOrdersByCustomerAsync(customerName);
        return TypedResults.Ok(orders);
    }

    private static async Task<IResult> UpdateOrderStatus(
        Guid orderId,
        [FromBody] UpdateOrderStatusRequest request,
        [FromServices] IOrderService orderService,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = CreateLogger(loggerFactory);
        try
        {
            await orderService.UpdateOrderStatusAsync(orderId, request.Status);
            return TypedResults.Ok(new { Message = "订单状态更新成功，缓存已刷新" });
        }
        catch (ArgumentException)
        {
            return TypedResults.NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新订单状态失败");
            return TypedResults.StatusCode(500);
        }
    }

    private static async Task<IResult> DeleteOrder(
        Guid orderId,
        [FromServices] IOrderService orderService,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = CreateLogger(loggerFactory);
        try
        {
            await orderService.DeleteOrderAsync(orderId);
            return TypedResults.Ok(new { Message = "订单已删除，相关缓存已清除" });
        }
        catch (ArgumentException)
        {
            return TypedResults.NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "删除订单失败");
            return TypedResults.StatusCode(500);
        }
    }

    private static async Task<IResult> ClearCache(
        string pattern,
        [FromServices] ICacheService cacheService,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = CreateLogger(loggerFactory);
        try
        {
            await cacheService.RemoveByPatternAsync(pattern);
            return TypedResults.Ok(new { Message = "缓存清理请求已提交", Pattern = pattern });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "清理缓存失败");
            return TypedResults.StatusCode(500);
        }
    }

    private static IResult GetCacheStats([FromServices] IConfiguration configuration)
    {
        var memoryInfo = GC.GetGCMemoryInfo();

        var stats = new
        {
            HybridCacheEnabled = true,
            RedisConfigured = !string.IsNullOrEmpty(configuration.GetConnectionString("Redis")),
            Timestamp = DateTime.UtcNow,
            Memory = new
            {
                TotalMemory = GC.GetTotalMemory(false),
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2),
                MemoryLoadBytes = memoryInfo.MemoryLoadBytes,
                HighMemoryLoadThresholdBytes = memoryInfo.HighMemoryLoadThresholdBytes,
                FragmentedBytes = memoryInfo.FragmentedBytes
            }
        };
        return TypedResults.Ok(stats);
    }
}

public record UpdateOrderStatusRequest(OrderStatus Status);

public record CreateOrderRequest(string CustomerName, decimal Amount);
