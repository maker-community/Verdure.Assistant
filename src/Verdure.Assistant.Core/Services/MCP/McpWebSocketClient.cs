using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services.MCP;

namespace Verdure.Assistant.Core.Services.MCP;

/// <summary>
/// MCP WebSocket客户端 - 集成MCP协议与WebSocket通信
/// 对应xiaozhi-esp32中的MCP客户端功能，提供完整的MCP工具调用能力
/// </summary>
public class McpWebSocketClient : IDisposable
{    
    
    private readonly ILogger<McpWebSocketClient>? _logger;
    private readonly WebSocketClient _webSocketClient;
    private readonly McpIntegrationService _mcpIntegrationService;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pendingRequests = new();
    private int _nextRequestId = 1;
    private bool _isInitialized = false;
    private bool _disposed = false;

    public event EventHandler<string>? McpResponseReceived;
    public event EventHandler<Exception>? McpErrorOccurred;

    public bool IsConnected => _webSocketClient.IsConnected;
    public bool IsInitialized => _isInitialized;    
    public McpWebSocketClient(
        WebSocketClient webSocketClient,
        McpIntegrationService mcpIntegrationService,
        ILogger<McpWebSocketClient>? logger = null)
    {
        _webSocketClient = webSocketClient ?? throw new ArgumentNullException(nameof(webSocketClient));
        _mcpIntegrationService = mcpIntegrationService ?? throw new ArgumentNullException(nameof(mcpIntegrationService));
        _logger = logger;

        // 订阅MCP消息事件
        _webSocketClient.McpMessageReceived += OnMcpMessageReceived;
        
        // 订阅MCP准备就绪事件，当设备声明支持MCP时自动初始化
        _webSocketClient.McpReadyForInitialization += OnMcpReadyForInitialization;
    }

    /// <summary>
    /// 初始化MCP客户端
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        try
        {
            _logger?.LogInformation("Initializing MCP WebSocket client");

            // 确保WebSocket已连接
            if (!_webSocketClient.IsConnected)
            {
                throw new InvalidOperationException("WebSocket must be connected before initializing MCP client");
            }

            // 初始化MCP集成服务
            await _mcpIntegrationService.InitializeAsync();

            // 发送MCP初始化请求
            var initRequestId = GetNextRequestId();
            var capabilities = new
            {
                tools = new { }
            };

            await _webSocketClient.SendMcpInitializeAsync(initRequestId, capabilities);
            _logger?.LogDebug("Sent MCP initialize request with ID: {RequestId}", initRequestId);

            _isInitialized = true;
            _logger?.LogInformation("MCP WebSocket client initialized successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize MCP WebSocket client");
            throw;
        }
    }

    /// <summary>
    /// 获取设备工具列表
    /// </summary>
    /// <param name="cursor">分页游标</param>
    /// <returns>工具列表JSON响应</returns>
    public async Task<string> GetToolsListAsync(string cursor = "")
    {
        if (!_isInitialized)
            throw new InvalidOperationException("MCP client must be initialized first");

        var requestId = GetNextRequestId();
        var tcs = new TaskCompletionSource<string>();
        _pendingRequests[requestId] = tcs;

        try
        {
            await _webSocketClient.SendMcpToolsListAsync(requestId, cursor);
            _logger?.LogDebug("Sent MCP tools list request with ID: {RequestId}", requestId);

            // 等待响应（设置超时）
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            cts.Token.Register(() => tcs.TrySetCanceled());

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            _pendingRequests.TryRemove(requestId, out _);
            _logger?.LogError(ex, "Failed to get tools list");
            throw;
        }
    }

    /// <summary>
    /// 调用设备工具
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="arguments">工具参数</param>
    /// <returns>工具调用结果JSON响应</returns>
    public async Task<string> CallToolAsync(string toolName, Dictionary<string, object>? arguments = null)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("MCP client must be initialized first");

        var requestId = GetNextRequestId();
        var tcs = new TaskCompletionSource<string>();
        _pendingRequests[requestId] = tcs;

        try
        {
            await _webSocketClient.SendMcpToolCallAsync(requestId, toolName, arguments);
            _logger?.LogDebug("Sent MCP tool call request with ID: {RequestId}, Tool: {ToolName}", requestId, toolName);

            // 等待响应（设置超时）
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            cts.Token.Register(() => tcs.TrySetCanceled());

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            _pendingRequests.TryRemove(requestId, out _);
            _logger?.LogError(ex, "Failed to call tool: {ToolName}", toolName);
            throw;
        }
    }

    /// <summary>
    /// 通过语音聊天系统执行设备功能（向后兼容）
    /// </summary>
    /// <param name="functionName">功能名称</param>
    /// <param name="parameters">功能参数</param>
    /// <returns>执行结果</returns>
    public async Task<string> ExecuteFunctionAsync(string functionName, Dictionary<string, object>? parameters = null)
    {
        try
        {
            return await _mcpIntegrationService.ExecuteFunctionAsync(functionName, parameters);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to execute function: {FunctionName}", functionName);
            throw;
        }
    }

    /// <summary>
    /// 获取设备状态信息
    /// </summary>
    /// <returns>设备状态字典</returns>
    public Dictionary<string, object> GetDeviceStates()
    {
        return _mcpIntegrationService.GetDeviceStates();
    }

    /// <summary>
    /// 处理接收到的MCP消息
    /// </summary>
    private void OnMcpMessageReceived(object? sender, McpMessage message)
    {
        try
        {
            _logger?.LogDebug("Received MCP message: {Payload}", JsonSerializer.Serialize(message.Payload));            // 尝试解析为JSON-RPC响应
            if (message.Payload is JsonElement payloadElement)
            {
                if (payloadElement.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var requestId))
                {
                    // 这是一个响应消息
                    if (_pendingRequests.TryRemove(requestId, out var tcs))
                    {
                        // 检查是否是错误响应
                        if (payloadElement.TryGetProperty("error", out var errorElement))
                        {
                            var errorMessage = "MCP Error";
                            if (errorElement.TryGetProperty("message", out var errorMessageElement))
                            {
                                errorMessage = errorMessageElement.GetString() ?? errorMessage;
                            }
                            
                            var exception = new Exception($"MCP JSON-RPC Error: {errorMessage}");
                            tcs.SetException(exception);
                            _logger?.LogError("MCP request {RequestId} failed with error: {Error}", requestId, errorMessage);
                        }
                        else
                        {
                            // 成功响应
                            var responseJson = JsonSerializer.Serialize(message.Payload);
                            tcs.SetResult(responseJson);
                            _logger?.LogDebug("Resolved pending request {RequestId}", requestId);
                        }
                    }
                }
                else
                {
                    // 这是一个通知消息
                    var notificationJson = JsonSerializer.Serialize(message.Payload);
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
    }

    /// <summary>
    /// 处理MCP准备就绪事件，当设备声明支持MCP时自动初始化MCP会话
    /// </summary>
    private async void OnMcpReadyForInitialization(object? sender, EventArgs e)
    {
        try
        {
            _logger?.LogInformation("Device declared MCP support, starting MCP initialization");
            await InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to auto-initialize MCP after device declared support");
            McpErrorOccurred?.Invoke(this, ex);
        }
    }

    /// <summary>
    /// 获取下一个请求ID
    /// </summary>
    private int GetNextRequestId()
    {
        return Interlocked.Increment(ref _nextRequestId);
    }

    #region IDisposable Support

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 取消所有挂起的请求
                foreach (var tcs in _pendingRequests.Values)
                {
                    tcs.TrySetCanceled();
                }
                _pendingRequests.Clear();                
                
                // 取消订阅事件
                if (_webSocketClient != null)
                {
                    _webSocketClient.McpMessageReceived -= OnMcpMessageReceived;
                    _webSocketClient.McpReadyForInitialization -= OnMcpReadyForInitialization;
                }

                _mcpIntegrationService?.Dispose();
            }

            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion
}
