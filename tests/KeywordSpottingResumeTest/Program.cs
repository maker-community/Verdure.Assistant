using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Models;

namespace KeywordSpottingResumeTest;

/// <summary>
/// Resume方法修复验证测试：验证暂停和恢复机制的正确性
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== KeywordSpotting Resume 方法修复验证测试 ===");
        Console.WriteLine();

        // 创建服务容器
        var services = new ServiceCollection();
        ConfigureServices(services);

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            // 获取服务
            var audioStreamManager = serviceProvider.GetRequiredService<AudioStreamManager>();
            var voiceChatService = serviceProvider.GetRequiredService<IVoiceChatService>();
            var keywordSpottingService = serviceProvider.GetRequiredService<IKeywordSpottingService>();

            Console.WriteLine("步骤 1: 检查关键词模型文件");
            var keywordModelsPath = GetKeywordModelsPath();
            var tableFiles = Directory.GetFiles(keywordModelsPath, "*.table");
            
            if (tableFiles.Length == 0)
            {
                Console.WriteLine("❌ 未找到关键词模型文件，请确保以下文件存在:");
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
            int keywordDetectionCount = 0;
            keywordSpottingService.KeywordDetected += (sender, args) =>
            {
                keywordDetectionCount++;
                Console.WriteLine($"🎉 检测到关键词 #{keywordDetectionCount}: '{args.Keyword}'");
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

            Console.WriteLine("步骤 4: 测试暂停和恢复机制");
            Console.WriteLine("请对着麦克风说出关键词（如：'Cortana'）");
            Console.WriteLine("等待5秒后将自动测试暂停/恢复功能...");

            // 监听5秒
            await Task.Delay(5000);

            Console.WriteLine("\n--- 测试暂停功能 ---");
            keywordSpottingService.Pause();
            Console.WriteLine("✓ 关键词检测已暂停");
            Console.WriteLine("现在说话应该不会触发关键词检测（等待3秒）...");
            await Task.Delay(3000);

            Console.WriteLine("\n--- 测试恢复功能 ---");
            keywordSpottingService.Resume();
            Console.WriteLine("✓ 关键词检测已恢复");
            Console.WriteLine("现在说出关键词应该能够再次检测到（等待5秒）...");
            await Task.Delay(5000);

            Console.WriteLine("\n--- 再次测试暂停/恢复循环 ---");
            keywordSpottingService.Pause();
            Console.WriteLine("✓ 再次暂停");
            await Task.Delay(2000);

            keywordSpottingService.Resume();
            Console.WriteLine("✓ 再次恢复");
            await Task.Delay(3000);

            Console.WriteLine($"\n=== 测试结果 ===");
            Console.WriteLine($"总共检测到关键词: {keywordDetectionCount} 次");
            Console.WriteLine("Resume 方法修复验证完成！");

            // 清理资源
            await keywordSpottingService.StopAsync();
            await audioStreamManager.StopRecordingAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "测试过程中发生错误");
            Console.WriteLine($"❌ 测试失败: {ex.Message}");
        }
    }    private static string GetKeywordModelsPath()
    {
        // 检查多个可能的路径
        var possiblePaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets"),
            Path.Combine(Environment.CurrentDirectory, "Assets"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "assets"),
            @"c:\Users\gil\Music\github\xiaozhi-dotnet\assets",
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "src", "Verdure.Assistant.WinUI", "Assets", "keywords"),
            @"c:\Users\gil\Music\github\xiaozhi-dotnet\src\Verdure.Assistant.WinUI\Assets\keywords"
        };

        foreach (var path in possiblePaths)
        {
            if (Directory.Exists(path))
            {
                var tableFiles = Directory.GetFiles(path, "*.table");
                if (tableFiles.Length > 0)
                {
                    return path;
                }
            }
        }

        // 如果都不存在，返回默认路径
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
    }private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<IVoiceChatService, MockVoiceChatService>();
        
        // 注册 AudioStreamManager 单例（使用与其他项目相同的方式）
        services.AddSingleton<AudioStreamManager>(provider =>
        {
            var logger = provider.GetService<ILogger<AudioStreamManager>>();
            return AudioStreamManager.GetInstance(logger);
        });
        
        services.AddSingleton<IKeywordSpottingService, KeywordSpottingService>();
    }
}

/// <summary>
/// 模拟的语音聊天服务，用于测试
/// </summary>
public class MockVoiceChatService : IVoiceChatService
{
    public event EventHandler<DeviceState>? DeviceStateChanged;
    public event EventHandler<bool>? VoiceChatStateChanged;
    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<ListeningMode>? ListeningModeChanged;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsVoiceChatActive { get; } = false;
    public bool IsConnected { get; } = true;
    public bool KeepListening { get; set; } = false;
    public DeviceState CurrentState { get; } = DeviceState.Idle;
    public ListeningMode CurrentListeningMode { get; } = ListeningMode.Manual;
    public bool IsKeywordDetectionEnabled { get; } = true;

    public Task InitializeAsync(VerdureConfig config) => Task.CompletedTask;
    public Task StartVoiceChatAsync() => Task.CompletedTask;
    public Task StopVoiceChatAsync() => Task.CompletedTask;
    public Task InterruptAsync(AbortReason reason) => Task.CompletedTask;
    public Task SendTextMessageAsync(string message) => Task.CompletedTask;
    public Task ToggleChatStateAsync() => Task.CompletedTask;
    public void SetEnhancedInterruptManager(Verdure.Assistant.Core.Services.Interrupt.EnhancedInterruptManager enhancedInterruptManager) { }
    public void SetKeywordSpottingService(IKeywordSpottingService keywordSpottingService) { }
    public Task<bool> StartKeywordDetectionAsync() => Task.FromResult(true);
    public Task StopKeywordDetectionAsync() => Task.CompletedTask;
    public void Dispose() { }
}
