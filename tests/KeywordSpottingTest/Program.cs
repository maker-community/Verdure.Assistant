using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Events;

namespace KeywordSpottingTest;

/// <summary>
/// 关键词唤醒功能测试：验证 Microsoft Cognitive Services 关键词检测
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== 关键词唤醒功能测试 ===");
        Console.WriteLine("本测试验证 Microsoft Cognitive Services 关键词检测功能");
        Console.WriteLine("注意：需要在 Assets/keywords 目录中放置 .table 关键词模型文件");
        Console.WriteLine();

        // 配置服务
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        await host.StartAsync();

        // 获取服务
        var audioStreamManager = host.Services.GetRequiredService<AudioStreamManager>();
        var keywordSpottingService = host.Services.GetRequiredService<IKeywordSpottingService>();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            Console.WriteLine("步骤 1: 检查关键词模型文件");
            var keywordModelsPath = GetKeywordModelsPath();
            if (!Directory.Exists(keywordModelsPath))
            {
                Console.WriteLine($"❌ 关键词模型目录不存在: {keywordModelsPath}");
                Console.WriteLine("请创建目录并放置 .table 关键词模型文件");
                return;
            }

            var tableFiles = Directory.GetFiles(keywordModelsPath, "*.table");
            if (tableFiles.Length == 0)
            {
                Console.WriteLine($"❌ 在 {keywordModelsPath} 中未找到 .table 关键词模型文件");
                Console.WriteLine("请下载并放置关键词模型文件，如：");
                Console.WriteLine("- keyword_xiaodian.table");
                Console.WriteLine("- keyword_cortana.table");
                return;
            }

            Console.WriteLine($"✓ 找到 {tableFiles.Length} 个关键词模型文件:");
            foreach (var file in tableFiles)
            {
                Console.WriteLine($"  - {Path.GetFileName(file)}");
            }
            Console.WriteLine();

            Console.WriteLine("步骤 2: 启动共享音频流");
            await audioStreamManager.StartRecordingAsync();
            Console.WriteLine("✓ 共享音频流已启动");
            Console.WriteLine();

            Console.WriteLine("步骤 3: 启动关键词检测服务");
            
            // 订阅关键词检测事件
            bool keywordDetected = false;
            keywordSpottingService.KeywordDetected += (sender, args) =>
            {
                keywordDetected = true;
                Console.WriteLine($"🎉 检测到关键词: '{args.Keyword}'");
                Console.WriteLine($"   完整文本: '{args.FullText}'");
                Console.WriteLine($"   置信度: {args.Confidence:F2}");
                Console.WriteLine($"   模型: {args.ModelName}");
                Console.WriteLine();
            };

            keywordSpottingService.ErrorOccurred += (sender, error) =>
            {
                Console.WriteLine($"❌ 关键词检测错误: {error}");
            };

            // 启动关键词检测
            var startResult = await keywordSpottingService.StartAsync();
            if (!startResult)
            {
                Console.WriteLine("❌ 启动关键词检测失败");
                return;
            }

            Console.WriteLine("✓ 关键词检测服务已启动");
            Console.WriteLine();

            Console.WriteLine("步骤 4: 监听关键词唤醒");
            Console.WriteLine("请对着麦克风说出关键词（如：'Cortana' 或配置的其他关键词）");
            Console.WriteLine("监听时间：30秒");
            Console.WriteLine();

            // 监听30秒
            var startTime = DateTime.Now;
            var timeoutSeconds = 30;
            
            while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
            {
                if (keywordDetected)
                {
                    Console.WriteLine("✓ 关键词检测测试成功！");
                    break;
                }

                // 显示剩余时间
                var remainingSeconds = timeoutSeconds - (int)(DateTime.Now - startTime).TotalSeconds;
                Console.Write($"\r剩余时间: {remainingSeconds}秒");
                
                await Task.Delay(1000);
            }

            if (!keywordDetected)
            {
                Console.WriteLine();
                Console.WriteLine("⚠ 在30秒内未检测到关键词");
                Console.WriteLine("可能的原因：");
                Console.WriteLine("1. 关键词模型文件不正确或不兼容");
                Console.WriteLine("2. 音频格式不匹配（需要16kHz, 16-bit, mono）");
                Console.WriteLine("3. Microsoft Cognitive Services 配置问题");
                Console.WriteLine("4. 麦克风权限或硬件问题");
                Console.WriteLine();
            }

            Console.WriteLine("步骤 5: 停止服务");
            await keywordSpottingService.StopAsync();
            await audioStreamManager.StopRecordingAsync();
            Console.WriteLine("✓ 所有服务已停止");

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "关键词唤醒测试过程中发生错误");
            Console.WriteLine($"❌ 测试失败: {ex.Message}");
            Console.WriteLine($"详细错误: {ex}");
        }
        finally
        {
            audioStreamManager?.Dispose();
        }

        Console.WriteLine();
        Console.WriteLine("按任意键退出...");
        Console.ReadKey();
        
        await host.StopAsync();
    }

    private static string GetKeywordModelsPath()
    {
        // 从当前程序集位置推断Assets路径
        var assemblyPath = AppDomain.CurrentDomain.BaseDirectory;

        // 向上查找到解决方案根目录
        var currentDir = new DirectoryInfo(assemblyPath);
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "Verdure.Assistant.sln")))
        {
            currentDir = currentDir.Parent;
        }

        if (currentDir != null)
        {
            return Path.Combine(currentDir.FullName, "src", "Verdure.Assistant.WinUI", "Assets", "keywords");
        }

        // 如果找不到解决方案目录，使用相对路径
        return Path.Combine(assemblyPath, "..", "..", "..", "..", "..", "src", "Verdure.Assistant.WinUI", "Assets", "keywords");
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // 日志
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 音频服务
        services.AddSingleton<AudioStreamManager>(provider =>
        {
            var logger = provider.GetService<ILogger<AudioStreamManager>>();
            return AudioStreamManager.GetInstance(logger);
        });
        
        services.AddSingleton<IAudioRecorder>(provider => provider.GetService<AudioStreamManager>()!);

        // 模拟 VoiceChatService（关键词检测服务需要）
        services.AddSingleton<IVoiceChatService, MockVoiceChatService>();

        // 关键词检测服务
        services.AddSingleton<IKeywordSpottingService, KeywordSpottingService>();
    }
}

/// <summary>
/// 模拟的语音聊天服务，用于测试
/// </summary>
public class MockVoiceChatService : IVoiceChatService
{
    public event EventHandler<DeviceState>? DeviceStateChanged;
    public event EventHandler<DeviceStateChangedEventArgs>? ConversationStarted;
    public event EventHandler<ConversationEventArgs>? ConversationEnded;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<VoiceChatStateChangedEventArgs>? VoiceChatStateChanged;
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<ListeningModeChangedEventArgs>? ListeningModeChanged;

    public bool IsListening => false;
    public bool IsSpeaking => false;
    public DeviceState CurrentState => DeviceState.Idle;
    public bool IsVoiceChatActive => false;
    public bool IsConnected => true;
    public bool KeepListening => false;
    public ListeningMode CurrentListeningMode => ListeningMode.Manual;
    public bool IsKeywordDetectionEnabled => true;

    public Task<bool> InitializeAsync(VerdureConfig config) => Task.FromResult(true);
    public Task<bool> StartListeningAsync() => Task.FromResult(true);
    public Task StopListeningAsync() => Task.CompletedTask;
    public Task<bool> StartSpeakingAsync(string text) => Task.FromResult(true);
    public Task StopSpeakingAsync() => Task.CompletedTask;
    public Task<string> ProcessVoiceInputAsync(byte[] audioData) => Task.FromResult("");
    public Task<bool> StartVoiceChatAsync() => Task.FromResult(true);
    public Task StopVoiceChatAsync() => Task.CompletedTask;
    public Task InterruptAsync(AbortReason reason) => Task.CompletedTask;
    public Task SendTextMessageAsync(string message) => Task.CompletedTask;
    public Task ToggleChatStateAsync() => Task.CompletedTask;
    public void SetEnhancedInterruptManager(Verdure.Assistant.Core.Services.Interrupt.EnhancedInterruptManager enhancedInterruptManager) { }
    public void SetKeywordSpottingService(IKeywordSpottingService keywordSpottingService) { }
    public Task StartKeywordDetectionAsync() => Task.CompletedTask;
    public void StopKeywordDetection() { }
    public void Dispose() { }
}
