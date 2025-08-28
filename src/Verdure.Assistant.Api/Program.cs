using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Services.MCP;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Api.Services;
using Verdure.Assistant.Api.Audio;

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

Console.WriteLine("=== 绿荫助手语音聊天API服务 ===");
Console.WriteLine("音乐播放功能: 已启用 (基于mpg123)");
Console.WriteLine("语音聊天功能: 已启用");
Console.WriteLine("MCP设备管理: 已启用");
Console.WriteLine("机器人表情与动作: 已启用");
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
