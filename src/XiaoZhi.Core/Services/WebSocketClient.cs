using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XiaoZhi.Core.Constants;
using XiaoZhi.Core.Interfaces;
using XiaoZhi.Core.Models;

namespace XiaoZhi.Core.Services;

/// <summary>
/// WebSocket通信客户端实现
/// 支持完整的WebSocket协议消息处理
/// </summary>
public class WebSocketClient : ICommunicationClient, IDisposable
{
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger? _logger;
    private bool _isConnected;
    private string? _sessionId;
    private readonly TaskCompletionSource<bool> _helloReceived = new();    
    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<ProtocolMessage>? ProtocolMessageReceived;
    public event EventHandler<byte[]>? AudioDataReceived;
    public event EventHandler<TtsMessage>? TtsStateChanged;

    public bool IsConnected => _isConnected;
    public string? SessionId => _sessionId;

    public WebSocketClient(IConfigurationService configurationService, ILogger? logger = null)
    {
        _configurationService = configurationService;
        _logger = logger;
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
            ConnectionStateChanged?.Invoke(this, true);

            _logger?.LogInformation("WebSocket连接已建立，正在发送Hello消息");

            // 开始接收消息
            _ = Task.Run(ReceiveMessagesAsync);

            // 发送客户端Hello消息
            var helloMessage = WebSocketProtocol.CreateHelloMessage(
                sessionId: null,
                sampleRate: 16000,
                channels: 1,
                frameDuration: 60
            );
            
            await SendTextAsync(helloMessage);

            _logger?.LogDebug("已发送Hello消息: {Message}", helloMessage);

            // 等待服务器Hello响应
            //try
            //{
            //    await _helloReceived.Task.WaitAsync(TimeSpan.FromSeconds(10), _cancellationTokenSource.Token);
 
            //    _logger?.LogInformation("WebSocket连接成功建立，会话ID: {SessionId}", _sessionId);
            //}
            //catch (TimeoutException)
            //{
            //    _logger?.LogError("等待服务器Hello响应超时");
            //    await DisconnectAsync();
            //    throw new TimeoutException("等待服务器Hello响应超时");
            //}
        }
        catch (Exception ex)
        {
            _isConnected = false;
            ConnectionStateChanged?.Invoke(this, false);
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

            _cancellationTokenSource?.Cancel();
            
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
            ConnectionStateChanged?.Invoke(this, false);
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
            throw new InvalidOperationException("WebSocket未连接");

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
    /// 发送音频数据
    /// </summary>
    /// <param name="audioData">音频数据</param>
    public async Task SendAudioAsync(byte[] audioData)
    {
        if (!_isConnected || _webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket未连接");

        try
        {
            await _webSocket.SendAsync(new ArraySegment<byte>(audioData), WebSocketMessageType.Binary, true, CancellationToken.None);
            _logger?.LogDebug("已发送WebSocket音频数据，长度: {Length}", audioData.Length);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "发送WebSocket音频数据失败");
            throw;
        }
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
    /// 发送IoT设备描述消息
    /// </summary>
    /// <param name="descriptors">设备描述</param>
    public async Task SendIotDescriptorsAsync(object descriptors)
    {
        var message = WebSocketProtocol.CreateIotDescriptorsMessage(descriptors, _sessionId);
        await SendTextAsync(message);
        _logger?.LogDebug("已发送IoT设备描述消息");
    }

    /// <summary>
    /// 发送IoT设备状态消息
    /// </summary>
    /// <param name="states">设备状态</param>
    public async Task SendIotStatesAsync(object states)
    {
        var message = WebSocketProtocol.CreateIotStatesMessage(states, _sessionId);
        await SendTextAsync(message);
        _logger?.LogDebug("已发送IoT设备状态消息");
    }

    #endregion

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
                    
                    AudioDataReceived?.Invoke(this, audioData);
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
                ConnectionStateChanged?.Invoke(this, false);
            }
        }
        finally
        {
            if (_isConnected)
            {
                _isConnected = false;
                ConnectionStateChanged?.Invoke(this, false);
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
                ProtocolMessageReceived?.Invoke(this, protocolMessage);
                return;
            }

            // 如果不是协议消息，尝试解析为ChatMessage
            var chatMessage = JsonSerializer.Deserialize<ChatMessage>(json);
            if (chatMessage != null)
            {
                MessageReceived?.Invoke(this, chatMessage);
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

        _logger?.LogInformation("Hello握手完成，会话ID: {SessionId}", _sessionId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理Goodbye消息
    /// </summary>
    private async Task HandleGoodbyeMessageAsync(GoodbyeMessage message)
    {
        _logger?.LogInformation("收到服务器Goodbye消息，准备断开连接");
        await DisconnectAsync();
    }    /// <summary>
    /// 处理TTS消息
    /// </summary>
    private async Task HandleTtsMessageAsync(TtsMessage message)
    {
        _logger?.LogDebug("收到TTS消息，状态: {State}，文本: {Text}", message.State, message.Text);
        
        // 触发TTS状态变化事件
        TtsStateChanged?.Invoke(this, message);
        
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
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        DisconnectAsync().Wait();
    }
}
