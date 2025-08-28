using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Core.Services.MCP;

/// <summary>
/// MCP服务扩展方法 - 简化依赖注入配置
/// 基于xiaozhi-esp32的简洁设计模式
/// </summary>
public static class McpServiceExtensions
{
    /// <summary>
    /// 添加简化的MCP服务到依赖注入容器
    /// 这个方法替代了复杂的 McpServer + McpDeviceManager + McpIntegrationService 配置
    /// </summary>
    public static IServiceCollection AddSimpleMcpServices(this IServiceCollection services)
    {
        // 自动注册相机服务
        services.AddSingleton<ICameraService>(provider =>
        {
            return CameraServiceFactory.CreateCameraService(provider);
        });

        // 注册简化的MCP管理器 - 自包含所有MCP功能
        services.AddSingleton<SimpleMcpManager>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<SimpleMcpManager>>();
            var mcpServerLogger = provider.GetRequiredService<ILogger<McpServer>>();
            var mcpServer = new McpServer(mcpServerLogger);
            
            return new SimpleMcpManager(logger, mcpServer);
        });

        // 可选：提供向后兼容的接口适配器
        services.AddSingleton<IMcpIntegration>(provider => 
            new SimpleMcpIntegrationAdapter(provider.GetRequiredService<SimpleMcpManager>()));

        return services;
    }

    /// <summary>
    /// 添加传统的MCP服务（保持向后兼容）
    /// </summary>
    public static IServiceCollection AddLegacyMcpServices(this IServiceCollection services)
    {
        // 保留原有的复杂配置，用于渐进式迁移
        services.AddSingleton<McpServer>();
        services.AddSingleton<McpDeviceManager>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<McpDeviceManager>>();
            var mcpServer = provider.GetRequiredService<McpServer>();
            var musicService = provider.GetService<IMusicPlayerService>();
            return new McpDeviceManager(logger, mcpServer, musicService);
        });
        services.AddSingleton<McpIntegrationService>();

        return services;
    }
}

/// <summary>
/// MCP集成接口 - 用于向后兼容
/// </summary>
public interface IMcpIntegration
{
    Task<string> HandleRequestAsync(string jsonRequest);
    Task<McpToolCallResult> ExecuteToolAsync(string toolName, Dictionary<string, object>? parameters = null);
    List<VoiceChatFunction> GetVoiceChatFunctions();
    Dictionary<string, object> GetDeviceStates();
}

/// <summary>
/// 简化MCP管理器的集成适配器 - 提供向后兼容性
/// </summary>
internal class SimpleMcpIntegrationAdapter : IMcpIntegration
{
    private readonly SimpleMcpManager _mcpManager;

    public SimpleMcpIntegrationAdapter(SimpleMcpManager mcpManager)
    {
        _mcpManager = mcpManager ?? throw new ArgumentNullException(nameof(mcpManager));
    }

    public async Task<string> HandleRequestAsync(string jsonRequest)
    {
        return await _mcpManager.HandleRequestAsync(jsonRequest);
    }

    public async Task<McpToolCallResult> ExecuteToolAsync(string toolName, Dictionary<string, object>? parameters = null)
    {
        return await _mcpManager.ExecuteToolAsync(toolName, parameters);
    }

    public List<VoiceChatFunction> GetVoiceChatFunctions()
    {
        return _mcpManager.GetVoiceChatFunctions();
    }

    public Dictionary<string, object> GetDeviceStates()
    {
        // 简化的设备状态返回
        return new Dictionary<string, object>
        {
            ["device_count"] = _mcpManager.GetAllTools().Count,
            ["tools_available"] = _mcpManager.GetAllTools().Keys.ToList(),
            ["music_available"] = _mcpManager.GetAllTools().ContainsKey("play_music"),
            ["camera_available"] = _mcpManager.GetAllTools().ContainsKey("control_camera"),
            ["timestamp"] = DateTime.Now
        };
    }
}
