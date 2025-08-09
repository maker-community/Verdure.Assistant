# WebSocket事件系统统一优化总结

## 优化目标
参考ConversationStateMachine的状态事件设计，统一WebSocketClient中分散的事件处理逻辑，简化VoiceChatService和HomePageViewModel中的事件处理代码。

## 主要改进

### 1. 创建统一的事件系统
- **新增文件**: `WebSocketEvents.cs` - 定义统一的WebSocket事件类型和参数
- **新增文件**: `WebSocketEventManager.cs` - 统一管理所有WebSocket相关事件

### 2. 事件触发器枚举
```csharp
public enum WebSocketEventTrigger
{
    // 连接事件
    ConnectionEstablished, ConnectionLost, ConnectionError, HelloReceived, GoodbyeReceived,
    
    // 消息事件
    TextMessageReceived, AudioDataReceived, ProtocolMessageReceived,
    
    // TTS事件
    TtsStarted, TtsStopped, TtsSentenceStarted, TtsSentenceEnded,
    
    // 音乐事件
    MusicPlay, MusicPause, MusicStop, MusicLyricUpdate, MusicSeek,
    
    // 系统状态和LLM事件
    SystemStatusUpdate, LlmEmotionUpdate,
    
    // MCP事件
    McpReadyForInitialization, McpInitialized, McpToolsListRequest,
    McpToolCallRequest, McpResponseReceived, McpError
}
```

### 3. WebSocketClient重构
- 保留原有事件接口（标记为Obsolete），确保向后兼容
- 新增统一事件：`WebSocketEventOccurred`
- 所有内部事件处理都通过`WebSocketEventManager`统一分发
- 根据不同的消息类型和状态自动触发对应的事件触发器

### 4. VoiceChatService简化
- 移除分散的WebSocket事件处理方法（如`OnTtsStateChanged`、`OnMusicMessageReceived`等）
- 新增统一的事件处理方法：`OnWebSocketEventOccurred`
- 通过switch语句根据事件触发器类型分发到具体的处理方法
- 保持对外事件接口不变，确保上层组件（如HomePageViewModel）无需修改

### 5. 处理逻辑分离
将原来的巨大事件处理方法拆分为小的专门方法：
- `HandleTtsStarted/Stopped/SentenceStarted/SentenceEnded` - TTS事件处理
- `HandleAudioDataReceived` - 音频数据处理
- `HandleMusicEvent` - 音乐事件处理
- `HandleSystemStatusEvent` - 系统状态处理
- `HandleLlmEmotionEvent` - LLM情感处理
- `HandleMcpXXX` - MCP相关事件处理

## 架构优势

### 1. 集中化管理
- 所有WebSocket事件都通过`WebSocketEventManager`统一分发
- 事件类型和参数标准化，便于维护和扩展

### 2. 类型安全
- 使用强类型的事件参数类（如`TtsEventArgs`、`MusicEventArgs`等）
- 编译时检查，减少运行时错误

### 3. 可扩展性
- 新增事件类型只需在枚举中添加新值
- 新增事件参数只需继承`WebSocketEventArgs`基类

### 4. 向后兼容
- 原有事件接口保持不变，标记为Obsolete
- 上层组件（VoiceChatService、HomePageViewModel）无需修改
- 渐进式迁移，可以逐步切换到新的事件系统

### 5. 调试友好
- 统一的事件日志记录
- 清晰的事件流追踪
- 上下文信息完整

## 代码复用性改进

### 原始架构问题
```csharp
// WebSocketClient中分散的事件
public event EventHandler<TtsMessage>? TtsStateChanged;
public event EventHandler<MusicMessage>? MusicMessageReceived;
public event EventHandler<SystemStatusMessage>? SystemStatusMessageReceived;
// ... 还有更多

// VoiceChatService中分散的处理方法
private void OnTtsStateChanged(object? sender, TtsMessage message) { ... }
private void OnMusicMessageReceived(object? sender, MusicMessage message) { ... }
private void OnSystemStatusMessageReceived(object? sender, SystemStatusMessage message) { ... }
// ... 还有更多

// HomePageViewModel中重复的事件订阅
wsClient.TtsStateChanged += OnTtsStateChanged;
wsClient.MusicMessageReceived += OnMusicMessageReceived;
wsClient.SystemStatusMessageReceived += OnSystemStatusMessageReceived;
// ... 还有更多
```

### 优化后的架构
```csharp
// WebSocketClient中统一的事件
public event EventHandler<WebSocketEventArgs>? WebSocketEventOccurred;

// VoiceChatService中统一的处理
private void OnWebSocketEventOccurred(object? sender, WebSocketEventArgs e)
{
    switch (e.Trigger) {
        case WebSocketEventTrigger.TtsStarted: HandleTtsStarted((TtsEventArgs)e); break;
        case WebSocketEventTrigger.MusicPlay: HandleMusicEvent((MusicEventArgs)e); break;
        // ... 统一分发
    }
}

// HomePageViewModel中简化的订阅
wsClient.WebSocketEventOccurred += OnWebSocketEventOccurred;
```

## 性能优化
- 减少事件订阅/取消订阅的开销
- 统一的事件分发减少了重复的类型检查
- 更好的内存管理（减少委托链）

## 未来扩展建议
1. 可以考虑使用事件总线模式进一步解耦
2. 可以添加事件过滤器功能
3. 可以实现事件重放功能用于调试
4. 可以添加事件度量和监控

## 兼容性说明
- 现有代码无需修改即可正常工作
- 建议逐步迁移到新的事件系统
- 旧的事件接口在未来版本中会被移除

这次优化成功简化了代码架构，提高了可维护性，同时保持了完全的向后兼容性。
