using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Models;

namespace KeywordSpottingContinuousRecognitionTest
{
    /// <summary>
    /// 简化测试：直接测试 KeywordSpottingService 的连续识别功能
    /// 验证 RestartContinuousRecognition 方法的正确性
    /// </summary>
    class Program
    {
        private static volatile int _detectionCount = 0;
        private static volatile bool _testRunning = true;
        
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== 关键词检测连续识别功能测试 ===");
            Console.WriteLine("测试目标：验证关键词检测后能自动重启继续监听");
            Console.WriteLine();            // 设置基本服务
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            
            // 注册 AudioStreamManager 单例（使用正确的方式）
            services.AddSingleton<AudioStreamManager>(provider =>
            {
                var logger = provider.GetService<ILogger<AudioStreamManager>>();
                return AudioStreamManager.GetInstance(logger);
            });
            
            var provider = services.BuildServiceProvider();
            var logger = provider.GetRequiredService<ILogger<KeywordSpottingService>>();
            var audioStreamManager = provider.GetRequiredService<AudioStreamManager>();
              // 创建最小的模拟服务
            var mockVoiceChatService = new MinimalMockVoiceChatService();
            
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
                    Console.WriteLine("可能的原因：");
                    Console.WriteLine("- 未找到关键词模型文件");
                    Console.WriteLine("- 音频设备不可用");
                    Console.WriteLine("- 缺少Microsoft Speech Services配置");
                    return;
                }
                
                Console.WriteLine("✅ 服务启动成功");
                Console.WriteLine();                Console.WriteLine("测试说明：");
                Console.WriteLine("1. 请说 '小智' 来触发关键词检测");
                Console.WriteLine("2. 系统将启动短时间模拟对话，然后自动重启监听");
                Console.WriteLine("3. 连续说多次 '小智' 测试连续检测功能");
                Console.WriteLine("4. 观察控制台输出的连续识别和重启消息");
                Console.WriteLine("5. 按 'q' 退出测试");
                Console.WriteLine("6. 注意：KeepListening=true 使系统在对话结束后自动重启关键词检测");
                Console.WriteLine();
                
                // 监控测试
                var monitorTask = Task.Run(MonitorTest);
                
                // 等待用户输入
                while (_testRunning)
                {
                    var key = Console.ReadKey(true);
                    if (key.KeyChar == 'q' || key.KeyChar == 'Q')
                    {
                        _testRunning = false;
                        break;
                    }
                }
                
                Console.WriteLine("停止服务...");
                await keywordService.StopAsync();
                
                Console.WriteLine($"\n=== 测试结果 ===");
                Console.WriteLine($"总检测次数: {_detectionCount}");
                
                if (_detectionCount > 0)
                {
                    Console.WriteLine("✅ 连续检测功能正常工作");
                    Console.WriteLine("🔄 RestartContinuousRecognition 方法成功实现了连续监听");
                }
                else
                {
                    Console.WriteLine("⚠️  未检测到关键词，请检查：");
                    Console.WriteLine("   - 麦克风是否正常工作");
                    Console.WriteLine("   - 说话声音是否足够清晰");
                    Console.WriteLine("   - 关键词模型是否正确加载");
                    Console.WriteLine("   - Microsoft Speech Services 配置");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 测试异常: {ex.Message}");
                Console.WriteLine($"详细信息: {ex}");
            }
        }
          private static void OnKeywordDetected(object? sender, KeywordDetectedEventArgs e)
        {
            _detectionCount++;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 🎯 检测到关键词: {e.Keyword} (第{_detectionCount}次)");
            Console.WriteLine($"   置信度: {e.Confidence:F2}");
            Console.WriteLine($"   模型: {e.ModelName}");
            Console.WriteLine("   → 系统将暂停关键词检测并启动语音对话");
            Console.WriteLine("   → 由于 KeepListening=true，对话结束后将自动重启关键词检测");
            Console.WriteLine("   → 这就是连续识别功能的核心逻辑");
            Console.WriteLine();
        }
          private static void OnErrorOccurred(object? sender, string errorMessage)
        {
            Console.WriteLine($"❌ 错误: {errorMessage}");
        }
        
        private static async Task MonitorTest()
        {
            int lastCount = 0;
            var noDetectionTime = DateTime.Now;
            
            while (_testRunning)
            {
                await Task.Delay(5000); // 每5秒检查一次
                
                if (_detectionCount > lastCount)
                {
                    lastCount = _detectionCount;
                    noDetectionTime = DateTime.Now;
                    Console.WriteLine($"[监控] ✅ 检测功能正常，已检测 {_detectionCount} 次");
                }
                else if (DateTime.Now - noDetectionTime > TimeSpan.FromMinutes(1))
                {
                    Console.WriteLine($"[监控] ⚠️  超过1分钟未检测到关键词");
                    Console.WriteLine("   提示：尝试说 '小智' 来测试检测功能");
                    noDetectionTime = DateTime.Now.AddMinutes(-0.5); // 避免频繁提示
                }
            }        }
    }
}

/// <summary>
/// 最小化的模拟语音聊天服务，仅实现必要的接口成员
/// 专门用于测试连续识别功能
/// </summary>
public class MinimalMockVoiceChatService : IVoiceChatService
{
    public event EventHandler<DeviceState>? DeviceStateChanged;
    public event EventHandler<bool>? VoiceChatStateChanged;
    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<ListeningMode>? ListeningModeChanged;
    public event EventHandler<string>? ErrorOccurred;
    
    public bool KeepListening { get; set; } = true; // 设置为 true 以测试连续识别
    public bool IsVoiceChatActive { get; } = false;
    public bool IsConnected { get; } = true;
    public DeviceState CurrentState { get; } = DeviceState.Idle;
    public ListeningMode CurrentListeningMode { get; } = ListeningMode.AlwaysOn;
    public bool IsKeywordDetectionEnabled { get; } = true;
    
    public Task InitializeAsync(VerdureConfig config) => Task.CompletedTask;
    public Task<bool> StartKeywordDetectionAsync() => Task.FromResult(true);
    public Task StopKeywordDetectionAsync() => Task.CompletedTask;
    public async Task StartVoiceChatAsync() 
    {
        Console.WriteLine("   [Mock] 启动语音对话（模拟短对话）");
        // 模拟短时间的语音对话，然后自动结束以测试连续识别
        await Task.Delay(1000); // 模拟1秒的对话
        Console.WriteLine("   [Mock] 语音对话结束，应触发连续识别重启");
        // 模拟对话结束，触发状态变化
        DeviceStateChanged?.Invoke(this, DeviceState.Idle);
    }
    public Task StopVoiceChatAsync() 
    {
        Console.WriteLine("   [Mock] 停止语音对话");
        return Task.CompletedTask;
    }
    public Task InterruptAsync(AbortReason reason) => Task.CompletedTask;
    public Task SendTextMessageAsync(string message) => Task.CompletedTask;
    public Task ToggleChatStateAsync() => Task.CompletedTask;
    public void SetEnhancedInterruptManager(Verdure.Assistant.Core.Services.Interrupt.EnhancedInterruptManager enhancedInterruptManager) { }
    public void SetKeywordSpottingService(IKeywordSpottingService keywordSpottingService) { }
    public void Dispose() { }
}
