using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Services.MCP;

namespace KeywordSpottingIntegrationTest;

/// <summary>
/// 关键词唤醒功能集成测试
/// 测试关键词检测的音频流推送逻辑是否正常工作
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== 关键词唤醒功能集成测试 ===");
        Console.WriteLine("本测试检查关键词检测是否能正确接收和处理音频数据");
        Console.WriteLine("注意：需要在 Assets/keywords 目录下有关键词模型文件");
        Console.WriteLine();

        // 配置服务
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        await host.StartAsync();

        // 获取服务
        var audioStreamManager = host.Services.GetRequiredService<AudioStreamManager>();
        var keywordSpottingService = host.Services.GetRequiredService<IKeywordSpottingService>();
        var mockVoiceChatService = host.Services.GetRequiredService<IVoiceChatService>() as MockVoiceChatService;
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            // 测试 1: 验证关键词模型文件是否存在
            Console.WriteLine("测试 1: 检查关键词模型文件");
            CheckKeywordModelFiles();
            
            // 测试 2: 启动关键词检测
            Console.WriteLine("\n测试 2: 启动关键词检测服务");
            
            // 设置事件处理
            bool keywordDetected = false;
            bool errorOccurred = false;
            string detectedKeyword = "";
            string errorMessage = "";

            keywordSpottingService.KeywordDetected += (sender, e) =>
            {
                keywordDetected = true;
                detectedKeyword = e.Keyword;
                Console.WriteLine($"🎯 关键词检测成功: {e.Keyword}");
            };

            keywordSpottingService.ErrorOccurred += (sender, e) =>
            {
                errorOccurred = true;
                errorMessage = e;
                Console.WriteLine($"❌ 关键词检测错误: {e}");
            };

            // 启动关键词检测
            Console.WriteLine("启动关键词检测...");
            bool started = await keywordSpottingService.StartAsync();
            
            if (!started)
            {
                Console.WriteLine("❌ 关键词检测启动失败");
                return;
            }
            
            Console.WriteLine("✓ 关键词检测启动成功");
            
            // 测试 3: 验证音频流推送
            Console.WriteLine("\n测试 3: 验证音频流推送逻辑");
            
            // 统计音频数据推送
            int audioDataCount = 0;
            EventHandler<byte[]> audioMonitor = (sender, data) =>
            {
                audioDataCount++;
                if (audioDataCount % 50 == 0) // 每50个数据包打印一次
                {
                    Console.WriteLine($"📊 已推送 {audioDataCount} 个音频数据包 (最新: {data.Length} 字节)");
                }
            };
            
            audioStreamManager.SubscribeToAudioData(audioMonitor);
            
            // 运行30秒，等待关键词检测
            Console.WriteLine("🎤 开始监听关键词，请说出唤醒词...");
            Console.WriteLine("建议的测试词汇：");
            Console.WriteLine("- 你好小天");
            Console.WriteLine("- Hey Cortana");
            Console.WriteLine("- Computer");
            Console.WriteLine();
            
            for (int i = 30; i > 0; i--)
            {
                Console.WriteLine($"倒计时: {i} 秒 (音频包: {audioDataCount}, 关键词: {(keywordDetected ? detectedKeyword : "无")})");
                await Task.Delay(1000);
                
                if (keywordDetected)
                {
                    Console.WriteLine("🎉 关键词检测测试成功！");
                    break;
                }
                
                if (errorOccurred)
                {
                    Console.WriteLine($"⚠️ 检测到错误: {errorMessage}");
                    break;
                }
            }
            
            audioStreamManager.UnsubscribeFromAudioData(audioMonitor);
            
            // 测试结果总结
            Console.WriteLine("\n=== 测试结果总结 ===");
            Console.WriteLine($"关键词检测状态: {(keywordSpottingService.IsRunning ? "运行中" : "已停止")}");
            Console.WriteLine($"音频数据包总数: {audioDataCount}");
            Console.WriteLine($"关键词检测结果: {(keywordDetected ? $"成功 - {detectedKeyword}" : "未检测到")}");
            Console.WriteLine($"错误状态: {(errorOccurred ? $"有错误 - {errorMessage}" : "无错误")}");
            
            if (audioDataCount > 0 && !errorOccurred)
            {
                Console.WriteLine("✅ 音频流推送正常工作");
                if (keywordDetected)
                {
                    Console.WriteLine("✅ 关键词检测功能正常");
                }
                else
                {
                    Console.WriteLine("⚠️ 音频流正常但未检测到关键词（可能是模型文件问题或语音不清晰）");
                }
            }
            else
            {
                Console.WriteLine("❌ 音频流推送或关键词检测存在问题");
            }            // 停止关键词检测
            await keywordSpottingService.StopAsync();
            Console.WriteLine("关键词检测已停止");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "测试过程中发生错误");
            Console.WriteLine($"❌ 测试失败: {ex.Message}");
        }
        
        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
        
        await host.StopAsync();
    }

    private static void CheckKeywordModelFiles()
    {
        try
        {
            // 查找关键词模型文件
            var currentDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            DirectoryInfo? solutionDir = null;
            
            // 向上查找解决方案目录
            while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "Verdure.Assistant.sln")))
            {
                currentDir = currentDir.Parent;
            }
            
            solutionDir = currentDir;
            
            if (solutionDir != null)
            {
                var assetsPath = Path.Combine(solutionDir.FullName, "src", "Verdure.Assistant.WinUI", "Assets");
                var keywordsPath = Path.Combine(assetsPath, "keywords");
                
                Console.WriteLine($"Assets 路径: {assetsPath}");
                Console.WriteLine($"Keywords 路径: {keywordsPath}");
                
                if (Directory.Exists(keywordsPath))
                {
                    var tableFiles = Directory.GetFiles(keywordsPath, "*.table");
                    Console.WriteLine($"找到 {tableFiles.Length} 个关键词模型文件:");
                    foreach (var file in tableFiles)
                    {
                        var fileInfo = new FileInfo(file);
                        Console.WriteLine($"  - {Path.GetFileName(file)} ({fileInfo.Length} bytes)");
                    }
                    
                    if (tableFiles.Length == 0)
                    {
                        Console.WriteLine("⚠️ 未找到 .table 关键词模型文件");
                        Console.WriteLine("请确保在 Assets/keywords 目录下有关键词模型文件");
                    }
                }
                else
                {
                    Console.WriteLine($"❌ Keywords 目录不存在: {keywordsPath}");
                }
            }
            else
            {
                Console.WriteLine("❌ 无法找到解决方案目录");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 检查关键词模型文件时出错: {ex.Message}");
        }
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

        // 模拟语音聊天服务
        services.AddSingleton<IVoiceChatService, MockVoiceChatService>();
        
        // 关键词检测服务
        services.AddSingleton<IKeywordSpottingService>(provider =>
        {
            var voiceChatService = provider.GetRequiredService<IVoiceChatService>();
            var audioStreamManager = provider.GetRequiredService<AudioStreamManager>();
            var logger = provider.GetService<ILogger<KeywordSpottingService>>();
            return new KeywordSpottingService(audioStreamManager, logger);
        });
    }
}

/// <summary>
/// 模拟语音聊天服务，用于测试
/// </summary>
public class MockVoiceChatService : IVoiceChatService
{
    // Protocol message events
    public event EventHandler<MusicMessage>? MusicMessageReceived;
    public event EventHandler<SystemStatusMessage>? SystemStatusMessageReceived;
    public event EventHandler<LlmMessage>? LlmMessageReceived;
    public event EventHandler<TtsMessage>? TtsStateChanged;
    
    public event EventHandler<bool>? VoiceChatStateChanged;
    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<DeviceState>? DeviceStateChanged;
    public event EventHandler<ListeningMode>? ListeningModeChanged;

    public bool IsVoiceChatActive { get; private set; } = false;
    public bool IsConnected { get; private set; } = true;
    public bool KeepListening { get; set; } = false;
    public DeviceState CurrentState { get; private set; } = DeviceState.Idle;
    public ListeningMode CurrentListeningMode { get; private set; } = ListeningMode.Manual;
    public bool IsKeywordDetectionEnabled { get; private set; } = true;

    public ConversationStateMachine? StateMachine => throw new NotImplementedException();

    public Task InitializeAsync(VerdureConfig config)
    {
        return Task.FromResult(true);
    }

    public Task StartVoiceChatAsync()
    {
        IsVoiceChatActive = true;
        VoiceChatStateChanged?.Invoke(this, true);
        return Task.CompletedTask;
    }

    public Task StopVoiceChatAsync()
    {
        IsVoiceChatActive = false;
        VoiceChatStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public Task InterruptAsync(AbortReason reason = AbortReason.UserInterruption)
    {
        return Task.CompletedTask;
    }

    public Task SendTextMessageAsync(string text)
    {
        return Task.CompletedTask;
    }

    public Task ToggleChatStateAsync()
    {
        return Task.CompletedTask;
    }

    public void SetInterruptManager(InterruptManager interruptManager)
    {
        // 模拟实现
    }    public void SetKeywordSpottingService(IKeywordSpottingService keywordSpottingService)
    {
        // 模拟实现
    }
    public Task<bool> StartKeywordDetectionAsync()
    {
        return Task.FromResult(true);
    }
    public Task StopKeywordDetectionAsync()
    {
        // 模拟实现
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // 模拟实现
    }

    public Task<bool> SwitchKeywordModelAsync(string modelFileName)
    {
        throw new NotImplementedException();
    }

    // 添加缺少的接口方法
    public void TriggerApiInterrupt(string reason, object? data = null)
    {
        // 模拟实现
    }

    public void SetInterruptService(Verdure.Assistant.Core.Services.Interrupt.IInterruptService interruptService)
    {
        // 模拟实现
    }
}
