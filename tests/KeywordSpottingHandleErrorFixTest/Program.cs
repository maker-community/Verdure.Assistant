using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services.MCP;

namespace KeywordSpottingHandleErrorFixTest
{
    /// <summary>
    /// 测试修复后的关键词检测服务是否还会产生 SPXERR_INVALID_HANDLE 错误
    /// 验证每次重启都创建新实例的修复方案
    /// </summary>
    class Program
    {
        private static volatile int _detectionCount = 0;
        private static volatile int _errorCount = 0;
        private static volatile int _handleErrorCount = 0;
        private static volatile bool _testRunning = true;
        
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== 关键词检测句柄错误修复验证测试 ===");
            Console.WriteLine("测试目标：验证修复后不再出现 SPXERR_INVALID_HANDLE 和 0x21 错误");
            Console.WriteLine("修复方案：每次重启都创建全新的识别器和模型实例");
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
              
            // 创建模拟服务用于测试
            var mockVoiceChatService = new HandleErrorFixTestMockVoiceChatService();
            
            // 创建 KeywordSpottingService
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
                Console.WriteLine("1. 系统将模拟快速连续的关键词检测和重启");
                Console.WriteLine("2. 观察是否还会出现 SPXERR_INVALID_HANDLE 或 0x21 错误");
                Console.WriteLine("3. 每次重启都会创建全新的实例");
                Console.WriteLine("4. 按 'q' 退出测试");
                Console.WriteLine("5. 预期：不再出现句柄错误");
                Console.WriteLine();

                // 启动监控任务
                var monitorTask = MonitorTest();
                
                // 启动自动测试任务
                var autoTestTask = AutomaticRapidRestartTest(keywordService);
                
                // 等待用户输入
                string? input;
                do
                {
                    input = Console.ReadLine();
                } while (input?.ToLower() != "q");

                _testRunning = false;
                
                // 等待监控任务完成
                await monitorTask;
                await autoTestTask;
                
                Console.WriteLine("\n停止关键词检测服务...");
                await keywordService.StopAsync();
                
                Console.WriteLine("\n=== 测试结果 ===");
                Console.WriteLine($"总检测次数: {_detectionCount}");
                Console.WriteLine($"总错误次数: {_errorCount}");
                Console.WriteLine($"句柄错误次数: {_handleErrorCount}");
                
                if (_handleErrorCount == 0)
                {
                    Console.WriteLine("✅ 修复成功！未发现句柄错误");
                }
                else
                {
                    Console.WriteLine("❌ 修复失败！仍然存在句柄错误");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"测试过程中发生异常: {ex.Message}");
            }
            finally
            {
                keywordService?.Dispose();
            }
        }

        /// <summary>
        /// 自动快速重启测试
        /// </summary>
        private static async Task AutomaticRapidRestartTest(IKeywordSpottingService keywordService)
        {
            var testCount = 0;
            
            while (_testRunning && testCount < 50) // 测试50次快速重启
            {
                await Task.Delay(2000); // 等待2秒
                
                if (!_testRunning) break;
                
                try
                {
                    Console.WriteLine($"执行第 {++testCount} 次快速重启测试...");
                    
                    // 快速停止和重启
                    await keywordService.StopAsync();
                    await Task.Delay(100); // 短暂延迟
                    await keywordService.StartAsync();
                    
                    Console.WriteLine($"第 {testCount} 次重启完成");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"第 {testCount} 次重启时发生错误: {ex.Message}");
                    _errorCount++;
                    
                    if (ex.Message.Contains("SPXERR_INVALID_HANDLE") || ex.Message.Contains("0x21"))
                    {
                        _handleErrorCount++;
                        Console.WriteLine("⚠️  检测到句柄错误！");
                    }
                }
            }
            
            Console.WriteLine($"自动测试完成，共执行 {testCount} 次重启");
        }

        private static void OnKeywordDetected(object? sender, KeywordDetectedEventArgs e)
        {
            _detectionCount++;
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.WriteLine($"[{timestamp}] 🎯 检测到关键词: {e.Keyword} (总计: {_detectionCount})");
        }

        private static void OnErrorOccurred(object? sender, string error)
        {
            _errorCount++;
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.WriteLine($"[{timestamp}] ❌ 错误: {error}");
            
            // 检查是否是句柄错误
            if (error.Contains("SPXERR_INVALID_HANDLE") || error.Contains("0x21"))
            {
                _handleErrorCount++;
                Console.WriteLine($"[{timestamp}] 🚨 检测到句柄错误！错误计数: {_handleErrorCount}");
            }
        }

        private static async Task MonitorTest()
        {
            var lastDetectionCount = 0;
            var lastErrorCount = 0;
            
            while (_testRunning)
            {
                await Task.Delay(5000); // 每5秒报告一次状态
                
                if (_detectionCount != lastDetectionCount || _errorCount != lastErrorCount)
                {
                    Console.WriteLine($"[状态] 检测: {_detectionCount}, 错误: {_errorCount}, 句柄错误: {_handleErrorCount}");
                    lastDetectionCount = _detectionCount;
                    lastErrorCount = _errorCount;
                }
            }
        }
    }

    /// <summary>
    /// 用于测试的模拟语音聊天服务
    /// </summary>
    public class HandleErrorFixTestMockVoiceChatService : IVoiceChatService
    {
        public DeviceState CurrentState { get; private set; } = DeviceState.Idle;
        
        public event EventHandler<DeviceState>? DeviceStateChanged;
        public event EventHandler<MusicMessage>? MusicMessageReceived;
        public event EventHandler<SystemStatusMessage>? SystemStatusMessageReceived;
        public event EventHandler<LlmMessage>? LlmMessageReceived;
        public event EventHandler<TtsMessage>? TtsStateChanged;
        public event EventHandler<bool>? VoiceChatStateChanged;
        public event EventHandler<ChatMessage>? MessageReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<ListeningMode>? ListeningModeChanged;

        public bool IsVoiceChatActive { get; private set; } = false;
        public bool IsConnected { get; private set; } = true;
        public bool KeepListening { get; set; } = false;
        public ListeningMode CurrentListeningMode { get; private set; } = ListeningMode.AlwaysOn;
        public bool IsKeywordDetectionEnabled { get; private set; } = true;

        public Task InitializeAsync(VerdureConfig config)
        {
            return Task.CompletedTask;
        }

        public Task StartVoiceChatAsync()
        {
            IsVoiceChatActive = true;
            ChangeState(DeviceState.Listening);
            return Task.CompletedTask;
        }

        public Task StopVoiceChatAsync()
        {
            IsVoiceChatActive = false;
            ChangeState(DeviceState.Idle);
            return Task.CompletedTask;
        }

        public Task InterruptAsync(AbortReason reason = AbortReason.UserInterruption)
        {
            ChangeState(DeviceState.Idle);
            return Task.CompletedTask;
        }

        public Task SendTextMessageAsync(string text)
        {
            return Task.CompletedTask;
        }

        public Task ToggleChatStateAsync()
        {
            KeepListening = !KeepListening;
            return Task.CompletedTask;
        }

        public void SetInterruptManager(InterruptManager interruptManager)
        {
            // Mock implementation
        }

        public void SetKeywordSpottingService(IKeywordSpottingService keywordSpottingService)
        {
            // Mock implementation
        }

        public Task<bool> StartKeywordDetectionAsync()
        {
            return Task.FromResult(true);
        }

        public Task StopKeywordDetectionAsync()
        {
            return Task.CompletedTask;
        }

        public void SetMcpIntegrationService(McpIntegrationService mcpIntegrationService)
        {
            // Mock implementation
        }

        public void SetMusicVoiceCoordinationService(MusicVoiceCoordinationService musicVoiceCoordinationService)
        {
            // Mock implementation
        }

        public void Dispose()
        {
            // Mock implementation
        }

        private void ChangeState(DeviceState newState)
        {
            if (CurrentState != newState)
            {
                CurrentState = newState;
                DeviceStateChanged?.Invoke(this, newState);
            }
        }
    }
}
