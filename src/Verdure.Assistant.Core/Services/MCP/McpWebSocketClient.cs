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
    {        if (_isInitialized)
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

            // 发送MCP初始化请求并等待响应
            var initRequestId = GetNextRequestId();
            var tcs = new TaskCompletionSource<string>();
            _pendingRequests[initRequestId] = tcs;

            var capabilities = new
            {
                tools = new { },
                logging = new { }
            };

            try
            {
                await _webSocketClient.SendMcpInitializeAsync(initRequestId, capabilities);
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
                    await LoadToolsFromServerAsync();
                    
                    _isInitialized = true;
                    _logger?.LogInformation("MCP WebSocket client initialized successfully");
                }
                else
                {
                    throw new InvalidOperationException("Server did not confirm MCP initialization");
                }
            }
            catch (Exception ex)
            {
                _pendingRequests.TryRemove(initRequestId, out _);
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
    private async Task LoadToolsFromServerAsync()
    {
        try
        {
            _logger?.LogDebug("Loading tools from server");
            
            var toolsResponse = await GetToolsListAsync();
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
                                await _mcpIntegrationService.RegisterToolAsync(toolName, toolDescription, toolElement.ToString());
                                registeredCount++;
                                _logger?.LogDebug("Registered tool: {ToolName}", toolName);
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
    }    /// <summary>
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

            var response = await tcs.Task;
            
            // 处理工具调用响应并更新设备状态
            await ProcessToolCallResponseAsync(toolName, response);
            
            return response;
        }
        catch (Exception ex)
        {
            _pendingRequests.TryRemove(requestId, out _);
            _logger?.LogError(ex, "Failed to call tool: {ToolName}", toolName);
            throw;
        }
    }

    /// <summary>
    /// 处理工具调用响应并更新设备状态
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="response">服务端响应</param>
    private async Task ProcessToolCallResponseAsync(string toolName, string response)
    {
        try
        {
            var responseElement = JsonSerializer.Deserialize<JsonElement>(response);
            
            if (responseElement.TryGetProperty("result", out var resultElement))
            {
                _logger?.LogDebug("Tool call {ToolName} succeeded, processing result", toolName);
                
                // 检查结果中是否包含状态更新
                if (resultElement.TryGetProperty("content", out var contentElement))
                {
                    var contentArray = contentElement.EnumerateArray();
                    foreach (var contentItem in contentArray)
                    {
                        if (contentItem.TryGetProperty("type", out var typeElement) && 
                            typeElement.GetString() == "text" &&
                            contentItem.TryGetProperty("text", out var textElement))
                        {
                            var resultText = textElement.GetString();
                            
                            // 尝试解析设备状态信息
                            await UpdateDeviceStateFromResultAsync(toolName, resultText);
                        }
                    }
                }
                
                // 通知MCP集成服务工具调用成功
                await _mcpIntegrationService.OnToolCallCompletedAsync(toolName, resultElement.ToString());
            }
            else if (responseElement.TryGetProperty("error", out var errorElement))
            {
                _logger?.LogWarning("Tool call {ToolName} failed with error: {Error}", toolName, errorElement.ToString());
                
                // 通知MCP集成服务工具调用失败
                await _mcpIntegrationService.OnToolCallFailedAsync(toolName, errorElement.ToString());
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to process tool call response for {ToolName}", toolName);
        }
    }

    /// <summary>
    /// 根据工具调用结果更新设备状态
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="resultText">结果文本</param>
    private async Task UpdateDeviceStateFromResultAsync(string toolName, string? resultText)
    {
        if (string.IsNullOrEmpty(resultText))
            return;

        try
        {
            // 根据工具名称和结果推断状态变化
            if (toolName.Contains("turn_on") || toolName.Contains("enable"))
            {
                var deviceName = ExtractDeviceNameFromTool(toolName);
                if (!string.IsNullOrEmpty(deviceName))
                {
                    await _mcpIntegrationService.UpdateDeviceStateAsync(deviceName, "power", true);
                    _logger?.LogDebug("Updated device {DeviceName} power state to ON", deviceName);
                }
            }
            else if (toolName.Contains("turn_off") || toolName.Contains("disable"))
            {
                var deviceName = ExtractDeviceNameFromTool(toolName);
                if (!string.IsNullOrEmpty(deviceName))
                {
                    await _mcpIntegrationService.UpdateDeviceStateAsync(deviceName, "power", false);
                    _logger?.LogDebug("Updated device {DeviceName} power state to OFF", deviceName);
                }
            }
            else if (toolName.Contains("brightness") || toolName.Contains("dim"))
            {
                var deviceName = ExtractDeviceNameFromTool(toolName);
                if (!string.IsNullOrEmpty(deviceName))
                {
                    // 尝试从结果文本中提取亮度值
                    var brightness = ExtractBrightnessFromResult(resultText);
                    if (brightness.HasValue)
                    {
                        await _mcpIntegrationService.UpdateDeviceStateAsync(deviceName, "brightness", brightness.Value);
                        _logger?.LogDebug("Updated device {DeviceName} brightness to {Brightness}", deviceName, brightness.Value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update device state from tool result");
        }
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
