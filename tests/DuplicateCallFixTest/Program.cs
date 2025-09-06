using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Models;

namespace DuplicateCallFixTest;

/// <summary>
/// 重复调用修复验证测试：验证关键词检测不会导致重复的StartVoiceChatAsync调用
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== 重复调用修复验证测试 ===");
        Console.WriteLine("验证关键词检测只通过VoiceChatService事件处理器调用StartVoiceChatAsync");
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
            var voiceChatService = serviceProvider.GetRequiredService<MockVoiceChatService>();
            var keywordSpottingService = serviceProvider.GetRequiredService<IKeywordSpottingService>();

            // 设置关键词检测服务到语音聊天服务
            voiceChatService.SetKeywordSpottingService(keywordSpottingService);

            Console.WriteLine("步骤 1: 启动音频流");
            await audioStreamManager.StartRecordingAsync();
            Console.WriteLine("✓ 音频流已启动");

            Console.WriteLine("\n步骤 2: 启动关键词检测");
            var success = await keywordSpottingService.StartAsync();
            if (!success)
            {
                Console.WriteLine("❌ 关键词检测启动失败");
                return;
            }
            Console.WriteLine("✓ 关键词检测已启动");

            Console.WriteLine("\n步骤 3: 模拟关键词检测");
            Console.WriteLine("当检测到关键词时，只有VoiceChatService的事件处理器应该调用StartVoiceChatAsync");
            
            // 等待一段时间让关键词检测工作
            Console.WriteLine("监听3秒...");
            await Task.Delay(3000);

            Console.WriteLine("\n=== 测试结果 ===");
            Console.WriteLine($"StartVoiceChatAsync 调用次数: {voiceChatService.StartVoiceChatAsyncCallCount}");
            Console.WriteLine($"StopVoiceChatAsync 调用次数: {voiceChatService.StopVoiceChatAsyncCallCount}");
            
            if (voiceChatService.StartVoiceChatAsyncCallCount <= 1 && voiceChatService.StopVoiceChatAsyncCallCount <= 1)
            {
                Console.WriteLine("✅ 测试通过：没有重复调用");
            }
            else
            {
                Console.WriteLine("❌ 测试失败：检测到重复调用");
            }

            // 清理
            await keywordSpottingService.StopAsync();
            await audioStreamManager.StopRecordingAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "测试过程中发生错误");
            Console.WriteLine($"❌ 测试失败: {ex.Message}");
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<MockVoiceChatService>();
        services.AddSingleton<IVoiceChatService>(provider => provider.GetRequiredService<MockVoiceChatService>());
        
        // 注册 AudioStreamManager 单例
        services.AddSingleton<AudioStreamManager>(provider =>
        {
            var logger = provider.GetService<ILogger<AudioStreamManager>>();
            return AudioStreamManager.GetInstance(logger);
        });
        
        services.AddSingleton<IKeywordSpottingService, KeywordSpottingService>();
    }
}

/// <summary>
/// 监控调用次数的模拟语音聊天服务
/// </summary>
public class MockVoiceChatService : IVoiceChatService
{
    public event EventHandler<DeviceState>? DeviceStateChanged;
    public event EventHandler<bool>? VoiceChatStateChanged;
    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<ListeningMode>? ListeningModeChanged;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsVoiceChatActive { get; private set; } = false;
    public bool IsConnected { get; } = true;
    public bool KeepListening { get; set; } = false;
    public DeviceState CurrentState { get; private set; } = DeviceState.Idle;
    public ListeningMode CurrentListeningMode { get; } = ListeningMode.Manual;
    public bool IsKeywordDetectionEnabled { get; } = true;

    // 调用计数器
    public int StartVoiceChatAsyncCallCount { get; private set; } = 0;
    public int StopVoiceChatAsyncCallCount { get; private set; } = 0;

    private IKeywordSpottingService? _keywordSpottingService;

    public Task InitializeAsync(VerdureConfig config) => Task.CompletedTask;

    public Task StartVoiceChatAsync()
    {
        StartVoiceChatAsyncCallCount++;
        Console.WriteLine($"🔥 StartVoiceChatAsync 被调用 (第 {StartVoiceChatAsyncCallCount} 次)");
        IsVoiceChatActive = true;
        CurrentState = DeviceState.Listening;
        VoiceChatStateChanged?.Invoke(this, true);
        DeviceStateChanged?.Invoke(this, CurrentState);
        return Task.CompletedTask;
    }

    public Task StopVoiceChatAsync()
    {
        StopVoiceChatAsyncCallCount++;
        Console.WriteLine($"🛑 StopVoiceChatAsync 被调用 (第 {StopVoiceChatAsyncCallCount} 次)");
        IsVoiceChatActive = false;
        CurrentState = DeviceState.Idle;
        VoiceChatStateChanged?.Invoke(this, false);
        DeviceStateChanged?.Invoke(this, CurrentState);
        return Task.CompletedTask;
    }

    public Task InterruptAsync(AbortReason reason) => Task.CompletedTask;
    public Task SendTextMessageAsync(string message) => Task.CompletedTask;
    public Task ToggleChatStateAsync() => Task.CompletedTask;
    public void SetEnhancedInterruptManager(Verdure.Assistant.Core.Services.Interrupt.EnhancedInterruptManager enhancedInterruptManager) { }
    
    public void SetKeywordSpottingService(IKeywordSpottingService keywordSpottingService)
    {
        _keywordSpottingService = keywordSpottingService;
        
        // 订阅关键词检测事件 - 这里应该是唯一调用StartVoiceChatAsync的地方
        _keywordSpottingService.KeywordDetected += OnKeywordDetected;
        _keywordSpottingService.ErrorOccurred += OnKeywordDetectionError;
    }

    private void OnKeywordDetected(object? sender, KeywordDetectedEventArgs e)
    {
        Console.WriteLine($"📢 VoiceChatService收到关键词检测事件: {e.Keyword}");
        
        // 模拟VoiceChatService的HandleKeywordDetectedAsync逻辑
        Task.Run(async () => await HandleKeywordDetectedAsync(e.Keyword));
    }

    private async Task HandleKeywordDetectedAsync(string keyword)
    {
        try
        {
            switch (CurrentState)
            {
                case DeviceState.Idle:
                    Console.WriteLine("🎯 在空闲状态检测到关键词，启动语音对话");
                    await Task.Delay(50); // 模拟延迟
                    await StartVoiceChatAsync();
                    break;

                case DeviceState.Speaking:
                    Console.WriteLine("🎯 在说话状态检测到关键词，中断当前对话");
                    await StopVoiceChatAsync();
                    await Task.Delay(50);
                    // 恢复关键词检测
                    _keywordSpottingService?.Resume();
                    break;

                case DeviceState.Listening:
                    Console.WriteLine("🎯 在监听状态检测到关键词，忽略");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 处理关键词检测事件时出错: {ex.Message}");
        }
    }

    private void OnKeywordDetectionError(object? sender, string error)
    {
        Console.WriteLine($"❌ 关键词检测错误: {error}");
    }

    public Task<bool> StartKeywordDetectionAsync() => Task.FromResult(true);
    public Task StopKeywordDetectionAsync() => Task.CompletedTask;
    public void Dispose() { }
}
