using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Services.MCP;
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

app.UseAuthorization();

app.MapControllers();

// Initialize services on startup
var logger = app.Services.GetService<ILogger<Program>>();
logger?.LogInformation("=== 绿荫助手语音聊天API服务启动 ===");
logger?.LogInformation("音乐播放功能: 已启用 (mpg123)");
logger?.LogInformation("语音聊天功能: 已启用");
logger?.LogInformation("MCP设备管理: 已启用");

Console.WriteLine("=== 绿荫助手语音聊天API服务 ===");
Console.WriteLine("音乐播放功能: 已启用 (基于mpg123)");
Console.WriteLine("语音聊天功能: 已启用");
Console.WriteLine("MCP设备管理: 已启用");
Console.WriteLine($"[音乐缓存] 音乐缓存目录: {Path.Combine(Path.GetTempPath(), "VerdureMusicCache")}");

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

app.Run();
