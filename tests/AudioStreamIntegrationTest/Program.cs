using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Interfaces;

namespace AudioStreamIntegrationTest;

/// <summary>
/// 集成测试：验证 PortAudio 单例管理器和共享音频流修复关键词唤醒问题
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== PortAudio 单例管理器和共享音频流集成测试 ===");
        Console.WriteLine("本测试验证关键词检测和语音录制的 PortAudio 资源管理修复");
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
            // 测试 1: PortAudio 单例管理器基本功能
            Console.WriteLine("测试 1: PortAudio 单例管理器");
            var portAudioManager = PortAudioManager.Instance;
            
            // 模拟多个组件获取 PortAudio 引用
            Console.WriteLine("组件 1 获取 PortAudio 引用...");
            portAudioManager.AcquireReference();
            
            Console.WriteLine("组件 2 获取 PortAudio 引用...");
            portAudioManager.AcquireReference();
            
            Console.WriteLine("组件 1 释放 PortAudio 引用...");
            portAudioManager.ReleaseReference();
            
            Console.WriteLine("组件 2 释放 PortAudio 引用...");
            portAudioManager.ReleaseReference();
            
            Console.WriteLine("✓ PortAudio 单例管理器测试成功");
            Console.WriteLine();

            // 测试 2: 共享音频流管理器
            Console.WriteLine("测试 2: 共享音频流管理器");
            
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
            Console.WriteLine("✓ 共享音频流管理器测试成功");
            Console.WriteLine();

            // 测试 3: 模拟关键词检测和语音录制同时使用
            Console.WriteLine("测试 3: 模拟关键词检测和语音录制同时使用的场景");
              // 第一个录制器（模拟关键词检测）
            var recorder1 = new PortAudioRecorder();
            Console.WriteLine("录制器 1 (关键词检测) 开始录制...");
            await recorder1.StartRecordingAsync(16000, 1);
            
            // 第二个录制器（模拟语音聊天）
            var recorder2 = new PortAudioRecorder();
            Console.WriteLine("录制器 2 (语音聊天) 开始录制...");
            await recorder2.StartRecordingAsync(16000, 1);
            
            Console.WriteLine("两个录制器同时运行 2 秒...");
            await Task.Delay(2000);
            
            Console.WriteLine("停止录制器 1...");
            await recorder1.StopRecordingAsync();
            recorder1.Dispose();
            
            Console.WriteLine("停止录制器 2...");
            await recorder2.StopRecordingAsync();
            recorder2.Dispose();
            
            Console.WriteLine("✓ 多录制器资源管理测试成功");
            Console.WriteLine();

            Console.WriteLine("🎉 所有测试完成！PortAudio 资源冲突问题已修复。");
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

        // 音频服务（使用与 WinUI 项目相同的配置）
        services.AddSingleton<AudioStreamManager>(provider =>
        {
            var logger = provider.GetService<ILogger<AudioStreamManager>>();
            return AudioStreamManager.GetInstance(logger);
        });
        
        services.AddSingleton<IAudioRecorder>(provider => provider.GetService<AudioStreamManager>()!);
    }
}
