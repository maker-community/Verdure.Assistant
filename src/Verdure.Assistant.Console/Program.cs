using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Console.Audio;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Services.MCP;

namespace Verdure.Assistant.Console;

class Program
{
    private static IVoiceChatService? _voiceChatService;
    private static ILogger<Program>? _logger;
    private static VerdureConfig? _config;

    static async Task Main(string[] args)
    {
        // 创建主机
        var host = CreateHostBuilder(args).Build();

        _logger = host.Services.GetRequiredService<ILogger<Program>>();
        _voiceChatService = host.Services.GetRequiredService<IVoiceChatService>();
        var musicPlayerService = host.Services.GetRequiredService<IMusicPlayerService>();
        var keywordSpottingService = host.Services.GetRequiredService<IKeywordSpottingService>();

        // 加载配置
        _config = LoadConfiguration();

        System.Console.WriteLine("=== 绿荫助手语音聊天客户端 (控制台版) ===");
        System.Console.WriteLine("初始化中...");

        try
        {            // 注册事件处理器
            _voiceChatService.MessageReceived += OnMessageReceived;
            _voiceChatService.VoiceChatStateChanged += OnVoiceChatStateChanged;
            _voiceChatService.ErrorOccurred += OnErrorOccurred;
            _voiceChatService.DeviceStateChanged += OnDeviceStateChanged;
            _voiceChatService.ListeningModeChanged += OnListeningModeChanged;
            _voiceChatService.DeviceStateChanged += OnDeviceStateChanged;
            _voiceChatService.ListeningModeChanged += OnListeningModeChanged;

            // Music player service is now injected via constructor dependency injection
            // No need to call SetMusicPlayerService anymore
            System.Console.WriteLine("音乐播放服务已通过构造函数注入用于VAD控制");

            // Initialize MCP IoT devices (new architecture based on xiaozhi-esp32)
            await InitializeMcpDevicesAsync(host.Services);

            // 初始化服务 (this will establish WebSocket connection and trigger IoT initialization)
            await _voiceChatService.InitializeAsync(_config);

            System.Console.WriteLine($"已连接到服务器: {(_config.UseWebSocket ? _config.ServerUrl : $"{_config.MqttBroker}:{_config.MqttPort}")}");
            System.Console.WriteLine();

            System.Console.ReadLine();

            //await ShowMenu();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "程序启动失败");
            System.Console.WriteLine($"启动失败: {ex.Message}");
        }
        finally
        {
            try
            {
                // 先释放VoiceChatService，但不停止音频录制
                _voiceChatService?.Dispose();

                // 最后释放音频录制器，停止连续录制
                var audioRecorder = host.Services.GetService<SoundFlowAudioRecorder>();
                if (audioRecorder != null)
                {
                    _logger?.LogInformation("程序退出，停止连续音频录制...");
                    audioRecorder.Dispose();
                    _logger?.LogInformation("连续音频录制已停止");
                }

                // 释放主机服务
                host?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "释放资源时出错");
                System.Console.WriteLine($"释放资源时出错: {ex.Message}");
            }
        }
    }

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    // Set Debug as the minimum level, can be overridden by appsettings.json
                    builder.SetMinimumLevel(LogLevel.Debug);
                });

                // Register services with dependency injection
                services.AddSingleton<IConfigurationService, ConfigurationService>();

                // Music player service (required for MCP music device) - Register before VoiceChatService
                services.AddSingleton<IMusicPlayerService, KuwoMusicService>();
                services.AddSingleton<IMusicAudioPlayer, SoundFlowMusicAudioPlayer>();

                services.AddSingleton<IVoiceChatService, VoiceChatService>();

                // Interrupt architecture (now integrated into VoiceChatService)
                // EnhancedInterruptManager has been removed - functionality integrated into VoiceChatService
                // Add Microsoft Cognitive Services keyword spotting service
                services.AddSingleton<IKeywordSpottingService, KeywordSpottingService>();

                // Register SoundFlow audio components as singleton using factory pattern
                services.AddSingleton<SoundFlowAudioRecorder>(provider =>
                {
                    var logger = provider.GetService<ILogger<SoundFlowAudioRecorder>>();
                    return SoundFlowAudioRecorder.GetInstance(logger);
                });


                services.AddSingleton<IAudioPlayer, SoundFlowAudioPlayer>();
                services.AddSingleton<ISharedAudioRecorder>(provider => provider.GetRequiredService<SoundFlowAudioRecorder>());
                // Register MCP services (new architecture based on xiaozhi-esp32)
                services.AddSingleton<McpServer>();
                services.AddSingleton(provider =>
                {
                    var logger = provider.GetRequiredService<ILogger<McpDeviceManager>>();
                    var mcpServer = provider.GetRequiredService<McpServer>();
                    var musicService = provider.GetService<IMusicPlayerService>();
                    return new McpDeviceManager(logger, mcpServer, musicService);
                });
            });

    static VerdureConfig LoadConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var config = new VerdureConfig();
        configuration.Bind(config);

        // 为Console项目设置关键词模型配置
        if (string.IsNullOrEmpty(config.KeywordModels.ModelsPath))
        {
            // Console项目的模型文件在 ModelFiles 目录
            config.KeywordModels.ModelsPath = "ModelFiles";
        }

        return config;
    }

    /// <summary>
    /// Initialize MCP IoT devices and setup integration (based on xiaozhi-esp32's MCP architecture)
    /// </summary>
    static async Task InitializeMcpDevicesAsync(IServiceProvider services)
    {
        try
        {
            var logger = services.GetService<ILogger<Program>>();
            logger?.LogInformation("开始初始化MCP IoT设备...");

            // Get required services
            var mcpServer = services.GetService<McpServer>();
            var mcpDeviceManager = services.GetService<McpDeviceManager>();
            var voiceChatService = services.GetService<IVoiceChatService>();

            if (mcpServer == null)
            {
                logger?.LogError("McpServer service not found");
                return;
            }
            if (mcpDeviceManager == null)
            {
                logger?.LogError("McpDeviceManager service not found");
                return;
            }
            if (voiceChatService == null)
            {
                logger?.LogError("VoiceChatService not found");
                return;
            }

            // Initialize MCP server and device manager (similar to xiaozhi-esp32 MCP initialization)
            await mcpServer.InitializeAsync();
            await mcpDeviceManager.InitializeAsync();

            logger?.LogInformation("MCP IoT设备初始化完成，共注册了 {DeviceCount} 个设备",
                mcpDeviceManager.Devices.Count);

            System.Console.WriteLine($"MCP IoT设备初始化完成，注册了 {mcpDeviceManager.Devices.Count} 个设备");
        }
        catch (Exception ex)
        {
            var logger = services.GetService<ILogger<Program>>();
            logger?.LogError(ex, "MCP IoT设备初始化失败");
            System.Console.WriteLine($"MCP IoT设备初始化失败: {ex.Message}");
        }
    }

    static void OnMessageReceived(object? sender, ChatMessage message)
    {
        System.Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] 收到消息 ({message.Role}): {message.Content}");
    }

    static void OnVoiceChatStateChanged(object? sender, bool isActive)
    {
        System.Console.WriteLine($"\n语音对话状态: {(isActive ? "已开始" : "已停止")}");
    }

    static void OnErrorOccurred(object? sender, string error)
    {
        System.Console.WriteLine($"\n错误: {error}");
    }

    static void OnDeviceStateChanged(object? sender, Verdure.Assistant.Core.Constants.DeviceState state)
    {
        System.Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] 设备状态变更: {state}");
    }

    static void OnListeningModeChanged(object? sender, Verdure.Assistant.Core.Constants.ListeningMode mode)
    {
        System.Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] 监听模式变更: {mode}");
    }
}
