using Verdure.Assistant.Api.IoT.Interfaces;
using Verdure.Assistant.Api.IoT.Services;
using Verdure.Assistant.Api.Services;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Services.MCP;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure logging
builder.Services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

// Register core services
builder.Services.AddSingleton<IVerificationService, VerificationService>();
builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();

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

// Register IoT services for robot emotion and action control
builder.Services.AddSingleton<IDisplayService, DisplayService>();
builder.Services.AddSingleton<IRobotActionService, RobotActionService>();
builder.Services.AddSingleton<IEmotionActionService, EmotionActionService>();

// Register emotion integration service
builder.Services.AddSingleton<IEmotionIntegrationService, EmotionIntegrationService>();

// Register VoiceChatService with emotion handler
builder.Services.AddSingleton<IVoiceChatService>(provider =>
{
    var configurationService = provider.GetRequiredService<IConfigurationService>();
    var audioStreamManager = provider.GetRequiredService<AudioStreamManager>();
    var logger = provider.GetService<ILogger<VoiceChatService>>();
    var emotionHandler = provider.GetService<IEmotionIntegrationService>();

    return new VoiceChatService(configurationService, audioStreamManager, logger, emotionHandler);
});

// Register time display background service
builder.Services.AddHostedService<TimeDisplayService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Enable static files with default files
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

app.MapControllers();

Console.WriteLine("=== 绿荫助手语音聊天API服务 ===");
Console.WriteLine("🚀 正在启动Web服务器...");

// Initialize basic services that don't block
var logger = app.Services.GetService<ILogger<Program>>();
logger?.LogInformation("=== 绿荫助手语音聊天API服务启动 ===");

// Start the web server in background and continue with initialization
var webServerTask = app.RunAsync();

Console.WriteLine($"📱 Web控制面板: http://localhost:5031");
Console.WriteLine($"📚 API文档: http://localhost:5031/swagger");
Console.WriteLine("🔧 开始初始化后台服务...");

// Initialize services in background after web server starts
_ = Task.Run(async () =>
{
    try
    {
        // Wait a moment for web server to start
        await Task.Delay(1000);
        
        Console.WriteLine("音乐播放功能: 已启用 (基于mpg123)");
        Console.WriteLine("语音聊天功能: 已启用");
        Console.WriteLine("MCP设备管理: 已启用");
        Console.WriteLine("机器人表情动作功能: 已启用");
        Console.WriteLine($"[音乐缓存] 音乐缓存目录: {Path.Combine(Path.GetTempPath(), "VerdureMusicCache")}");

        // Initialize IoT emotion services
        try
        {
            var emotionIntegrationService = app.Services.GetService<IEmotionIntegrationService>();
            if (emotionIntegrationService != null)
            {
                await emotionIntegrationService.InitializeAsync();
                logger?.LogInformation("情感集成服务初始化完成");
                Console.WriteLine("✅ 情感集成服务初始化完成");
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "情感集成服务初始化失败");
            Console.WriteLine($"❌ 情感集成服务初始化失败: {ex.Message}");
        }

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
                Console.WriteLine($"✅ MCP服务初始化完成，注册了 {mcpDeviceManager.Devices.Count} 个设备");
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "MCP服务初始化失败");
            Console.WriteLine($"❌ MCP服务初始化失败: {ex.Message}");
        }

        // 可选：自动初始化语音聊天服务（在后台运行，不阻塞）
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

                if (voiceChatService != null && interruptManager != null && keywordSpottingService != null)
                {
                    // 设置语音聊天服务的各种组件（类似Console项目）
                    voiceChatService.SetInterruptManager(interruptManager);
                    await interruptManager.InitializeAsync();

                    voiceChatService.SetKeywordSpottingService(keywordSpottingService);
                    Console.WriteLine("✅ [语音聊天] 关键词唤醒功能已启用（基于Microsoft认知服务）");

                    if (musicVoiceCoordinationService != null)
                    {
                        voiceChatService.SetMusicVoiceCoordinationService(musicVoiceCoordinationService);
                        Console.WriteLine("✅ [语音聊天] 音乐语音协调服务已启用");
                    }

                    if (mcpIntegrationServiceForVoice != null)
                    {
                        voiceChatService.SetMcpIntegrationService(mcpIntegrationServiceForVoice);
                        Console.WriteLine("✅ [语音聊天] MCP集成服务已连接");
                    }

                    // 创建默认配置并初始化
                    var config = CreateDefaultVerdureConfig(app.Configuration);
                    await voiceChatService.InitializeAsync(config);

                    logger?.LogInformation("语音聊天服务自动启动完成");
                    Console.WriteLine("✅ [语音聊天] 语音聊天服务自动启动完成，开始监听关键词唤醒...");
                    
                    // 注意：这里不调用会阻塞的方法，只是初始化
                    // 关键词监听会在后台自动运行
                }
                else
                {
                    logger?.LogWarning("语音聊天服务组件不完整，跳过自动启动");
                    Console.WriteLine("⚠️ [语音聊天] 语音聊天服务组件不完整，跳过自动启动");
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "自动启动语音聊天失败");
                Console.WriteLine($"❌ [语音聊天] 自动启动失败: {ex.Message}");
            }
        }

        Console.WriteLine("✅ 所有后台服务初始化完成");
    }
    catch (Exception ex)
    {
        logger?.LogError(ex, "后台服务初始化失败");
        Console.WriteLine($"❌ 后台服务初始化失败: {ex.Message}");
    }
});

// Wait for the web server (this will block until the application is shut down)
await webServerTask;

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
