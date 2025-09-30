using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Events;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services.MCP;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// WebSocket通信客户端实现
/// 支持完整的WebSocket协议消息处理和MCP协议集成
/// 基于xiaozhi-esp32的MCP协议实现，提供统一的WebSocket+MCP通信能力
/// </summary>
public class WebSocketClient : ICommunicationClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _receiveTask;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger? _logger;
    private bool _isConnected;
    private string? _sessionId;
    private readonly TaskCompletionSource<bool> _helloReceived = new();

    private bool _mcpInitialized = false;
    // 统一事件管理器
    private readonly WebSocketEventManager _eventManager;
    
    // Channel 优化的音频处理器
    private WebSocketAudioHandler? _audioHandler;

    /// <summary>
    /// 新的统一WebSocket事件 - 推荐使用
    /// </summary>
    public event EventHandler<WebSocketEventArgs>? WebSocketEventOccurred
    {
        add => _eventManager.WebSocketEventOccurred += value;
        remove => _eventManager.WebSocketEventOccurred -= value;
    }
    public bool IsConnected => _isConnected;
    public string? SessionId => _sessionId;

    // MCP属性
    public bool IsMcpInitialized => _mcpInitialized;

    public WebSocketClient(IConfigurationService configurationService, ILogger? logger = null)
    {
        _configurationService = configurationService;
        _logger = logger;
        _eventManager = new WebSocketEventManager();
        
        // 初始化 Channel 优化的音频处理器
        _audioHandler = new WebSocketAudioHandler(SendAudioDirectAsync, logger);
        _audioHandler.AudioDataReceived += OnAudioDataReceived;
    }

    public async Task ConnectAsync()
    {
        if (_isConnected) return;

        try
        {
            _webSocket = new ClientWebSocket();

            // 设置WebSocket头部，参考Python实现
            var accessToken = "test"; // TODO: 从配置获取真实的访问令牌
            _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            _webSocket.Options.SetRequestHeader("Protocol-Version", "1");
            _webSocket.Options.SetRequestHeader("Device-Id", _configurationService.DeviceId);
            _webSocket.Options.SetRequestHeader("Client-Id", _configurationService.ClientId);

            _cancellationTokenSource = new CancellationTokenSource();

            // 连接WebSocket
            var websocketUrl = _configurationService.WebSocketUrl;
            await _webSocket.ConnectAsync(new Uri(websocketUrl), _cancellationTokenSource.Token);
            _isConnected = true;

            // 触发连接建立事件
            _eventManager.TriggerConnectionEvent(WebSocketEventTrigger.ConnectionEstablished, true, context: "WebSocket connected");

            _logger?.LogInformation("WebSocket连接已建立，正在发送Hello消息");

            // 开始接收消息 - 使用 LongRunning 任务避免占用线程池
            _receiveTask = Task.Factory.StartNew(
                ReceiveMessagesAsync,
                _cancellationTokenSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            ).Unwrap();

            // 监控接收任务异常
            _ = MonitorReceiveTaskAsync(_receiveTask);

            // 发送客户端Hello消息
            var helloMessage = WebSocketProtocol.CreateHelloMessage(
                sessionId: null,
                sampleRate: 16000,
                channels: 1,
                frameDuration: 60,
                supportMcp: true  // 声明支持MCP协议
            );

            await SendTextAsync(helloMessage);

            _logger?.LogDebug("已发送Hello消息: {Message}", helloMessage);
        }
        catch (Exception ex)
        {
            _isConnected = false;

            // 触发连接错误事件
            _eventManager.TriggerConnectionEvent(WebSocketEventTrigger.ConnectionError, false,
                errorMessage: ex.Message, context: "WebSocket connection failed");

            _logger?.LogError(ex, "连接WebSocket失败");
            throw new Exception($"连接WebSocket失败: {ex.Message}", ex);
        }
    }

    public async Task DisconnectAsync()
    {
        if (!_isConnected) return;

        try
        {
            // 发送goodbye消息
            if (_webSocket?.State == WebSocketState.Open && !string.IsNullOrEmpty(_sessionId))
            {
                var goodbyeMessage = WebSocketProtocol.CreateGoodbyeMessage(_sessionId);
                await SendTextAsync(goodbyeMessage);
                _logger?.LogDebug("已发送Goodbye消息");
            }

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

            // 关闭 WebSocket 连接
            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "断开WebSocket连接时出错");
        }
        finally
        {
            _isConnected = false;
            _sessionId = null;
            _receiveTask = null;
            //ConnectionStateChanged?.Invoke(this, false);
            _webSocket?.Dispose();
            _cancellationTokenSource?.Dispose();
            _logger?.LogInformation("WebSocket连接已断开");
        }
    }

    public async Task SendMessageAsync(ChatMessage message)
    {
        if (!_isConnected || _webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket未连接");

        var json = JsonSerializer.Serialize(message);
        await SendTextAsync(json);
    }

    public async Task SendVoiceAsync(VoiceMessage voiceMessage)
    {
        if (!_isConnected || _webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket未连接");

        await SendAudioAsync(voiceMessage.Data);
    }

    /// <summary>
    /// 发送文本消息
    /// </summary>
    /// <param name="message">消息内容</param>
    public async Task SendTextAsync(string message)
    {
        if (!_isConnected || _webSocket?.State != WebSocketState.Open)
        {
            _logger?.LogWarning("WebSocket未连接");
            return;
        }
            //throw new InvalidOperationException("WebSocket未连接");

        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            _logger?.LogDebug("已发送WebSocket文本消息，长度: {Length}", bytes.Length);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "发送WebSocket文本消息失败");
            throw;
        }
    }

    /// <summary>
    /// 发送音频数据 - 使用 Channel 优化缓冲
    /// </summary>
    /// <param name="audioData">音频数据</param>
    public async Task SendAudioAsync(byte[] audioData)
    {
        if (_audioHandler == null) 
        {
            _logger?.LogWarning("音频处理器未初始化");
            return;
        }

        // 使用 Channel 缓冲发送
        if (!await _audioHandler.QueueAudioForSendingAsync(audioData))
        {
            _logger?.LogWarning("音频发送队列已满，丢弃数据");
        }
    }

    /// <summary>
    /// 直接发送音频数据到 WebSocket（由 Channel 处理器调用）
    /// </summary>
    private async Task SendAudioDirectAsync(byte[] audioData, CancellationToken cancellationToken)
    {
        if (!_isConnected || _webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket未连接");

        await _webSocket.SendAsync(new ArraySegment<byte>(audioData), WebSocketMessageType.Binary, true, cancellationToken);
    }

    /// <summary>
    /// 处理接收到的音频数据事件
    /// </summary>
    private void OnAudioDataReceived(object? sender, byte[] audioData)
    {
        _eventManager.TriggerMessageEvent(WebSocketEventTrigger.AudioDataReceived,
            audioData: audioData, context: $"Audio data: {audioData.Length} bytes");
    }

    #region Protocol Message Methods

    /// <summary>
    /// 发送开始监听消息
    /// </summary>
    /// <param name="mode">监听模式</param>
    public async Task SendStartListenAsync(ListeningMode mode)
    {
        var message = WebSocketProtocol.CreateStartListenMessage(_sessionId, mode);
        await SendTextAsync(message);
        _logger?.LogDebug("已发送开始监听消息，模式: {Mode}", mode);
    }

    /// <summary>
    /// 发送停止监听消息
    /// </summary>
    public async Task SendStopListenAsync()
    {
        var message = WebSocketProtocol.CreateStopListenMessage(_sessionId);
        await SendTextAsync(message);
        _logger?.LogDebug("已发送停止监听消息");
    }

    /// <summary>
    /// 发送唤醒词检测消息
    /// </summary>
    /// <param name="wakeWord">检测到的唤醒词</param>
    public async Task SendWakeWordDetectedAsync(string wakeWord)
    {
        var message = WebSocketProtocol.CreateWakeWordDetectedMessage(wakeWord, _sessionId);
        await SendTextAsync(message);
        _logger?.LogDebug("已发送唤醒词检测消息: {WakeWord}", wakeWord);
    }

    /// <summary>
    /// 发送中止消息
    /// </summary>
    /// <param name="reason">中止原因</param>
    public async Task SendAbortAsync(AbortReason reason)
    {
        var message = WebSocketProtocol.CreateAbortMessage(reason, _sessionId);
        await SendTextAsync(message);
        _logger?.LogDebug("已发送中止消息，原因: {Reason}", reason);
    }

    /// <summary>
    /// 发送MCP消息
    /// 对应xiaozhi-esp32的SendMcpMessage方法
    /// </summary>
    /// <param name="payload">MCP JSON-RPC负载</param>
    public async Task SendMcpMessageAsync(JsonDocument payload)
    {
        var message = WebSocketProtocol.CreateMcpMessage(_sessionId, payload);
        await SendTextAsync(message);
        _logger?.LogDebug("已发送MCP消息");
    }

    /// <summary>
    /// 发送MCP初始化请求
    /// </summary>
    /// <param name="id">请求ID</param>
    /// <param name="capabilities">客户端能力</param>
    public async Task SendMcpInitializeAsync(int id, object? capabilities = null)
    {
        var message = WebSocketProtocol.CreateMcpInitializeMessage(_sessionId, id, capabilities);
        await SendTextAsync(message);
        _logger?.LogDebug("已发送MCP初始化请求，ID: {Id}", id);
    }

    /// <summary>
    /// 发送MCP工具列表响应
    /// </summary>
    /// <param name="id">请求ID</param>
    /// <param name="cursor">分页游标</param>
    public async Task SendMcpToolsListResponseAsync(int id, List<SimpleMcpTool> mcpTools, string? nextCursor = "")
    {
        var message = WebSocketProtocol.CreateMcpToolsListResponseMessage(_sessionId, id, mcpTools, nextCursor);
        await SendTextAsync(message);
        _logger?.LogDebug("已发送MCP工具列表请求，ID: {Id}, nextCursor: {nextCursor}", id, nextCursor);
    }

    #endregion

    #region MCP Methods

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

    /// <summary>
    /// 初始化MCP客户端
    /// 对应xiaozhi-esp32的MCP初始化流程
    /// </summary>
    public async Task InitializeMcpAsync()
    {
        if (_mcpInitialized)
            return;

        try
        {
            _logger?.LogInformation("Initializing MCP client");

            // 确保WebSocket已连接
            if (!_isConnected)
            {
                throw new InvalidOperationException("WebSocket must be connected before initializing MCP client");
            }

            // 发送MCP初始化请求并等待响应
            var initRequestId = 1;

            var capabilities = new
            {
                tools = new { },
                logging = new { }
            };

            try
            {
                await SendMcpInitializeAsync(initRequestId, capabilities);
                _logger?.LogDebug("Sent MCP initialize request with ID: {RequestId}", initRequestId);

                _mcpInitialized = true;
            }
            catch (Exception)
            {
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize MCP WebSocket client");
            throw;
        }
    }



    private async Task ReceiveMessagesAsync()
    {
        var buffer = new byte[8192];

        try
        {
            while (_isConnected && _webSocket?.State == WebSocketState.Open && !_cancellationTokenSource!.Token.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    _logger?.LogDebug("收到WebSocket文本消息: {Message}", json);

                    await HandleTextMessageAsync(json);
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    var audioData = new byte[result.Count];
                    Array.Copy(buffer, audioData, result.Count);
                    _logger?.LogDebug("收到WebSocket音频数据，长度: {Length}", audioData.Length);

                    // 使用 Channel 优化的音频处理器处理接收到的音频数据
                    _audioHandler?.TryProcessReceivedAudio(audioData);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger?.LogInformation("服务器关闭了WebSocket连接");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消操作
            _logger?.LogDebug("WebSocket接收循环被取消");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "接收WebSocket消息时出错");
            if (_isConnected)
            {
                _isConnected = false;
            }
        }
        finally
        {
            if (_isConnected)
            {
                _isConnected = false;

                // 触发连接丢失事件
                _eventManager.TriggerConnectionEvent(WebSocketEventTrigger.ConnectionLost, false,
                    context: "WebSocket receive loop terminated");
            }
        }
    }

    /// <summary>
    /// 处理接收到的文本消息
    /// </summary>
    /// <param name="json">JSON消息</param>
    private async Task HandleTextMessageAsync(string json)
    {
        try
        {
            // 首先尝试解析为协议消息
            var protocolMessage = WebSocketProtocol.ParseMessage(json);
            if (protocolMessage != null)
            {
                await HandleProtocolMessageAsync(protocolMessage);

                // 触发协议消息事件
                _eventManager.TriggerMessageEvent(WebSocketEventTrigger.ProtocolMessageReceived,
                    protocolMessage: protocolMessage, context: $"Protocol message: {protocolMessage.Type}");
                return;
            }

            // 如果不是协议消息，尝试解析为ChatMessage
            var chatMessage = JsonSerializer.Deserialize<ChatMessage>(json);
            if (chatMessage != null)
            {
                // 触发文本消息事件
                _eventManager.TriggerMessageEvent(WebSocketEventTrigger.TextMessageReceived,
                    chatMessage: chatMessage, context: $"Chat message: {chatMessage.Type}");
            }
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "解析JSON消息失败: {Message}", json);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理文本消息时出错");
        }
    }

    /// <summary>
    /// 处理协议消息
    /// </summary>
    /// <param name="message">协议消息</param>
    private async Task HandleProtocolMessageAsync(ProtocolMessage message)
    {
        _logger?.LogDebug("处理协议消息，类型: {Type}", message.Type);

        switch (message.Type.ToLowerInvariant())
        {
            case "hello":
                await HandleHelloMessageAsync((HelloMessage)message);
                break;

            case "goodbye":
                await HandleGoodbyeMessageAsync((GoodbyeMessage)message);
                break;

            case "tts":
                await HandleTtsMessageAsync((TtsMessage)message);
                break;

            case "listen":
                await HandleListenMessageAsync((ListenMessage)message);
                break;

            case "llm":
                await HandleLlmMessageAsync((LlmMessage)message);
                break;

            case "music":
                await HandleMusicMessageAsync((MusicMessage)message);
                break;

            case "system_status":
                await HandleSystemStatusMessageAsync((SystemStatusMessage)message);
                break;
            case "mcp":
                await HandleMcpMessageAsync((McpMessage)message);
                break;
            default:
                _logger?.LogDebug("收到未知类型的协议消息: {Type}", message.Type);
                break;
        }
    }

    /// <summary>
    /// 处理Hello消息
    /// </summary>
    private async Task HandleHelloMessageAsync(HelloMessage message)
    {
        _logger?.LogInformation("收到服务器Hello消息，传输方式: {Transport}", message.Transport);

        // 验证传输方式
        if (message.Transport != "websocket")
        {
            _logger?.LogError("不支持的传输方式: {Transport}", message.Transport);
            return;
        }

        // 设置会话ID
        _sessionId = message.SessionId;

        // 设置Hello接收事件
        if (!_helloReceived.Task.IsCompleted)
        {
            _helloReceived.SetResult(true);
        }

        // 触发Hello接收事件
        _eventManager.TriggerConnectionEvent(WebSocketEventTrigger.HelloReceived, true,
            sessionId: _sessionId, context: $"Hello received from device, MCP support");

        _logger?.LogInformation("Hello握手完成，会话ID: {SessionId}", _sessionId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理Goodbye消息
    /// </summary>
    private async Task HandleGoodbyeMessageAsync(GoodbyeMessage message)
    {
        _logger?.LogInformation("收到服务器Goodbye消息，准备断开连接");

        // 触发Goodbye接收事件
        _eventManager.TriggerConnectionEvent(WebSocketEventTrigger.GoodbyeReceived, false,
            sessionId: _sessionId, context: "Goodbye received from server");

        await DisconnectAsync();
    }    /// <summary>
         /// 处理TTS消息
         /// </summary>
    private async Task HandleTtsMessageAsync(TtsMessage message)
    {
        _logger?.LogDebug("收到TTS消息，状态: {State}，文本: {Text}", message.State, message.Text);

        // 根据TTS状态触发对应事件
        var trigger = message.State?.ToLowerInvariant() switch
        {
            "start" => WebSocketEventTrigger.TtsStarted,
            "stop" => WebSocketEventTrigger.TtsStopped,
            "sentence_start" => WebSocketEventTrigger.TtsSentenceStarted,
            "sentence_end" => WebSocketEventTrigger.TtsSentenceEnded,
            _ => WebSocketEventTrigger.TtsStarted // 默认
        };

        // 触发TTS事件
        _eventManager.TriggerTtsEvent(trigger, message, message.State, message.Text,
            context: $"TTS state: {message.State}");

        // 可以在这里添加TTS状态处理逻辑
        switch (message.State?.ToLowerInvariant())
        {
            case "start":
                _logger?.LogDebug("TTS开始播放");
                break;
            case "stop":
                _logger?.LogDebug("TTS停止播放");
                break;
            case "sentence_start":
                _logger?.LogDebug("TTS句子开始: {Text}", message.Text);
                break;
            case "sentence_end":
                _logger?.LogDebug("TTS句子结束");
                break;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理Listen消息
    /// </summary>
    private async Task HandleListenMessageAsync(ListenMessage message)
    {
        _logger?.LogDebug("收到Listen消息，状态: {State}，模式: {Mode}，文本: {Text}",
            message.State, message.Mode, message.Text);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理LLM消息
    /// </summary>
    private async Task HandleLlmMessageAsync(LlmMessage message)
    {
        _logger?.LogDebug("收到LLM消息，情感: {Emotion}", message.Emotion);

        // 触发LLM情感事件
        _eventManager.TriggerLlmEmotionEvent(WebSocketEventTrigger.LlmEmotionUpdate,
            message, message.Emotion, context: $"LLM emotion: {message.Emotion}");

        await Task.CompletedTask;
    }    
    
    /// <summary>
    /// 处理音乐播放器消息
    /// </summary>
    private async Task HandleMusicMessageAsync(MusicMessage message)
    {
        _logger?.LogDebug("收到音乐消息，动作: {Action}，歌曲: {Song}", message.Action, message.SongName);

        // 根据音乐动作触发对应事件
        var trigger = message.Action?.ToLowerInvariant() switch
        {
            "play" => WebSocketEventTrigger.MusicPlay,
            "pause" => WebSocketEventTrigger.MusicPause,
            "stop" => WebSocketEventTrigger.MusicStop,
            "lyric_update" => WebSocketEventTrigger.MusicLyricUpdate,
            "seek" => WebSocketEventTrigger.MusicSeek,
            _ => WebSocketEventTrigger.MusicPlay // 默认
        };

        // 触发音乐事件
        _eventManager.TriggerMusicEvent(trigger, message, message.Action, message.SongName,
            message.Artist, message.LyricText, message.Position, message.Duration,
            context: $"Music action: {message.Action}");

        // 根据不同的音乐动作进行处理
        switch (message.Action?.ToLowerInvariant())
        {
            case "play":
                _logger?.LogInformation("开始播放音乐: {Song} - {Artist}", message.SongName, message.Artist);
                break;
            case "pause":
                _logger?.LogInformation("音乐暂停");
                break;
            case "stop":
                _logger?.LogInformation("音乐停止");
                break;
            case "lyric_update":
                _logger?.LogDebug("歌词更新: {Lyric}", message.LyricText);
                break;
            case "seek":
                _logger?.LogDebug("音乐跳转到: {Position}/{Duration}", message.Position, message.Duration);
                break;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理系统状态消息
    /// </summary>
    private async Task HandleSystemStatusMessageAsync(SystemStatusMessage message)
    {
        _logger?.LogDebug("收到系统状态消息，组件: {Component}，状态: {Status}，消息: {Message}",
            message.Component, message.Status, message.Message);

        // 触发系统状态事件
        _eventManager.TriggerSystemStatusEvent(WebSocketEventTrigger.SystemStatusUpdate,
            message, message.Component, message.Status, message.Message,
            context: $"System status: {message.Component} -> {message.Status}");

        await Task.CompletedTask;
    }
    /// <summary>
    /// 处理MCP消息
    /// </summary>
    private async Task HandleMcpMessageAsync(McpMessage message)
    {
        _logger?.LogDebug("收到MCP消息，会话ID: {SessionId}", message.SessionId);

        try
        {
            // 成功响应
            var responseJson = JsonSerializer.Serialize(message.Payload, JsonOptions);

            // 触发MCP响应事件
            _eventManager.TriggerMcpEvent(WebSocketEventTrigger.McpResponseReceived,
                message, responseJson, context: "MCP message received");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing MCP message");

            // 触发MCP错误事件
            _eventManager.TriggerMcpEvent(WebSocketEventTrigger.McpError,
                message, error: ex, context: "MCP message processing error");
        }
        await Task.CompletedTask;
    }

    #endregion

    public void Dispose()
    {
        DisconnectAsync().Wait();
        _audioHandler?.Dispose();
    }
}
