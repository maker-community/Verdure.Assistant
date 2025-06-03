using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services;

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
            
            // Initialize IoT devices BEFORE initializing VoiceChatService (similar to py-xiaozhi's _initialize_iot_devices)
            InitializeIoTDevices(host.Services);

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
                    builder.SetMinimumLevel(LogLevel.Debug);
                });                // Register services with dependency injection
                services.AddSingleton<IVerificationService, VerificationService>();
                services.AddSingleton<IConfigurationService, ConfigurationService>();
                services.AddSingleton<IVoiceChatService, VoiceChatService>();
                
                // Add InterruptManager for wake word detector coordination
                services.AddSingleton<InterruptManager>();
                  // Add Microsoft Cognitive Services keyword spotting service
                services.AddSingleton<IKeywordSpottingService, KeywordSpottingService>();

                // 注册 AudioStreamManager 单例（使用正确的方式）
                services.AddSingleton<AudioStreamManager>(provider =>
                {
                    var logger = provider.GetService<ILogger<AudioStreamManager>>();
                    return AudioStreamManager.GetInstance(logger);
                });                // Music player service (required for MusicPlayerIoTDevice)
                services.AddSingleton<IMusicPlayerService, KugouMusicService>();
                services.AddSingleton<IMusicAudioPlayer, ConsoleMusicAudioPlayer>();

                // Register IoT Device Manager and devices (similar to py-xiaozhi's _initialize_iot_devices)
                services.AddSingleton<IoTDeviceManager>();
                services.AddSingleton<MusicPlayerIoTDevice>();
                services.AddSingleton<LampIoTDevice>();
                services.AddSingleton<SpeakerIoTDevice>();

            });static VerdureConfig LoadConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var config = new VerdureConfig();
        configuration.Bind(config);
        
        return config;
    }

    /// <summary>
    /// Initialize IoT devices and setup integration (similar to py-xiaozhi's _initialize_iot_devices)
    /// </summary>
    static void InitializeIoTDevices(IServiceProvider services)
    {
        try
        {
            var logger = services.GetService<ILogger<Program>>();
            logger?.LogInformation("开始初始化IoT设备...");

            // Get required services
            var iotDeviceManager = services.GetService<IoTDeviceManager>();
            var voiceChatService = services.GetService<IVoiceChatService>();
            var musicPlayerDevice = services.GetService<MusicPlayerIoTDevice>();
            var lampDevice = services.GetService<LampIoTDevice>();
            var speakerDevice = services.GetService<SpeakerIoTDevice>();

            if (iotDeviceManager == null)
            {
                logger?.LogError("IoTDeviceManager service not found");
                return;
            }
            if (voiceChatService == null)
            {
                logger?.LogError("VoiceChatService not found");
                return;
            }

            // Add IoT devices to manager (similar to py-xiaozhi's thing_manager.add_thing)
            if (musicPlayerDevice != null)
            {
                iotDeviceManager.AddDevice(musicPlayerDevice);
                logger?.LogInformation("已添加音乐播放器IoT设备");
            }
            if (lampDevice != null)
            {
                iotDeviceManager.AddDevice(lampDevice);
                logger?.LogInformation("已添加智能灯IoT设备");
            }
            if (speakerDevice != null)
            {
                iotDeviceManager.AddDevice(speakerDevice);
                logger?.LogInformation("已添加智能音箱IoT设备");
            }

            // Set IoT device manager on VoiceChatService (similar to py-xiaozhi integration)
            voiceChatService.SetIoTDeviceManager(iotDeviceManager);

            logger?.LogInformation("IoT设备初始化完成，共注册了 {DeviceCount} 个设备", 
                iotDeviceManager.GetDevices().Count);
                
            System.Console.WriteLine($"IoT设备初始化完成，注册了 {iotDeviceManager.GetDevices().Count} 个设备");
        }
        catch (Exception ex)
        {
            var logger = services.GetService<ILogger<Program>>();
            logger?.LogError(ex, "IoT设备初始化失败");
            System.Console.WriteLine($"IoT设备初始化失败: {ex.Message}");
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
