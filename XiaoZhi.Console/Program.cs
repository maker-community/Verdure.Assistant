using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using XiaoZhi.Core.Interfaces;
using XiaoZhi.Core.Models;
using XiaoZhi.Core.Services;

namespace XiaoZhi.Console;

class Program
{
    private static IVoiceChatService? _voiceChatService;
    private static ILogger<Program>? _logger;
    private static XiaoZhiConfig? _config;

    static async Task Main(string[] args)
    {
        // 创建主机
        var host = CreateHostBuilder(args).Build();
        
        _logger = host.Services.GetRequiredService<ILogger<Program>>();
        _voiceChatService = host.Services.GetRequiredService<IVoiceChatService>();

        // 加载配置
        _config = LoadConfiguration();

        System.Console.WriteLine("=== 小智语音聊天客户端 (控制台版) ===");
        System.Console.WriteLine("初始化中...");

        try
        {
            // 注册事件处理器
            _voiceChatService.MessageReceived += OnMessageReceived;
            _voiceChatService.VoiceChatStateChanged += OnVoiceChatStateChanged;
            _voiceChatService.ErrorOccurred += OnErrorOccurred;

            // 初始化服务
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
    }

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });
                
                services.AddSingleton<IVoiceChatService, VoiceChatService>();
            });

    static XiaoZhiConfig LoadConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var config = new XiaoZhiConfig();
        configuration.Bind(config);
        
        return config;
    }

    static async Task ShowMenu()
    {
        while (true)
        {
            System.Console.WriteLine("\n请选择操作:");
            System.Console.WriteLine("1. 开始语音对话");
            System.Console.WriteLine("2. 停止语音对话");
            System.Console.WriteLine("3. 发送文本消息");
            System.Console.WriteLine("4. 查看连接状态");
            System.Console.WriteLine("5. 退出");
            System.Console.Write("请输入选项 (1-5): ");

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
                    await SendTextMessage();
                    break;
                case "4":
                    ShowConnectionStatus();
                    break;
                case "5":
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
    }

    static void ShowConnectionStatus()
    {
        if (_voiceChatService == null)
        {
            System.Console.WriteLine("服务未初始化");
            return;
        }

        System.Console.WriteLine($"连接状态: {(_voiceChatService.IsConnected ? "已连接" : "未连接")}");
        System.Console.WriteLine($"语音对话状态: {(_voiceChatService.IsVoiceChatActive ? "进行中" : "未开始")}");
        System.Console.WriteLine($"通信协议: {(_config?.UseWebSocket == true ? "WebSocket" : "MQTT")}");
        System.Console.WriteLine($"语音功能: {(_config?.EnableVoice == true ? "启用" : "禁用")}");
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
}
