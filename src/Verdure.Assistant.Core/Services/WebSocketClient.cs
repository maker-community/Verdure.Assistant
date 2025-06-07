using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Verdure.Assistant.Core.Constants;
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
    private readonly IConfigurationService _configurationService;
    private readonly ILogger? _logger;
    private bool _isConnected;
    private string? _sessionId;
    private readonly TaskCompletionSource<bool> _helloReceived = new();

    // MCP相关字段
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _mcpPendingRequests = new();
    private int _mcpNextRequestId = 1;
    private bool _mcpInitialized = false;
    private McpIntegrationService? _mcpIntegrationService;

    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<ProtocolMessage>? ProtocolMessageReceived;
    public event EventHandler<byte[]>? AudioDataReceived;
    public event EventHandler<TtsMessage>? TtsStateChanged;
    public event EventHandler<MusicMessage>? MusicMessageReceived;
    public event EventHandler<SystemStatusMessage>? SystemStatusMessageReceived;
    public event EventHandler<LlmMessage>? LlmMessageReceived;
    public event EventHandler<McpMessage>? McpMessageReceived;
    public event EventHandler<EventArgs>? McpReadyForInitialization;

    // MCP事件
    public event EventHandler<string>? McpResponseReceived;
    public event EventHandler<Exception>? McpErrorOccurred;
    public bool IsConnected => _isConnected;
    public string? SessionId => _sessionId;

    // MCP属性
    public bool IsMcpInitialized => _mcpInitialized;

    public WebSocketClient(IConfigurationService configurationService, ILogger? logger = null)
    {
        _configurationService = configurationService;
        _logger = logger;
    }

    /// <summary>
    /// 设置MCP集成服务（可选，用于高级MCP功能）
    /// </summary>
    public void SetMcpIntegrationService(McpIntegrationService mcpIntegrationService)
    {
        _mcpIntegrationService = mcpIntegrationService;
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
                frameDuration: 60,
                supportMcp: true  // 声明支持MCP协议
            );

            await SendTextAsync(helloMessage);

            _logger?.LogDebug("已发送Hello消息: {Message}", helloMessage);
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

    /// <summary>
    /// 发送MCP工具列表请求
    /// </summary>
    /// <param name="id">请求ID</param>
    /// <param name="cursor">分页游标</param>
    public async Task SendMcpToolsListAsync(int id, string cursor = "")
    {
        var message = WebSocketProtocol.CreateMcpToolsListMessage(_sessionId, id, cursor);
        await SendTextAsync(message);
        _logger?.LogDebug("已发送MCP工具列表请求，ID: {Id}, Cursor: {Cursor}", id, cursor);
    }

    /// <summary>
    /// 发送MCP工具调用请求
    /// </summary>
    /// <param name="id">请求ID</param>
    /// <param name="toolName">工具名称</param>
    /// <param name="arguments">工具参数</param>
    public async Task SendMcpToolCallAsync(int id, string toolName, Dictionary<string, object>? arguments = null)
    {
        var message = WebSocketProtocol.CreateMcpToolCallMessage(_sessionId, id, toolName, arguments);
        await SendTextAsync(message);
        _logger?.LogDebug("已发送MCP工具调用请求，ID: {Id}, Tool: {ToolName}", id, toolName);
    }

    #endregion

    #region MCP Methods

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

            // 初始化MCP集成服务（如果有）
            if (_mcpIntegrationService != null)
            {
                await _mcpIntegrationService.InitializeAsync();
            }

            // 发送MCP初始化请求并等待响应
            var initRequestId = 1;
            var tcs = new TaskCompletionSource<string>();
            _mcpPendingRequests[initRequestId] = tcs;

            var capabilities = new
            {
                tools = new { },
                logging = new { }
            };

            try
            {
                await SendMcpInitializeAsync(initRequestId, capabilities);
                _logger?.LogDebug("Sent MCP initialize request with ID: {RequestId}", initRequestId);

                // 等待服务端初始化响应（设置超时）
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                cts.Token.Register(() => tcs.TrySetCanceled());

                var initResponse = await tcs.Task;
                _logger?.LogDebug("Received MCP initialize response: {Response}", initResponse);

                // 验证初始化响应
                var responseElement = JsonSerializer.Deserialize<JsonElement>(initResponse);
                if (responseElement.TryGetProperty("result", out var resultElement))
                {
                    _logger?.LogInformation("MCP initialization confirmed by server");

                    // 初始化成功后，自动获取工具列表
                    await LoadMcpToolsFromServerAsync();

                    _mcpInitialized = true;
                    _logger?.LogInformation("MCP WebSocket client initialized successfully");
                }
                else
                {
                    throw new InvalidOperationException("Server did not confirm MCP initialization");
                }
            }
            catch (Exception)
            {
                _mcpPendingRequests.TryRemove(initRequestId, out _);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize MCP WebSocket client");
            throw;
        }
    }

    /// <summary>
    /// 从服务端加载工具列表并注册到本地管理器
    /// </summary>
    private async Task LoadMcpToolsFromServerAsync()
    {
        try
        {
            _logger?.LogDebug("Loading tools from server");

            var toolsResponse = await GetMcpToolsListAsync();
            var responseElement = JsonSerializer.Deserialize<JsonElement>(toolsResponse);

            if (responseElement.TryGetProperty("result", out var resultElement) &&
                resultElement.TryGetProperty("tools", out var toolsElement))
            {
                var toolsArray = toolsElement.EnumerateArray();
                var registeredCount = 0;

                foreach (var toolElement in toolsArray)
                {
                    try
                    {
                        if (toolElement.TryGetProperty("name", out var nameElement) &&
                            toolElement.TryGetProperty("description", out var descElement))
                        {
                            var toolName = nameElement.GetString();
                            var toolDescription = descElement.GetString();

                            if (!string.IsNullOrEmpty(toolName) && !string.IsNullOrEmpty(toolDescription))
                            {
                                // 将工具注册到MCP集成服务中
                                if (_mcpIntegrationService != null)
                                {
                                    await _mcpIntegrationService.RegisterToolAsync(toolName, toolDescription, toolElement.ToString());
                                    registeredCount++;
                                    _logger?.LogDebug("Registered tool: {ToolName}", toolName);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to register tool from server response");
                    }
                }

                _logger?.LogInformation("Successfully registered {Count} tools from server", registeredCount);
            }
            else
            {
                _logger?.LogWarning("Server tools list response missing tools array");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load tools from server");
            // 不抛出异常，允许初始化继续，因为工具列表可能为空
        }
    }

    /// <summary>
    /// 获取设备工具列表
    /// </summary>
    /// <param name="cursor">分页游标</param>
    /// <returns>工具列表JSON响应</returns>
    public async Task<string> GetMcpToolsListAsync(string cursor = "")
    {
        if (!_mcpInitialized)
            throw new InvalidOperationException("MCP client not initialized");

        var requestId = GetNextMcpRequestId();
        var tcs = new TaskCompletionSource<string>();
        _mcpPendingRequests[requestId] = tcs;

        try
        {
            await SendMcpToolsListAsync(requestId, cursor);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            cts.Token.Register(() => tcs.TrySetCanceled());

            return await tcs.Task;
        }
        catch (Exception)
        {
            _mcpPendingRequests.TryRemove(requestId, out _);
            throw;
        }
    }

    /// <summary>
    /// 调用设备工具
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="arguments">工具参数</param>
    /// <returns>工具调用结果JSON响应</returns>
    public async Task<string> CallMcpToolAsync(string toolName, Dictionary<string, object>? arguments = null)
    {
        if (!_mcpInitialized)
            throw new InvalidOperationException("MCP client not initialized");

        var requestId = GetNextMcpRequestId();
        var tcs = new TaskCompletionSource<string>();
        _mcpPendingRequests[requestId] = tcs;

        try
        {
            await SendMcpToolCallAsync(requestId, toolName, arguments);
            _logger?.LogDebug("Sent MCP tool call request: {ToolName}", toolName);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            cts.Token.Register(() => tcs.TrySetCanceled());

            var response = await tcs.Task;

            // 处理工具调用响应并更新设备状态
            await ProcessMcpToolCallResponseAsync(toolName, response);

            return response;
        }
        catch (Exception)
        {
            _mcpPendingRequests.TryRemove(requestId, out _);
            throw;
        }
    }

    /// <summary>
    /// 处理工具调用响应并更新设备状态
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="response">服务端响应</param>
    private async Task ProcessMcpToolCallResponseAsync(string toolName, string response)
    {
        try
        {
            var responseElement = JsonSerializer.Deserialize<JsonElement>(response);

            if (responseElement.TryGetProperty("result", out var resultElement) &&
                resultElement.TryGetProperty("content", out var contentElement))
            {
                var contentArray = contentElement.EnumerateArray();
                foreach (var contentItem in contentArray)
                {
                    if (contentItem.TryGetProperty("text", out var textElement))
                    {
                        var resultText = textElement.GetString();
                        if (!string.IsNullOrEmpty(resultText))
                        {
                            await UpdateDeviceStateFromMcpResultAsync(toolName, resultText);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to process tool call response for {ToolName}", toolName);
        }
    }

    /// <summary>
    /// 根据工具调用结果更新设备状态
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="resultText">结果文本</param>
    private async Task UpdateDeviceStateFromMcpResultAsync(string toolName, string? resultText)
    {
        if (_mcpIntegrationService == null || string.IsNullOrEmpty(resultText))
            return;

        try
        {
            var deviceName = ExtractDeviceNameFromTool(toolName);
            if (string.IsNullOrEmpty(deviceName))
                return;            // 根据工具名称和结果更新状态
            if (toolName.Contains("turn_on"))
            {
                await _mcpIntegrationService.UpdateDeviceStateAsync(deviceName, "power", "on");
            }
            else if (toolName.Contains("turn_off"))
            {
                await _mcpIntegrationService.UpdateDeviceStateAsync(deviceName, "power", "off");
            }
            else if (toolName.Contains("set_brightness") || toolName.Contains("brightness"))
            {
                var brightness = ExtractBrightnessFromResult(resultText);
                if (brightness.HasValue)
                {
                    await _mcpIntegrationService.UpdateDeviceStateAsync(deviceName, "brightness", brightness.Value);
                }
            }

            _logger?.LogDebug("Updated device state for {DeviceName} based on tool {ToolName}", deviceName, toolName);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to update device state from tool result");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 从工具名称中提取设备名称
    /// </summary>
    private string? ExtractDeviceNameFromTool(string toolName)
    {
        // 例如：self.lamp.turn_on -> lamp
        var parts = toolName.Split('.');
        return parts.Length >= 2 ? parts[1] : null;
    }

    /// <summary>
    /// 从结果文本中提取亮度值
    /// </summary>
    private int? ExtractBrightnessFromResult(string resultText)
    {
        // 简单的亮度值提取逻辑
        var match = System.Text.RegularExpressions.Regex.Match(resultText, @"brightness.*?(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (match.Success && int.TryParse(match.Groups[1].Value, out var brightness))
        {
            return brightness;
        }

        return null;
    }

    /// <summary>
    /// 通过语音聊天系统执行设备功能（向后兼容）
    /// </summary>
    /// <param name="functionName">功能名称</param>
    /// <param name="parameters">功能参数</param>
    /// <returns>执行结果</returns>
    public async Task<string> ExecuteMcpFunctionAsync(string functionName, Dictionary<string, object>? parameters = null)
    {
        if (_mcpIntegrationService == null)
            throw new InvalidOperationException("MCP integration service not configured");

        return await _mcpIntegrationService.ExecuteFunctionAsync(functionName, parameters);
    }

    /// <summary>
    /// 获取设备状态信息
    /// </summary>
    /// <returns>设备状态字典</returns>
    public Dictionary<string, object> GetMcpDeviceStates()
    {
        return _mcpIntegrationService?.GetDeviceStates() ?? new Dictionary<string, object>();
    }

    /// <summary>
    /// 获取下一个MCP请求ID
    /// </summary>
    private int GetNextMcpRequestId()
    {
        return Interlocked.Increment(ref _mcpNextRequestId);
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

        // 检查设备是否支持MCP协议
        bool deviceSupportsMcp = false;
        if (message.Features != null && message.Features.TryGetValue("mcp", out var mcpValue))
        {
            deviceSupportsMcp = mcpValue is bool mcpBool && mcpBool;
        }

        _logger?.LogInformation("设备MCP支持状态: {McpSupported}", deviceSupportsMcp);

        // 设置Hello接收事件
        if (!_helloReceived.Task.IsCompleted)
        {
            _helloReceived.SetResult(true);
        }

        // 如果设备支持MCP，触发MCP准备就绪事件以便外部组件开始MCP初始化
        if (deviceSupportsMcp)
        {
            McpReadyForInitialization?.Invoke(this, EventArgs.Empty);
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
    }    /// <summary>
         /// 处理LLM消息
         /// </summary>
    private async Task HandleLlmMessageAsync(LlmMessage message)
    {
        _logger?.LogDebug("收到LLM消息，情感: {Emotion}", message.Emotion);
        LlmMessageReceived?.Invoke(this, message);
        await Task.CompletedTask;
    }    /// <summary>
         /// 处理音乐播放器消息
         /// </summary>
    private async Task HandleMusicMessageAsync(MusicMessage message)
    {
        _logger?.LogDebug("收到音乐消息，动作: {Action}，歌曲: {Song}", message.Action, message.SongName);

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

        MusicMessageReceived?.Invoke(this, message);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理系统状态消息
    /// </summary>
    private async Task HandleSystemStatusMessageAsync(SystemStatusMessage message)
    {
        _logger?.LogDebug("收到系统状态消息，组件: {Component}，状态: {Status}，消息: {Message}",
            message.Component, message.Status, message.Message);
        SystemStatusMessageReceived?.Invoke(this, message);
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
            // 尝试解析为JSON-RPC响应
            if (message?.Payload?.RootElement is JsonElement payloadElement)
            {
                // 成功响应
                if (payloadElement.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var requestId))
                {
                    // 成功响应
                    var responseJson = JsonSerializer.Serialize(message.Payload, JsonOptions);
                    _logger?.LogDebug("Resolved pending MCP request {RequestId}", requestId);

                    // 触发响应事件
                    McpResponseReceived?.Invoke(this, responseJson);
                }
                else
                {
                    // 这是一个通知消息
                    var notificationJson = JsonSerializer.Serialize(message.Payload, JsonOptions);
                    McpResponseReceived?.Invoke(this, notificationJson);
                    _logger?.LogDebug("Received MCP notification");
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing MCP message");
            McpErrorOccurred?.Invoke(this, ex);
        }
        await Task.CompletedTask;
    }

    #endregion

    public void Dispose()
    {
        DisconnectAsync().Wait();
    }
}
