using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Services.MCP;

/// <summary>
/// MCP集成适配器 - 将简化的IMcpIntegration适配为传统的McpIntegrationService
/// 使用简化实现避免复杂的依赖问题
/// </summary>
internal class McpIntegrationAdapter : McpIntegrationService
{
    private readonly IMcpIntegration _mcpIntegration;

    public McpIntegrationAdapter(IMcpIntegration mcpIntegration) 
        : base(
            new NullLogger<McpIntegrationService>(), 
            new McpDeviceManager(new NullLogger<McpDeviceManager>(), new McpServer(new NullLogger<McpServer>())),
            new McpServer(new NullLogger<McpServer>()))
    {
        _mcpIntegration = mcpIntegration ?? throw new ArgumentNullException(nameof(mcpIntegration));
    }

    /// <summary>
    /// 获取可用的IoT函数（委托给简化的MCP集成）
    /// </summary>
    public new List<VoiceChatFunction> GetAvailableFunctions()
    {
        try
        {
            return _mcpIntegration.GetVoiceChatFunctions();
        }
        catch
        {
            return new List<VoiceChatFunction>();
        }
    }

    /// <summary>
    /// 执行函数调用（委托给简化的MCP集成）
    /// </summary>
    public new async Task<string> ExecuteFunctionAsync(string functionName, Dictionary<string, object>? parameters = null)
    {
        try
        {
            var result = await _mcpIntegration.ExecuteToolAsync(functionName, parameters);
            
            // 转换McpToolCallResult为字符串响应
            if (result.IsError)
            {
                return $"执行失败: {result.Content?.FirstOrDefault()?.Text ?? "未知错误"}";
            }
            
            return result.Content?.FirstOrDefault()?.Text ?? "执行成功";
        }
        catch (Exception ex)
        {
            return $"执行失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 获取设备状态（委托给简化的MCP集成）
    /// </summary>
    public new Dictionary<string, object> GetDeviceStates()
    {
        try
        {
            return _mcpIntegration.GetDeviceStates();
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// 处理MCP请求（委托给简化的MCP集成）
    /// </summary>
    public new async Task<string> HandleMcpRequestAsync(string jsonRequest)
    {
        try
        {
            return await _mcpIntegration.HandleRequestAsync(jsonRequest);
        }
        catch (Exception ex)
        {
            return $"{{\"jsonrpc\":\"2.0\",\"error\":{{\"code\":-32603,\"message\":\"Internal error: {ex.Message}\"}}}}";
        }
    }

    /// <summary>
    /// 初始化（简化版无需额外初始化）
    /// </summary>
    public new async Task InitializeAsync()
    {
        // 简化的MCP集成在构造函数中已经完成初始化，无需额外操作
        await Task.CompletedTask;
    }
}
