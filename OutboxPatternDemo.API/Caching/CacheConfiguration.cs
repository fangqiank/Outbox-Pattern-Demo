using Microsoft.Extensions.Configuration.Json;
using System.Net.Sockets;

namespace OutboxPatternDemo.API.Caching
{
    public static class CacheConfiguration
    {
        public static IServiceCollection AddHybridCaching(
            this IServiceCollection services,
            IConfiguration configuration
            )
        {
            // 从配置读取 Redis 连接字符串
            var redisConnection = configuration.GetConnectionString("Redis");

            // 优先使用环境变量
            var redisEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Redis")
                          ?? Environment.GetEnvironmentVariable("REDIS");

            if (!string.IsNullOrWhiteSpace(redisEnv))
            {
                redisConnection = redisEnv;
            }

            // Dynamic Connectivity Check & Fallback
            if (!string.IsNullOrWhiteSpace(redisConnection))
            {
                if (!IsRedisReachable(redisConnection))
                {
                    Console.WriteLine($"[WARNING] Redis connection to '{redisConnection}' failed validation. Attempting to fallback to appsettings.json...");
                    
                    var fallbackConnection = GetFallbackConnectionFromConfig(configuration);
                    if (!string.IsNullOrWhiteSpace(fallbackConnection) && fallbackConnection != redisConnection)
                    {
                         Console.WriteLine($"[INFO] Fallback found: '{fallbackConnection}'. Verifying...");
                         if (IsRedisReachable(fallbackConnection))
                         {
                             Console.WriteLine($"[INFO] Switching to fallback connection: '{fallbackConnection}'");
                             redisConnection = fallbackConnection;
                         }
                         else
                         {
                             Console.WriteLine($"[WARNING] Fallback connection also failed validation.");
                         }
                    }
                    else
                    {
                        Console.WriteLine($"[WARNING] No valid fallback connection found in configuration providers.");
                    }
                }
            }

            // 使用 IsNullOrWhiteSpace 检查空字符串
            if (!string.IsNullOrWhiteSpace(redisConnection))
            {
                // 配置 Redis
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = redisConnection;
                    options.InstanceName = "OutboxDemo:";
                });
            }
            else
            {
                // 显式配置内存分布式缓存
                services.AddDistributedMemoryCache();
            }

            // 配置HybridCache
            services.AddHybridCache();

            services.AddScoped<ICacheService, CacheService>();

            return services;
        }

        private static bool IsRedisReachable(string connectionString)
        {
            try
            {
                var options = StackExchange.Redis.ConfigurationOptions.Parse(connectionString);
                foreach (var endpoint in options.EndPoints)
                {
                    if (endpoint is System.Net.DnsEndPoint dnsEp)
                    {
                        using var client = new TcpClient();
                        // 1 second timeout for check
                        if (client.ConnectAsync(dnsEp.Host, dnsEp.Port).Wait(1000)) 
                        {
                            return true;
                        }
                    }
                    else if (endpoint is System.Net.IPEndPoint ipEp)
                    {
                        using var client = new TcpClient();
                        if (client.ConnectAsync(ipEp.Address, ipEp.Port).Wait(1000))
                        {
                            return true;
                        }
                    }
                }
            }
            catch 
            {
                // Ignore parsing or connection errors during check
            }
            return false;
        }

        private static string? GetFallbackConnectionFromConfig(IConfiguration configuration)
        {
            if (configuration is IConfigurationRoot root)
            {
                // Iterate providers to find one from a JSON file (appsettings.json)
                foreach (var provider in root.Providers)
                {
                    if (provider.ToString()!.Contains("JsonConfigurationProvider"))
                    {
                        if (provider.TryGet("ConnectionStrings:Redis", out var val))
                        {
                            return val;
                        }
                    }
                }
            }
            return null;
        }
    }
}
