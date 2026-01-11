
using Microsoft.Extensions.Logging;

namespace OutboxPatternDemo.Services
{
    public class MockMessagePublisher(
        ILogger<MockMessagePublisher> logger
        ) : IMessagePublisher
    {
        public async Task<bool> PublishAsync(
            string messageType, 
            string content, 
            CancellationToken cancellationToken)
        {
            try
            {
                // 模拟网络延迟
                await Task.Delay(100, cancellationToken);

                // 模拟90%的成功率（测试错误处理）
                var random = new Random();
                if (random.Next(0, 10) < 9) // 90% 成功率
                {
                    logger.LogInformation(
                        "模拟发布消息: {Type} - {Content}",
                        messageType, content.Substring(0, Math.Min(100, content.Length)));
                    return true;
                }

                logger.LogWarning("模拟消息发布失败");
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "发布消息时出错");
                return false;
            }
        }
    }
}
