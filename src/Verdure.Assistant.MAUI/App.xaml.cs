using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Services.MCP;

namespace Verdure.Assistant.MAUI;

public partial class App : Application
{
    private readonly ILogger<App>? _logger;

    public App(ILogger<App>? logger = null)
    {
        InitializeComponent();
        _logger = logger;

        _logger?.LogInformation("Verdure Assistant MAUI应用程序已启动");
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell() { Title = "绿荫助手" });
        
        // 异步初始化VoiceChatService相关服务（参考WinUI项目架构）
        Task.Run(async () => await InitializeVoiceChatServicesAsync());
        
        return window;
    }

    /// <summary>
    /// 初始化VoiceChatService和相关服务（参考WinUI项目的初始化逻辑）
    /// </summary>
    private async Task InitializeVoiceChatServicesAsync()
    {
        try
        {
            _logger?.LogInformation("开始初始化语音聊天服务...");

            // 获取服务实例
            var voiceChatService = GetService<IVoiceChatService>();
            var mcpServer = GetService<McpServer>();
            var mcpDeviceManager = GetService<McpDeviceManager>();
            var musicPlayerService = GetService<IMusicPlayerService>();
            var keywordSpottingService = GetService<IKeywordSpottingService>();

            if (voiceChatService == null)
            {
                _logger?.LogError("VoiceChatService未注册");
                return;
            }

            // 初始化MCP服务（如果可用）
            if (mcpServer != null && mcpDeviceManager != null)
            {
                await mcpServer.InitializeAsync();
                await mcpDeviceManager.InitializeAsync();
                _logger?.LogInformation("MCP服务已初始化");
            }

            _logger?.LogInformation("语音聊天服务初始化完成");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "语音聊天服务初始化失败");
        }
    }

    /// <summary>
    /// 获取依赖注入容器中的服务
    /// </summary>
    private static T? GetService<T>() where T : class
    {
        return Current?.Handler?.MauiContext?.Services?.GetService<T>();
    }
}
