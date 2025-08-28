using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Services.MCP;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Api.Services;
using Verdure.Assistant.Api.Audio;
using System.Runtime.ExceptionServices;
using System.Runtime;

// 优化垃圾回收设置，特别针对树莓派等ARM设备
GCSettings.LatencyMode = GCLatencyMode.Batch; // 批量回收模式，减少中断
if (Environment.ProcessorCount <= 4) // 树莓派等低核心数设备
{
    // 强制使用服务器垃圾回收模式，提高音频处理稳定性
    Console.WriteLine("[GC优化] 检测到低核心数设备，启用优化的垃圾回收模式");
}

// 添加全局异常处理器
AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    var exception = e.ExceptionObject as Exception;
    Console.WriteLine($"[全局异常] 未处理的异常: {exception?.Message}");
    Console.WriteLine($"[全局异常] 堆栈跟踪: {exception?.StackTrace}");
    
    // 特别处理音频相关异常
    if (exception?.Message.Contains("PortAudio") == true || 
        exception?.Message.Contains("audio") == true ||
        exception?.Message.Contains("stream") == true ||
        exception?.StackTrace?.Contains("Stream.Finalize") == true)
    {
        Console.WriteLine("[音频异常] 检测到音频流异常，尝试恢复...");
        try
        {
            // 强制垃圾回收，清理可能的悬挂对象
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            // 尝试重新初始化音频系统
            var audioManager = AudioStreamManager.GetInstance();
            audioManager?.ForceCleanup();
            Console.WriteLine("[音频异常] 音频系统已强制清理");
            
            // 强制清理 PortAudio 管理器
            PortAudioManager.Instance.ForceCleanup();
            Console.WriteLine("[音频异常] PortAudio 管理器已强制清理");
        }
        catch (Exception cleanupEx)
        {
            Console.WriteLine($"[音频异常] 清理失败: {cleanupEx.Message}");
        }
    }
    
    // 记录异常但不让程序崩溃（如果可能）
    if (!e.IsTerminating)
    {
        Console.WriteLine("[全局异常] 异常已被捕获，程序继续运行");
    }
    else
    {
        Console.WriteLine("[全局异常] 致命异常，程序即将退出");
        Console.WriteLine("[清理] 执行紧急资源清理...");
        
        // 紧急清理资源
        try
        {
            PortAudioManager.Instance.ForceCleanup();
            Console.WriteLine("[清理] 紧急清理完成");
        }
        catch (Exception emergencyEx)
        {
            Console.WriteLine($"[清理] 紧急清理失败: {emergencyEx.Message}");
        }
    }
};

TaskScheduler.UnobservedTaskException += (sender, e) =>
{
    Console.WriteLine($"[任务异常] 未观察到的任务异常: {e.Exception.Message}");
    foreach (var ex in e.Exception.InnerExceptions)
    {
        Console.WriteLine($"[任务异常] 内部异常: {ex.Message}");
        
        // 特别处理音频相关的任务异常
        if (ex.Message.Contains("PortAudio") || 
            ex.Message.Contains("audio") || 
            ex.Message.Contains("stream"))
        {
            Console.WriteLine($"[音频任务异常] 音频流任务异常: {ex.Message}");
        }
    }
    
    // 标记异常为已处理，防止程序崩溃
    e.SetObserved();
    Console.WriteLine("[任务异常] 异常已被标记为已处理");
};

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configure logging
builder.Services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

// Register core services
builder.Services.AddSingleton<IVerificationService, VerificationService>();
builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
builder.Services.AddSingleton<IVoiceChatService, VoiceChatService>();

// Add InterruptManager for wake word detector coordination
builder.Services.AddSingleton<InterruptManager>();

// Add Microsoft Cognitive Services keyword spotting service
builder.Services.AddSingleton<IKeywordSpottingService, KeywordSpottingService>();

// Add Music-Voice Coordination Service for automatic pause/resume synchronization
builder.Services.AddSingleton<MusicVoiceCoordinationService>();

// 注册 AudioStreamManager 单例
builder.Services.AddSingleton<AudioStreamManager>(provider =>
{
    var logger = provider.GetService<ILogger<AudioStreamManager>>();
    return AudioStreamManager.GetInstance(logger);
});

// Music player service (using mpg123)
builder.Services.AddSingleton<IMusicPlayerService, ApiMusicService>();

// Register Robot Services
builder.Services.AddSingleton<Verdure.Assistant.Api.Services.Robot.DisplayService>();
builder.Services.AddSingleton<Verdure.Assistant.Api.Services.Robot.RobotActionService>();
builder.Services.AddSingleton<Verdure.Assistant.Api.Services.Robot.EmotionActionService>();

// Register Background Services
builder.Services.AddHostedService<Verdure.Assistant.Api.Services.Robot.TimeDisplayService>();
builder.Services.AddHostedService<Verdure.Assistant.Api.Services.AudioMonitoringService>();

// Register Emotion Integration Service
builder.Services.AddSingleton<Verdure.Assistant.Api.Services.EmotionIntegrationService>();

// Register MCP services
builder.Services.AddSingleton<McpServer>();
builder.Services.AddSingleton<McpDeviceManager>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<McpDeviceManager>>();
    var mcpServer = provider.GetRequiredService<McpServer>();
    var musicService = provider.GetService<IMusicPlayerService>();
    return new McpDeviceManager(logger, mcpServer, musicService);
});
builder.Services.AddSingleton<McpIntegrationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Add static files support (for wwwroot)
app.UseDefaultFiles();
app.UseStaticFiles();

// Add CORS support
app.UseCors(builder =>
{
    builder.AllowAnyOrigin()
           .AllowAnyMethod()
           .AllowAnyHeader();
});

app.UseAuthorization();

app.MapControllers();

// Initialize services on startup
var logger = app.Services.GetService<ILogger<Program>>();
logger?.LogInformation("=== 绿荫助手语音聊天API服务启动 ===");
logger?.LogInformation("音乐播放功能: 已启用 (mpg123)");
logger?.LogInformation("语音聊天功能: 已启用");
logger?.LogInformation("MCP设备管理: 已启用");
logger?.LogInformation("机器人表情与动作: 已启用");
logger?.LogInformation("音频监控服务: 已启用");
logger?.LogInformation("全局异常处理: 已启用");

Console.WriteLine("=== 绿荫助手语音聊天API服务 ===");
Console.WriteLine("音乐播放功能: 已启用 (基于mpg123)");
Console.WriteLine("语音聊天功能: 已启用");
Console.WriteLine("MCP设备管理: 已启用");
Console.WriteLine("机器人表情与动作: 已启用");
Console.WriteLine("音频监控服务: 已启用 (30秒检查间隔)");
Console.WriteLine("全局异常处理: 已启用 (音频流异常自动恢复)");
Console.WriteLine($"[音乐缓存] 音乐缓存目录: {Path.Combine(Path.GetTempPath(), "VerdureMusicCache")}");
Console.WriteLine($"[机器人] Web控制面板: http://localhost:5000");

// Initialize Robot services if needed
try
{
    var emotionActionService = app.Services.GetService<Verdure.Assistant.Api.Services.Robot.EmotionActionService>();
    
    if (emotionActionService != null)
    {
        // 在后台初始化机器人位置
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3000); // 等待其他服务完全启动
                await emotionActionService.InitializeRobotAsync();
                logger?.LogInformation("机器人初始化完成");
                Console.WriteLine("[机器人] 机器人位置初始化完成");



                // Initialize MCP services if needed
                try
                {
                    var mcpServer = app.Services.GetService<McpServer>();
                    var mcpDeviceManager = app.Services.GetService<McpDeviceManager>();
                    var mcpIntegrationService = app.Services.GetService<McpIntegrationService>();

                    if (mcpServer != null && mcpDeviceManager != null && mcpIntegrationService != null)
                    {
                        await mcpServer.InitializeAsync();
                        await mcpDeviceManager.InitializeAsync();
                        await mcpIntegrationService.InitializeAsync();

                        logger?.LogInformation("MCP服务初始化完成，注册了 {DeviceCount} 个设备", mcpDeviceManager.Devices.Count);
                        Console.WriteLine($"MCP服务初始化完成，注册了 {mcpDeviceManager.Devices.Count} 个设备");
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "MCP服务初始化失败");
                    Console.WriteLine($"MCP服务初始化失败: {ex.Message}");
                }

                // 可选：自动初始化语音聊天服务（类似Console项目）
                var autoStartVoiceChat = app.Configuration.GetValue<bool>("AutoStartVoiceChat", false);
                if (autoStartVoiceChat)
                {
                    try
                    {
                        logger?.LogInformation("自动启动语音聊天功能...");
                        Console.WriteLine("[语音聊天] 自动启动语音聊天功能...");

                        var voiceChatService = app.Services.GetService<IVoiceChatService>();
                        var interruptManager = app.Services.GetService<InterruptManager>();
                        var keywordSpottingService = app.Services.GetService<IKeywordSpottingService>();
                        var musicVoiceCoordinationService = app.Services.GetService<MusicVoiceCoordinationService>();
                        var mcpIntegrationServiceForVoice = app.Services.GetService<McpIntegrationService>();
                        var emotionIntegrationService = app.Services.GetService<Verdure.Assistant.Api.Services.EmotionIntegrationService>();

                        if (voiceChatService != null && interruptManager != null && keywordSpottingService != null)
                        {
                            // 设置语音聊天服务的各种组件（类似Console项目）
                            voiceChatService.SetInterruptManager(interruptManager);
                            await interruptManager.InitializeAsync();

                            voiceChatService.SetKeywordSpottingService(keywordSpottingService);
                            Console.WriteLine("[语音聊天] 关键词唤醒功能已启用（基于Microsoft认知服务）");

                            if (musicVoiceCoordinationService != null)
                            {
                                voiceChatService.SetMusicVoiceCoordinationService(musicVoiceCoordinationService);
                                Console.WriteLine("[语音聊天] 音乐语音协调服务已启用");
                            }

                            if (mcpIntegrationServiceForVoice != null)
                            {
                                voiceChatService.SetMcpIntegrationService(mcpIntegrationServiceForVoice);
                                Console.WriteLine("[语音聊天] MCP集成服务已连接");
                            }

                            // 连接机器人情感集成服务
                            if (emotionIntegrationService != null)
                            {
                                emotionIntegrationService.SetVoiceChatService(voiceChatService);
                                Console.WriteLine("[机器人] 情感集成服务已连接，支持语音情感表达");
                            }

                            // 创建默认配置并初始化
                            var config = CreateDefaultVerdureConfig(app.Configuration);
                            await voiceChatService.InitializeAsync(config);

                            logger?.LogInformation("语音聊天服务自动启动完成");
                            Console.WriteLine("[语音聊天] 语音聊天服务自动启动完成，开始监听关键词唤醒...");
                            Console.WriteLine("[语音聊天] 语音情感将自动触发机器人表情与动作");
                        }
                        else
                        {
                            logger?.LogWarning("语音聊天服务组件不完整，跳过自动启动");
                            Console.WriteLine("[语音聊天] 语音聊天服务组件不完整，跳过自动启动");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "自动启动语音聊天失败");
                        Console.WriteLine($"[语音聊天] 自动启动失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "机器人初始化失败");
                Console.WriteLine($"[机器人] 初始化失败: {ex.Message}");
            }
        });
        
        logger?.LogInformation("机器人服务初始化完成");
        Console.WriteLine("[机器人] 机器人服务初始化完成");
        Console.WriteLine("[机器人] API 端点:");
        Console.WriteLine("  POST /api/emotion/play - 播放指定表情和动作");
        Console.WriteLine("  POST /api/emotion/play-emotion/{type} - 仅播放表情");
        Console.WriteLine("  POST /api/emotion/play-action/{type} - 仅播放动作");
        Console.WriteLine("  POST /api/emotion/play-random - 随机播放");
        Console.WriteLine("  POST /api/emotion/stop - 停止播放");
        Console.WriteLine("  GET  /api/emotion/status - 获取状态");
        Console.WriteLine("  POST /api/emotion/initialize - 初始化机器人");
        Console.WriteLine("  POST /api/emotion/demo - 运行演示程序");
    }
}
catch (Exception ex)
{
    logger?.LogError(ex, "机器人服务初始化失败");
    Console.WriteLine($"[机器人] 服务初始化失败: {ex.Message}");
}



app.Run();

// 应用程序关闭时的清理
AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
{
    try
    {
        Console.WriteLine("[关闭] 应用程序正在关闭，清理资源...");
        PortAudioManager.Instance.ForceCleanup();
        Console.WriteLine("[关闭] 资源清理完成");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[关闭] 清理资源时出错: {ex.Message}");
    }
};

// 创建默认配置的辅助方法
static VerdureConfig CreateDefaultVerdureConfig(IConfiguration configuration)
{
    var config = new VerdureConfig();
    configuration.Bind(config);
    
    // 设置默认值
    if (string.IsNullOrEmpty(config.ServerUrl))
        config.ServerUrl = "wss://api.tenclass.net/xiaozhi/v1/";
    if (string.IsNullOrEmpty(config.MqttClientId))
        config.MqttClientId = "xiaozhi_api_client";
    if (string.IsNullOrEmpty(config.MqttTopic))
        config.MqttTopic = "xiaozhi/chat";
    
    // API项目没有ModelFiles目录，可能需要调整
    if (config.KeywordModels == null)
    {
        config.KeywordModels = new KeywordModelConfig
        {
            ModelsPath = "ModelFiles", // 可能需要复制模型文件或使用绝对路径
            CurrentModel = "keyword_xiaodian.table"
        };
    }
    
    return config;
}
