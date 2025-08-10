using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
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
        // 检查是否有测试音乐播放器的参数
        if (args.Length > 0 && args[0] == "--test-music")
        {
            await TestMusic.MusicPlayerTest.TestMusicPlayback();
            return;
        }

        // 创建主机
        var host = CreateHostBuilder(args).Build();       
        
        _logger = host.Services.GetRequiredService<ILogger<Program>>();
        _voiceChatService = host.Services.GetRequiredService<IVoiceChatService>();
        var interruptManager = host.Services.GetRequiredService<InterruptManager>();
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

            // Set up wake word detector coordination (matches py-xiaozhi behavior)
            _voiceChatService.SetInterruptManager(interruptManager);
            await interruptManager.InitializeAsync();            
            
            // Set up Microsoft Cognitive Services keyword spotting (matches py-xiaozhi wake word detector)
            _voiceChatService.SetKeywordSpottingService(keywordSpottingService);
            System.Console.WriteLine("关键词唤醒功能已启用（基于Microsoft认知服务）");
            
            // Set up Music-Voice Coordination Service for automatic synchronization
            var musicVoiceCoordinationService = host.Services.GetRequiredService<MusicVoiceCoordinationService>();
            _voiceChatService.SetMusicVoiceCoordinationService(musicVoiceCoordinationService);
            System.Console.WriteLine("音乐语音协调服务已启用（自动暂停/恢复语音识别）");

            // Initialize MCP IoT devices (new architecture based on xiaozhi-esp32)
            await InitializeMcpDevicesAsync(host.Services);

            // Initialize MCP services (new architecture based on xiaozhi-esp32)
            await InitializeMcpServicesAsync(host.Services);

            // 初始化服务 (this will establish WebSocket connection and trigger IoT initialization)
            await _voiceChatService.InitializeAsync(_config);
            
            System.Console.WriteLine($"已连接到服务器: {(_config.UseWebSocket ? _config.ServerUrl : $"{_config.MqttBroker}:{_config.MqttPort}")}");
            System.Console.WriteLine();

            await ShowMenu();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "程序启动失败");
            System.Console.WriteLine($"启动失败: {ex.Message}");
        }
        finally
        {
            _voiceChatService?.Dispose();
        }
    }    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    // Set Debug as the minimum level, can be overridden by appsettings.json
                    builder.SetMinimumLevel(LogLevel.Information);
                });                
                
                // Register services with dependency injection
                services.AddSingleton<IVerificationService, VerificationService>();
                services.AddSingleton<IConfigurationService, ConfigurationService>();
                services.AddSingleton<IVoiceChatService, VoiceChatService>();
                
                // Add InterruptManager for wake word detector coordination
                services.AddSingleton<InterruptManager>();                
                // Add Microsoft Cognitive Services keyword spotting service
                services.AddSingleton<IKeywordSpottingService, KeywordSpottingService>();
                
                // Add Music-Voice Coordination Service for automatic pause/resume synchronization
                services.AddSingleton<MusicVoiceCoordinationService>();

                // 注册 AudioStreamManager 单例（使用正确的方式）
                services.AddSingleton<AudioStreamManager>(provider =>
                {
                    var logger = provider.GetService<ILogger<AudioStreamManager>>();
                    return AudioStreamManager.GetInstance(logger);                });                
                // Music player service (required for MCP music device)
                services.AddSingleton<IMusicPlayerService, KugouMusicService>();
                services.AddSingleton<IMusicAudioPlayer, ConsoleMusicAudioPlayer>();                
                // Register MCP services (new architecture based on xiaozhi-esp32)
                services.AddSingleton<McpServer>();
                services.AddSingleton<McpDeviceManager>(provider =>
                {
                    var logger = provider.GetRequiredService<ILogger<McpDeviceManager>>();
                    var mcpServer = provider.GetRequiredService<McpServer>();
                    var musicService = provider.GetService<IMusicPlayerService>();
                    return new McpDeviceManager(logger, mcpServer, musicService);
                });
                services.AddSingleton<McpIntegrationService>();

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
            var mcpIntegrationService = services.GetService<McpIntegrationService>();
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
            if (mcpIntegrationService == null)
            {
                logger?.LogError("McpIntegrationService service not found");
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
            await mcpIntegrationService.InitializeAsync();

            // Set MCP integration service on VoiceChatService (new MCP-based integration)
            voiceChatService.SetMcpIntegrationService(mcpIntegrationService);

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
    }/// <summary>
    /// Initialize MCP services (new architecture based on xiaozhi-esp32)
    /// </summary>
    static async Task InitializeMcpServicesAsync(IServiceProvider services)
    {
        try
        {
            var logger = services.GetService<ILogger<Program>>();
            logger?.LogInformation("开始初始化MCP服务...");

            // Get MCP services
            var mcpServer = services.GetService<McpServer>();
            var mcpDeviceManager = services.GetService<McpDeviceManager>();
            var mcpIntegrationService = services.GetService<McpIntegrationService>();

            if (mcpServer == null || mcpDeviceManager == null || mcpIntegrationService == null)
            {
                logger?.LogWarning("MCP services not found, skipping MCP initialization");
                return;
            }

            // Initialize MCP integration
            await mcpIntegrationService.InitializeAsync();

            // Wire MCP integration service to VoiceChatService
            if (_voiceChatService != null)
            {
                _voiceChatService.SetMcpIntegrationService(mcpIntegrationService);
                logger?.LogInformation("MCP集成服务已连接到VoiceChatService");
            }

            logger?.LogInformation("MCP服务初始化完成");
            System.Console.WriteLine("MCP设备管理器已启用 (基于xiaozhi-esp32架构)");
        }
        catch (Exception ex)
        {
            var logger = services.GetService<ILogger<Program>>();
            logger?.LogError(ex, "MCP服务初始化失败");
            System.Console.WriteLine($"MCP服务初始化失败: {ex.Message}");
        }
    }

    static async Task ShowMenu()
    {
        while (true)
        {            System.Console.WriteLine("\n请选择操作:");
            System.Console.WriteLine("1. 开始语音对话");
            System.Console.WriteLine("2. 停止语音对话");
            System.Console.WriteLine("3. 切换对话状态 (自动模式)");
            System.Console.WriteLine("4. 切换自动对话模式");
            System.Console.WriteLine("5. 发送文本消息");
            System.Console.WriteLine("6. 查看连接状态");
            System.Console.WriteLine("7. 退出");
            System.Console.Write("请输入选项 (1-7): ");

            var input = System.Console.ReadLine();
              switch (input)
            {
                case "1":
                    await StartVoiceChat();
                    break;
                case "2":
                    await StopVoiceChat();
                    break;
                case "3":
                    await ToggleChatState();
                    break;
                case "4":
                    await ToggleAutoDialogueMode();
                    break;
                case "5":
                    await SendTextMessage();
                    break;
                case "6":
                    ShowConnectionStatus();
                    break;
                case "7":
                    System.Console.WriteLine("再见!");
                    return;
                default:
                    System.Console.WriteLine("无效选项，请重新输入。");
                    break;
            }
        }
    }

    static async Task StartVoiceChat()
    {
        if (_voiceChatService == null)
        {
            System.Console.WriteLine("服务未初始化");
            return;
        }

        if (!_voiceChatService.IsConnected)
        {
            System.Console.WriteLine("未连接到服务器");
            return;
        }

        if (_voiceChatService.IsVoiceChatActive)
        {
            System.Console.WriteLine("语音对话已经在进行中");
            return;
        }

        try
        {
            await _voiceChatService.StartVoiceChatAsync();
            System.Console.WriteLine("语音对话已开始，按任意键停止...");
            System.Console.ReadKey();
            await _voiceChatService.StopVoiceChatAsync();
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"启动语音对话失败: {ex.Message}");
        }
    }

    static async Task StopVoiceChat()
    {
        if (_voiceChatService?.IsVoiceChatActive == true)
        {
            await _voiceChatService.StopVoiceChatAsync();
            System.Console.WriteLine("语音对话已停止");
        }
        else
        {
            System.Console.WriteLine("语音对话未在进行中");
        }
    }

    static async Task SendTextMessage()
    {
        if (_voiceChatService == null || !_voiceChatService.IsConnected)
        {
            System.Console.WriteLine("未连接到服务器");
            return;
        }

        System.Console.Write("请输入消息: ");
        var message = System.Console.ReadLine();
        
        if (!string.IsNullOrWhiteSpace(message))
        {
            await _voiceChatService.SendTextMessageAsync(message);
            System.Console.WriteLine("消息已发送");
        }
    }    static void ShowConnectionStatus()
    {
        if (_voiceChatService == null)
        {
            System.Console.WriteLine("服务未初始化");
            return;
        }

        System.Console.WriteLine($"连接状态: {(_voiceChatService.IsConnected ? "已连接" : "未连接")}");
        System.Console.WriteLine($"语音对话状态: {(_voiceChatService.IsVoiceChatActive ? "进行中" : "未开始")}");
        System.Console.WriteLine($"设备状态: {_voiceChatService.CurrentState}");
        System.Console.WriteLine($"监听模式: {_voiceChatService.CurrentListeningMode}");
        System.Console.WriteLine($"自动对话模式: {(_voiceChatService.KeepListening ? "启用" : "禁用")}");
        System.Console.WriteLine($"通信协议: {(_config?.UseWebSocket == true ? "WebSocket" : "MQTT")}");
        System.Console.WriteLine($"语音功能: {(_config?.EnableVoice == true ? "启用" : "禁用")}");
    }

    static async Task ToggleChatState()
    {
        if (_voiceChatService == null)
        {
            System.Console.WriteLine("服务未初始化");
            return;
        }

        if (!_voiceChatService.IsConnected)
        {
            System.Console.WriteLine("未连接到服务器");
            return;
        }

        try
        {
            await _voiceChatService.ToggleChatStateAsync();
            System.Console.WriteLine($"对话状态已切换，当前状态: {_voiceChatService.CurrentState}");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"切换对话状态失败: {ex.Message}");
        }
    }    static Task ToggleAutoDialogueMode()
    {
        if (_voiceChatService == null)
        {
            System.Console.WriteLine("服务未初始化");
            return Task.CompletedTask;
        }

        _voiceChatService.KeepListening = !_voiceChatService.KeepListening;
        System.Console.WriteLine($"自动对话模式: {(_voiceChatService.KeepListening ? "已启用" : "已禁用")}");
        
        if (_voiceChatService.KeepListening)
        {
            System.Console.WriteLine("设备将在对话结束后自动开始下一轮监听");
        }
        
        return Task.CompletedTask;
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
