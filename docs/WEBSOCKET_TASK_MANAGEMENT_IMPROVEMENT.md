# WebSocket 任务管理改进总结

## 改进日期
2025年9月30日

## 问题分析

### 原有设计的问题

在 `WebSocketClient.ConnectAsync()` 方法中，原先使用以下代码启动消息接收任务：

```csharp
_ = Task.Run(ReceiveMessagesAsync);
```

这种设计存在以下问题：

1. **线程池资源占用**：`Task.Run()` 会将任务放到线程池中执行，对于长时间运行的后台任务（如 WebSocket 消息接收循环），会不必要地占用线程池资源。

2. **异常处理缺失**：使用 `_ =` 丢弃返回值意味着没有跟踪任务的生命周期，如果 `ReceiveMessagesAsync()` 抛出未捕获的异常，调用者无法感知。

3. **生命周期管理不完善**：`DisconnectAsync()` 只取消 `CancellationToken`，但没有等待接收任务真正结束，可能导致资源泄漏或"幽灵任务"。

4. **缺乏任务状态监控**：无法查询接收任务的运行状态，难以调试和排查问题。

## 改进方案

### 1. 添加任务引用字段

```csharp
private Task? _receiveTask;
```

添加私有字段用于存储和跟踪消息接收任务的引用。

### 2. 使用 LongRunning 任务

```csharp
// 开始接收消息 - 使用 LongRunning 任务避免占用线程池
_receiveTask = Task.Factory.StartNew(
    ReceiveMessagesAsync,
    _cancellationTokenSource.Token,
    TaskCreationOptions.LongRunning,
    TaskScheduler.Default
).Unwrap();
```

**关键改进点**：
- `TaskCreationOptions.LongRunning`：告诉任务调度器这是长时间运行的任务，会分配专用线程而不占用线程池
- `.Unwrap()`：用于解包嵌套的 `Task<Task>`，获取内部的实际任务
- 存储任务引用：便于后续监控和等待任务完成

### 3. 添加任务异常监控

```csharp
// 监控接收任务异常
_ = MonitorReceiveTaskAsync(_receiveTask);
```

新增 `MonitorReceiveTaskAsync()` 方法来监控接收任务的异常：

```csharp
/// <summary>
/// 监控接收任务的异常情况
/// </summary>
private async Task MonitorReceiveTaskAsync(Task receiveTask)
{
    try
    {
        await receiveTask;
    }
    catch (OperationCanceledException)
    {
        // 正常取消，不需要记录
        _logger?.LogDebug("消息接收任务已取消");
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, "消息接收任务发生未处理的异常");
        
        // 触发连接错误事件
        if (_isConnected)
        {
            _isConnected = false;
            _eventManager.TriggerConnectionEvent(WebSocketEventTrigger.ConnectionError, false,
                errorMessage: ex.Message, context: "Message receive task failed");
        }
    }
}
```

**功能**：
- 捕获接收任务中的未处理异常
- 区分正常取消和异常情况
- 触发适当的错误事件通知调用者
- 记录详细的日志信息

### 4. 优雅关闭机制

改进 `DisconnectAsync()` 方法，添加任务完成等待逻辑：

```csharp
// 取消接收任务
_cancellationTokenSource?.Cancel();

// 等待接收任务完成（设置超时避免无限等待）
if (_receiveTask != null && !_receiveTask.IsCompleted)
{
    try
    {
        _logger?.LogDebug("等待消息接收任务完成...");
        var completedTask = await Task.WhenAny(_receiveTask, Task.Delay(5000));
        
        if (completedTask == _receiveTask)
        {
            _logger?.LogDebug("消息接收任务已正常结束");
        }
        else
        {
            _logger?.LogWarning("消息接收任务在超时时间内未完成");
        }
    }
    catch (Exception ex)
    {
        _logger?.LogWarning(ex, "等待消息接收任务结束时出错");
    }
}
```

**关键特性**：
- 先取消任务，然后等待其完成
- 设置 5 秒超时，避免无限等待
- 使用 `Task.WhenAny()` 实现超时机制
- 记录详细的任务结束状态
- 在 `finally` 块中清理 `_receiveTask` 引用

## 改进效果

### 性能优化
- ✅ 不再占用线程池资源
- ✅ 为长时间运行的任务分配专用线程
- ✅ 提高了线程池的可用性

### 可靠性提升
- ✅ 完整的异常处理和监控
- ✅ 优雅的任务关闭机制
- ✅ 避免资源泄漏和幽灵任务
- ✅ 更好的错误事件通知

### 可维护性增强
- ✅ 可以跟踪任务状态
- ✅ 详细的日志记录
- ✅ 更清晰的生命周期管理
- ✅ 便于调试和问题排查

## 最佳实践总结

### 何时使用 TaskCreationOptions.LongRunning

**适用场景**：
- WebSocket 消息接收循环
- 服务器监听循环
- 长时间运行的后台处理任务
- 持续的监控或轮询任务

**不适用场景**：
- 短时间完成的任务
- CPU 密集型并行计算（应使用 `Parallel` 或 PLINQ）
- 异步 I/O 操作（应使用 async/await）

### 任务生命周期管理原则

1. **存储任务引用**：对于需要管理的后台任务，始终保存 `Task` 引用
2. **监控异常**：使用 continuation 或单独的监控任务来捕获异常
3. **优雅关闭**：在清理时等待任务完成，但设置合理的超时
4. **避免使用 `_ =`**：只有在确定任务不需要跟踪时才使用
5. **日志记录**：记录任务的启动、完成和异常状态

## 后续建议

1. **添加健康检查**：可以定期检查 `_receiveTask` 的状态，实现心跳机制
2. **重连机制**：当接收任务异常终止时，可以考虑自动重连
3. **性能监控**：可以记录接收任务的运行时间和消息处理速度
4. **资源限制**：可以添加消息队列大小限制，防止内存溢出

## 相关文件

- `src/Verdure.Assistant.Core/Services/Protocols/WebSocketClient.cs`

## 技术参考

- [Task.Factory.StartNew vs Task.Run](https://devblogs.microsoft.com/pfxteam/task-run-vs-task-factory-startnew/)
- [TaskCreationOptions.LongRunning](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcreationoptions)
- [WebSocket Best Practices](https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets.clientwebsocket)
