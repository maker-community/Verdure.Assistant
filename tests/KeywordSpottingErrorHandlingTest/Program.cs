using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Models;

namespace KeywordSpottingErrorHandlingTest
{
    /// <summary>
    /// 专门测试 SPXERR_INVALID_HANDLE 错误处理的项目
    /// 验证修复后的 RestartContinuousRecognition 方法能正确处理 Microsoft Speech SDK 的句柄错误
    /// </summary>
    class Program
    {
        private static volatile int _detectionCount = 0;
        private static volatile int _errorCount = 0;
        private static volatile bool _testRunning = true;
        
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Microsoft Speech SDK 错误处理测试 ===");
            Console.WriteLine("测试目标：验证 SPXERR_INVALID_HANDLE 错误的处理和恢复机制");
            Console.WriteLine("此测试将模拟快速连续的关键词检测以触发潜在的句柄错误");
            Console.WriteLine();

            // 设置基本服务
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            
            // 注册 AudioStreamManager 单例
            services.AddSingleton<AudioStreamManager>(provider =>
            {
                var logger = provider.GetService<ILogger<AudioStreamManager>>();
                return AudioStreamManager.GetInstance(logger);
            });

            var provider = services.BuildServiceProvider();
            var logger = provider.GetRequiredService<ILogger<KeywordSpottingService>>();
            var audioStreamManager = provider.GetRequiredService<AudioStreamManager>();
              
            // 创建专门的模拟服务用于错误测试
            var mockVoiceChatService = new ErrorTestMockVoiceChatService();
            
            // 直接创建 KeywordSpottingService
            var keywordService = new KeywordSpottingService(mockVoiceChatService, audioStreamManager, logger);
            
            // 订阅事件
            keywordService.KeywordDetected += OnKeywordDetected;
            keywordService.ErrorOccurred += OnErrorOccurred;
            
            try
            {
                Console.WriteLine("启动关键词检测服务...");
                var result = await keywordService.StartAsync();
                
                if (!result)
                {
                    Console.WriteLine("❌ 服务启动失败");
                    return;
                }
                
                Console.WriteLine("✅ 服务启动成功");
                Console.WriteLine();
                Console.WriteLine("测试说明：");
                Console.WriteLine("1. 请连续快速说 '小智' 来触发关键词检测");
                Console.WriteLine("2. 系统将快速重启监听以测试句柄错误处理");
                Console.WriteLine("3. 观察控制台输出中的错误处理和恢复消息");
                Console.WriteLine("4. 按 'q' 退出测试");
                Console.WriteLine("5. 预期：即使出现 SPXERR_INVALID_HANDLE 错误，系统仍能继续工作");
                Console.WriteLine();

                // 启动监控任务
                var monitorTask = MonitorTest();
                
                // 等待用户输入
                string? input;
                do
                {
                    input = Console.ReadLine();
                } while (input?.ToLower() != "q");
                
                _testRunning = false;
                
                Console.WriteLine("\n正在停止服务...");
                await keywordService.StopAsync();
                
                await monitorTask;
                
                Console.WriteLine($"\n=== 测试总结 ===");
                Console.WriteLine($"关键词检测次数: {_detectionCount}");
                Console.WriteLine($"错误处理次数: {_errorCount}");
                
                if (_errorCount > 0 && _detectionCount > _errorCount)
                {
                    Console.WriteLine("✅ 错误处理测试通过：系统能够从错误中恢复并继续工作");
                }
                else if (_errorCount == 0)
                {
                    Console.WriteLine("ℹ️ 未触发预期错误，可能需要更频繁的测试");
                }
                else
                {
                    Console.WriteLine("⚠️ 错误恢复可能存在问题，需要进一步调查");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 测试过程中发生异常: {ex.Message}");
            }
            finally
            {
                provider.Dispose();
            }
        }
        
        private static void OnKeywordDetected(object? sender, KeywordDetectedEventArgs e)
        {
            var count = Interlocked.Increment(ref _detectionCount);
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            
            Console.WriteLine($"[{timestamp}] 🎯 检测到关键词: {e.Keyword} (第{count}次)");
            Console.WriteLine($"   置信度: {e.Confidence:F2}");
            Console.WriteLine($"   模型: {e.ModelName}");
            Console.WriteLine($"   → 快速重启识别以测试错误处理");
        }
        
        private static void OnErrorOccurred(object? sender, string errorMessage)
        {
            var count = Interlocked.Increment(ref _errorCount);
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            
            Console.WriteLine($"[{timestamp}] ⚠️ 错误事件 (第{count}次): {errorMessage}");
            
            if (errorMessage.Contains("SPXERR_INVALID_HANDLE") || errorMessage.Contains("0x21"))
            {
                Console.WriteLine($"   → 这是预期的Microsoft Speech SDK句柄错误");
            }
        }
        
        private static async Task MonitorTest()
        {
            int lastDetectionCount = 0;
            int lastErrorCount = 0;
            
            while (_testRunning)
            {
                await Task.Delay(5000); // 每5秒检查一次
                
                if (_detectionCount != lastDetectionCount || _errorCount != lastErrorCount)
                {
                    Console.WriteLine($"[监控] 检测: {_detectionCount} 次, 错误: {_errorCount} 次");
                    lastDetectionCount = _detectionCount;
                    lastErrorCount = _errorCount;
                }
            }
        }
    }
}

/// <summary>
/// 专门用于错误处理测试的模拟语音聊天服务
/// 快速状态变化以增加触发句柄错误的概率
/// </summary>
public class ErrorTestMockVoiceChatService : IVoiceChatService
{
    public bool KeepListening { get; set; } = true;
    public DeviceState CurrentState { get; private set; } = DeviceState.Idle;
    public ListeningMode CurrentListeningMode { get; private set; } = ListeningMode.Manual;
    public bool IsVoiceChatActive { get; private set; } = false;
    public bool IsConnected { get; private set; } = true;
    public bool IsKeywordDetectionEnabled { get; private set; } = false;

    public event EventHandler<bool>? VoiceChatStateChanged;
    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<DeviceState>? DeviceStateChanged;
    public event EventHandler<ListeningMode>? ListeningModeChanged;

    public Task InitializeAsync(VerdureConfig config) => Task.CompletedTask;
    
    public async Task StartVoiceChatAsync()
    {
        Console.WriteLine("   [Mock] 启动快速模拟对话（测试快速重启）");
        IsVoiceChatActive = true;
        CurrentState = DeviceState.Listening;
        VoiceChatStateChanged?.Invoke(this, true);
        DeviceStateChanged?.Invoke(this, DeviceState.Listening);
        
        // 非常短的对话，快速返回空闲状态以增加重启频率
        await Task.Delay(100); // 极短延迟
        
        Console.WriteLine("   [Mock] 快速结束对话，立即重启检测");
        IsVoiceChatActive = false;
        CurrentState = DeviceState.Idle;
        VoiceChatStateChanged?.Invoke(this, false);
        DeviceStateChanged?.Invoke(this, DeviceState.Idle);
    }

    public Task StopVoiceChatAsync() 
    {
        IsVoiceChatActive = false;
        CurrentState = DeviceState.Idle;
        VoiceChatStateChanged?.Invoke(this, false);
        DeviceStateChanged?.Invoke(this, DeviceState.Idle);
        return Task.CompletedTask;
    }

    public Task InterruptAsync(AbortReason reason = AbortReason.UserInterruption) => Task.CompletedTask;
    public Task SendTextMessageAsync(string text) => Task.CompletedTask;
    public Task ToggleChatStateAsync() => Task.CompletedTask;
    
    public void SetEnhancedInterruptManager(Verdure.Assistant.Core.Services.Interrupt.EnhancedInterruptManager enhancedInterruptManager) { }
    public void SetKeywordSpottingService(IKeywordSpottingService keywordSpottingService) { }
    
    public Task<bool> StartKeywordDetectionAsync() 
    {
        IsKeywordDetectionEnabled = true;
        return Task.FromResult(true);
    }
    
    public Task StopKeywordDetectionAsync() 
    {
        IsKeywordDetectionEnabled = false;
        return Task.CompletedTask;
    }
    
    public void Dispose() { }
}
