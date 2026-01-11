# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

这是一个 Outbox Pattern（发件箱模式）演示项目，使用 .NET 10.0 和 ASP.NET Core API。项目展示了如何在分布式系统中实现最终一致性，结合了 HybridCache（内存+Redis 二级缓存）来优化性能。

## 架构分层

项目采用标准的干净架构（Clean Architecture）分层：

```
OutboxPatternDemo.Domain/          # 领域层：实体、接口
OutboxPatternDemo.Infrastructure/  # 基础设施层：数据访问、仓储实现
OutboxPatternDemo.Services/        # 服务层：业务服务、后台服务
OutboxPatternDemo.API/             # 表示层：API 控制器、缓存配置
```

### 核心架构模式

**Outbox Pattern 流程：**
1. 业务操作和创建 OutboxMessage 在同一数据库事务中完成（原子性）
2. `CacheAwareOutboxProcessor`（后台服务）定期轮询待处理消息
3. 发布到外部消息队列后更新消息状态
4. 使用缓存实现幂等性检查和性能优化

**缓存策略：**
- 使用 `Microsoft.Extensions.Caching.Hybrid` 实现二级缓存（本地 L1 + 分布式 L2）
- `CacheService` 封装 HybridCache，提供降级策略
- `CacheAwareOrderService` 在写入时更新缓存，使相关缓存失效
- `CacheAwareOutboxProcessor` 使用缓存防止并发处理和重复消费

## 构建和运行

### 构建项目
```powershell
dotnet build
```

### 运行项目（开发环境，使用 InMemory 数据库）
```powershell
dotnet run --project OutboxPatternDemo.API
```

### 生产环境配置
在 `appsettings.json` 中配置连接字符串：
- `DefaultConnection`: SQL Server 或 PostgreSQL 连接串
- `Redis`: Redis 连接串（可选，不配置则仅使用内存缓存）

切换到 PostgreSQL 需要在 `Program.cs` 中取消注释：
```csharp
// options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
```

### 数据库迁移
项目使用 `EnsureCreated()` 自动创建数据库结构。如需使用 EF Core 迁移：
```powershell
# 添加迁移
dotnet ef migrations add InitialCreate --project OutboxPatternDemo.Infrastructure

# 更新数据库
dotnet ef database update --project OutboxPatternDemo.API
```

## 关键设计决策

### 事务边界
- 订单创建和 OutboxMessage 必须在同一事务中（见 `CacheAwareOrderService.CreateOrderAsync`）
- 缓存更新在事务外异步执行，失败不影响主业务

### 并发控制
- OutboxMessage 状态转换：Pending → Processing → Processed/Failed
- `CacheAwareOutboxProcessor` 使用缓存锁防止多实例并发处理同一消息
- 批量处理默认每次 50 条（可在 `ProcessWithCacheAwarenessAsync` 中调整）

### 缓存键管理
- 统一在 `CacheKeys` 静态类中定义
- 格式：`{resource}:{identifier}`，如 `order:{guid}`、`orders:customer:{name}`

### 依赖注入配置
- `IOrderService` → `CacheAwareOrderService`（带缓存）
- `IMessagePublisher` → `MockMessagePublisher`（演示用，生产替换为真实实现）
- 后台服务：`CacheAwareOutboxProcessor`（替换了基础的 `OutboxProcessorService`）

## API 端点

- `POST /api/orders` - 创建订单
- `GET /api/orders/{orderId}` - 获取订单（带缓存）
- `GET /api/orders/customer/{customerName}` - 获取客户订单列表
- `PUT /api/orders/{orderId}/status` - 更新订单状态
- `POST /api/orders/cache/clear/{pattern}` - 清理缓存
- `GET /api/orders/cache/stats` - 缓存统计信息

Scalar API 文档在开发环境可用：`/scalar`

## 常见任务

### 添加新的 Outbox 消息类型
1. 在 `CacheAwareOutboxProcessor.HandleMessageCacheUpdatesAsync` 的 switch 中添加类型处理
2. 定义消息内容和缓存失效策略

### 切换数据库
在 `Program.cs` 中：
- InMemory: `UseInMemoryDatabase()`
- SQL Server: `UseSqlServer()`
- PostgreSQL: `UseNpgsql()`

### 调整缓存策略
修改 `CacheService` 中的默认过期时间，或在调用时指定：
```csharp
await cache.GetOrCreateAsync(key, factory, TimeSpan.FromMinutes(10));
```
