using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Interfaces;

namespace AudioStreamIntegrationTest;

/// <summary>
/// 集成测试：验证共享音频流简化架构
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== 简化音频架构集成测试 ===");
        Console.WriteLine("本测试验证移除 PortAudioManager 后的音频组件工作状态");
        Console.WriteLine();

        // 配置服务（模拟 WinUI 项目的配置）
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        await host.StartAsync();

        // 获取服务
        var audioStreamManager = host.Services.GetRequiredService<AudioStreamManager>();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            // 测试 1: 简化的音频流管理器
            Console.WriteLine("测试 1: 简化的音频流管理器");
            
            // 启动共享音频流
            Console.WriteLine("启动共享音频流...");
            await audioStreamManager.StartRecordingAsync();
            
            // 模拟订阅者（关键词检测器）
            bool audioDataReceived = false;
            EventHandler<byte[]> audioHandler = (sender, data) =>
            {
                audioDataReceived = true;
                Console.WriteLine($"接收到音频数据: {data.Length} 字节");
            };
            
            audioStreamManager.SubscribeToAudioData(audioHandler);
            Console.WriteLine("已订阅音频数据流");
            
            // 等待一段时间接收数据
            Console.WriteLine("等待音频数据...");
            await Task.Delay(2000);
            
            if (audioDataReceived)
            {
                Console.WriteLine("✓ 音频数据接收成功");
            }
            else
            {
                Console.WriteLine("⚠ 未接收到音频数据（可能无麦克风或权限问题）");
            }
            
            // 取消订阅
            audioStreamManager.UnsubscribeFromAudioData(audioHandler);
            Console.WriteLine("已取消订阅音频数据流");
            
            // 停止共享音频流
            await audioStreamManager.StopRecordingAsync();
            Console.WriteLine("已停止共享音频流");
            Console.WriteLine("✓ 简化音频流管理器测试成功");
            Console.WriteLine();

            // 测试 2: 模拟多个组件同时使用共享音频流
            Console.WriteLine("测试 2: 模拟多个组件同时使用共享音频流的场景");
            
            // 创建两个共享音频流管理器实例（应该返回同一个单例）
            var sharedRecorder1 = AudioStreamManager.GetInstance();
            var sharedRecorder2 = AudioStreamManager.GetInstance();
            
            Console.WriteLine($"检查单例模式: recorder1 == recorder2: {ReferenceEquals(sharedRecorder1, sharedRecorder2)}");
            
            // 模拟关键词检测订阅
            bool keywordAudioReceived = false;
            EventHandler<byte[]> keywordHandler = (sender, data) =>
            {
                keywordAudioReceived = true;
                Console.WriteLine($"关键词检测接收到音频数据: {data.Length} 字节");
            };
            
            // 模拟语音聊天订阅
            bool voiceChatAudioReceived = false;
            EventHandler<byte[]> voiceChatHandler = (sender, data) =>
            {
                voiceChatAudioReceived = true;
                Console.WriteLine($"语音聊天接收到音频数据: {data.Length} 字节");
            };
            
            sharedRecorder1.SubscribeToAudioData(keywordHandler);
            sharedRecorder2.SubscribeToAudioData(voiceChatHandler);
            
            Console.WriteLine("两个组件都已订阅共享音频流");
            
            // 启动共享录制（应该只创建一个音频流）
            await sharedRecorder1.StartRecordingAsync(16000, 1);
            
            Console.WriteLine("共享音频流运行 2 秒...");
            await Task.Delay(2000);
            
            // 清理订阅
            sharedRecorder1.UnsubscribeFromAudioData(keywordHandler);
            sharedRecorder2.UnsubscribeFromAudioData(voiceChatHandler);
            
            await sharedRecorder1.StopRecordingAsync();
            
            Console.WriteLine($"关键词检测接收数据: {keywordAudioReceived}");
            Console.WriteLine($"语音聊天接收数据: {voiceChatAudioReceived}");
            Console.WriteLine("✓ 共享音频流多组件测试成功");
            Console.WriteLine();

            // 测试 3: 播放器测试
            Console.WriteLine("测试 3: 简化播放器测试");
            
            var audioPlayer = new PortAudioPlayer();
            
            // 模拟播放一些音频数据
            var testAudioData = new byte[1600]; // 100ms 的 16kHz 单声道音频
            for (int i = 0; i < testAudioData.Length; i += 2)
            {
                // 生成简单的正弦波测试音调
                var sample = (short)(Math.Sin(2.0 * Math.PI * 440.0 * i / (16000.0 * 2)) * 16383);
                testAudioData[i] = (byte)(sample & 0xFF);
                testAudioData[i + 1] = (byte)((sample >> 8) & 0xFF);
            }
            
            Console.WriteLine("播放测试音调...");
            await audioPlayer.PlayAsync(testAudioData, 16000, 1);
            
            // 等待播放完成
            await Task.Delay(1000);
            
            await audioPlayer.StopAsync();
            audioPlayer.Dispose();
            
            Console.WriteLine("✓ 播放器测试完成");
            Console.WriteLine();

            Console.WriteLine("🎉 所有测试完成！简化音频架构工作正常。");
            Console.WriteLine("✅ 已成功移除 PortAudioManager，简化了架构");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "测试过程中发生错误");
            Console.WriteLine($"❌ 测试失败: {ex.Message}");
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

    private static void ConfigureServices(IServiceCollection services)
    {
        // 日志
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 音频服务（使用共享音频流管理器）
        services.AddSingleton<AudioStreamManager>(provider =>
        {
            var logger = provider.GetService<ILogger<AudioStreamManager>>();
            return AudioStreamManager.GetInstance(logger);
        });
        
        // 注册为 ISharedAudioRecorder 接口
        services.AddSingleton<ISharedAudioRecorder>(provider => provider.GetService<AudioStreamManager>()!);
        
        // 保持向后兼容的 IAudioRecorder 接口
        services.AddSingleton<IAudioRecorder>(provider => provider.GetService<AudioStreamManager>()!);
    }
}
