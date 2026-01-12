using Microsoft.AspNetCore.Mvc;
using OutboxPatternDemo.API.Caching;
using OutboxPatternDemo.Domain.Entities;
using OutboxPatternDemo.Services;

namespace OutboxPatternDemo.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController(
        IOrderService orderService,
        ICacheService cacheService,
        ILogger<OrdersController> logger
        ) : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
        {
            try
            {
                var orderId = await orderService.CreateOrderAsync(
                    request.CustomerName,
                    request.Amount);

                return Ok(new
                {
                    OrderId = orderId,
                    Message = "订单创建成功，缓存已更新"
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "创建订单API失败");
                return StatusCode(500, "创建订单时出错");
            }
        }

        [HttpGet("{orderId}")]
        public async Task<IActionResult> GetOrder(Guid orderId)
        {
            var order = await orderService.GetOrderAsync(orderId);

            if (order == null)
                return NotFound();

            // 添加缓存相关头部信息
            Response.Headers.Append("X-Cache-Status", "HYBRID");
            Response.Headers.Append("X-Cache-Key", CacheKeys.OrderById(orderId));

            return Ok(order);
        }

        [HttpGet("customer/{customerName}")]
        public async Task<IActionResult> GetCustomerOrders(string customerName)
        {
            var orders = await orderService.GetOrdersByCustomerAsync(customerName);

            Response.Headers.Append("X-Cache-Status", "HYBRID");
            Response.Headers.Append("X-Cache-Keys-Count", orders.Count.ToString());

            return Ok(orders);
        }

        [HttpPut("{orderId}/status")]
        public async Task<IActionResult> UpdateOrderStatus(
            Guid orderId,
            [FromBody] UpdateOrderStatusRequest request)
        {
            try
            {
                await orderService.UpdateOrderStatusAsync(orderId, request.Status);
                return Ok(new { Message = "订单状态更新成功，缓存已刷新" });
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "更新订单状态失败");
                return StatusCode(500, "更新订单状态时出错");
            }
        }

        [HttpDelete("{orderId}")]
        public async Task<IActionResult> DeleteOrder(Guid orderId)
        {
            try
            {
                await orderService.DeleteOrderAsync(orderId);
                return Ok(new { Message = "订单已删除，相关缓存已清除" });
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "删除订单失败");
                return StatusCode(500, "删除订单时出错");
            }
        }

        [HttpPost("cache/clear/{pattern}")]
        public async Task<IActionResult> ClearCache(string pattern)
        {
            try
            {
                await cacheService.RemoveByPatternAsync(pattern);
                return Ok(new
                {
                    Message = "缓存清理请求已提交",
                    Pattern = pattern
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "清理缓存失败");
                return StatusCode(500, "清理缓存时出错");
            }
        }

        [HttpGet("cache/stats")]
        public async Task<IActionResult> GetCacheStats()
        {
            var memoryInfo = GC.GetGCMemoryInfo();

            var stats = new
            {
                HybridCacheEnabled = true,
                RedisConfigured = !string.IsNullOrEmpty(
                    HttpContext.RequestServices.GetService<IConfiguration>()?
                        .GetConnectionString("Redis")),
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

            return Ok(stats);
        }

    }

    public record UpdateOrderStatusRequest(OrderStatus Status);

    public record CreateOrderRequest(string CustomerName, decimal Amount);
}
