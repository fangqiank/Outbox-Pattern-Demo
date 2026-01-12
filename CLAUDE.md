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

## RabbitMQ 集成

### Exchange（交换机）

Exchange 是 RabbitMQ 中负责**接收消息并路由到队列**的核心组件。

**消息流转过程：**
```
Publisher → Exchange → Queue → Consumer
           (路由规则)
```

Publisher 不直接发送消息到队列，而是发送到 Exchange，Exchange 根据路由规则将消息分发到绑定的队列。

**Exchange 类型：**

| 类型 | 说明 | 路由键匹配 |
|------|------|-----------|
| **Direct** | 精确匹配 | 完全相同 |
| **Topic** | 通配符匹配 | `*` 单单词, `#` 多单词 |
| **Fanout** | 广播，忽略路由键 | 发送到所有绑定队列 |
| **Headers** | 根据消息头匹配 | 很少使用 |

**本项目配置：**

```json
"RabbitMQ": {
  "ExchangeName": "outbox.exchange",
  "ExchangeType": "topic"    // 使用 Topic 类型
}
```

**路由规则：**
```
outbox.exchange (topic)
    │
    ├─ ordercreated       → order.events.queue
    ├─ orderstatusupdated → order.events.queue
    └─ orderdeleted       → order.events.queue
```

**Publisher 发送消息**（见 `RabbitMQMessagePublisher.cs`）：
```csharp
await channel.BasicPublishAsync(
    exchange: "outbox.exchange",
    routingKey: "ordercreated",  // 路由键
    body: messageBody
);
```

**Consumer 绑定队列**（见 `OrderEventConsumer.cs`）：
```csharp
await channel.QueueBindAsync(
    queue: "order.events.queue",
    exchange: "outbox.exchange",
    routingKey: "ordercreated"     // 绑定此路由键
);
```

**Topic Exchange 匹配示例：**

| Routing Key | 绑定模式 | 是否匹配 |
|-------------|----------|----------|
| `order.created` | `order.*` | ✓ |
| `order.created` | `#.created` | ✓ |
| `order.created` | `order.created` | ✓ |
| `order.created` | `order.updated` | ✗ |

### Consumer 类型

项目提供两种 Consumer，通过配置切换：

| Consumer | 队列名称 | 日志级别 | 使用场景 |
|----------|----------|----------|----------|
| `OrderEventConsumer` | `order.events.queue` | 详细（显示 RabbitMQ 消费过程） | 生产环境 |
| `SimpleOrderEventConsumer` | `order.events.simple.queue` | 简化（仅业务日志） | 开发/测试 |

**切换方式：**
```json
"ConsumerSettings": {
  "EnableSimpleConsumer": false   // true=Simple, false=Full
}
```

### Consumer 日志示例

**启动时：**
```
========================================
RabbitMQ 订单事件消费者启动中...
Exchange: outbox.exchange
Queue: order.events.queue
Consumer Tag: order-event-consumer-v1
========================================
[RabbitMQ Consumer] Channel #1 已创建
[RabbitMQ Consumer] 队列已声明: order.events.queue (消息数: 0, 消费者数: 0)
[RabbitMQ Consumer] 队列绑定: order.events.queue <- ordercreated (@outbox.exchange)
[RabbitMQ Consumer] QoS 设置: PrefetchCount=10
[RabbitMQ Consumer] 开始消费消息 (AutoAck: False, 手动确认模式)
```

**收到消息时：**
```
----------------------------------------
[RabbitMQ Consumer] 收到消息 (DeliveryTag: 1)
  Exchange: outbox.exchange, RoutingKey: ordercreated
  ConsumerTag: order-event-consumer-v1
  Redelivered: False
  MessageId: xxx-xxx-xxx
  ContentType: application/json
  DeliveryMode: 2 (持久化)
  BodySize: xxx bytes
  Message: {"OrderId":"...","CustomerName":"..."}
  [订单创建事件] ID=xxx, Customer=xxx, Amount=¥xxx
  [业务处理] 发送欢迎邮件、更新订单统计...
[RabbitMQ Consumer] 消息已确认 (BasicAck DeliveryTag: 1) - 耗时: 5ms ✓
----------------------------------------
```

### 关键概念

**DeliveryTag**：每条消息的唯一递增序号，用于 ACK/NACK 确认

**ConsumerTag**：消费者的标识符，用于区分不同的消费者实例

**PrefetchCount (QoS)**：控制 RabbitMQ 一次推送多少条消息给消费者，防止消费者被压垮

**AutoAck**：
- `false`：手动确认模式（推荐），处理成功后调用 `BasicAck`
- `true`：自动确认模式，消息发送后立即确认（可能丢失消息）

**BasicAck vs BasicNack**：
- `BasicAck`：确认消息处理成功
- `BasicNack(requeue: true)`：处理失败，消息重新入队
- `BasicNack(requeue: false)`：处理失败，消息丢弃（或进入死信队列）
