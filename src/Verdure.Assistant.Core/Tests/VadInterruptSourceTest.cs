using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services.Interrupt.Sources;
using Verdure.Assistant.Core.Interfaces;
using System.Text.Json;

namespace Verdure.Assistant.Core.Tests;

/// <summary>
/// VAD 中断源功能测试
/// </summary>
public class VadInterruptSourceTest
{
    public static async Task TestVadWithSimulatedAudio()
    {
        // 创建简单的日志记录器
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<VoiceActivityInterruptSource>();

        // 创建VAD配置
        var vadConfig = new VoiceActivityInterruptSource.VadConfiguration
        {
            EnergyThreshold = 0.001f,
            MinVoiceFrames = 3,
            MinSilenceFrames = 10,
            MinVoiceDurationMs = 100f,
            MaxSilenceDurationMs = 500f,
            DebugOutput = true
        };

        // 创建模拟音频录制器
        var mockAudioRecorder = new MockAudioRecorder();

        // 创建VAD中断源
        var vadSource = new VoiceActivityInterruptSource(
            mockAudioRecorder, 
            null, // 不需要 VoiceChatService 用于测试
            vadConfig,
            logger);

        // 订阅中断事件
        vadSource.InterruptTriggered += (sender, e) =>
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] VAD Interrupt Triggered!");
            Console.WriteLine($"  Type: {e.InterruptType}");
            Console.WriteLine($"  Source: {e.SourceName}");
            Console.WriteLine($"  Description: {e.Description}");
            Console.WriteLine($"  Priority: {e.Priority}");
            if (e.Data != null)
            {
                Console.WriteLine($"  Data: {JsonSerializer.Serialize(e.Data)}");
            }
            Console.WriteLine();
        };

        Console.WriteLine("Starting VAD test...");
        
        try
        {
            // 启动VAD
            await vadSource.StartAsync();
            
            Console.WriteLine("VAD started. Simulating audio activity...");
            
            // 模拟不同强度的音频数据
            var scenarios = new[]
            {
                ("Background noise", 0.0005f, 2000),
                ("Quiet speech", 0.002f, 1500),  
                ("Normal speech", 0.01f, 2000),
                ("Loud speech", 0.05f, 1000),
                ("Silence", 0.0001f, 3000)
            };

            foreach (var (description, amplitude, durationMs) in scenarios)
            {
                Console.WriteLine($"\n--- Simulating: {description} (amplitude: {amplitude}, duration: {durationMs}ms) ---");
                
                mockAudioRecorder.SimulateAudio(amplitude, durationMs);
                await Task.Delay(durationMs + 1000); // 等待处理完成 + 额外间隔
            }

            Console.WriteLine("\nTest completed. Stopping VAD...");
            await vadSource.StopAsync();
            
            // 显示统计信息
            var stats = vadSource.GetStatistics();
            Console.WriteLine($"\nVAD Statistics:");
            Console.WriteLine($"  Audio Frames: {stats.AudioFrameCount}");
            Console.WriteLine($"  VAD Triggers: {stats.VadTriggerCount}");
            Console.WriteLine($"  Final Voice State: {stats.IsVoiceActive}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            vadSource.Dispose();
            mockAudioRecorder.Dispose();
        }
    }
}

/// <summary>
/// 模拟音频录制器用于测试
/// </summary>
public class MockAudioRecorder : ISharedAudioRecorder
{
    public event EventHandler<byte[]>? DataAvailable;
    public event EventHandler? RecordingStopped;
    
    public bool IsRecording { get; private set; }
    private bool _disposed = false;

    public void SubscribeToAudioData(EventHandler<byte[]> handler)
    {
        DataAvailable += handler;
    }

    public void UnsubscribeFromAudioData(EventHandler<byte[]> handler)
    {
        DataAvailable -= handler;
    }

    public async Task StartRecordingAsync(int sampleRate = 16000, int channels = 1)
    {
        IsRecording = true;
        await Task.CompletedTask;
    }

    public async Task StopRecordingAsync()
    {
        IsRecording = false;
        RecordingStopped?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    public void ForceCleanup()
    {
        IsRecording = false;
    }

    /// <summary>
    /// 模拟音频数据生成
    /// </summary>
    public void SimulateAudio(float amplitude, int durationMs)
    {
        const int sampleRate = 16000;
        const int frameMs = 20; // 20ms 帧
        const int frameSamples = sampleRate * frameMs / 1000; // 每帧样本数
        const int frameBytes = frameSamples * 2; // 16-bit samples
        
        var totalFrames = durationMs / frameMs;
        var random = new Random();

        for (int frame = 0; frame < totalFrames; frame++)
        {
            var audioData = new byte[frameBytes];
            
            // 生成模拟音频数据
            for (int i = 0; i < frameSamples; i++)
            {
                // 生成带有随机噪声的正弦波
                var t = (float)i / sampleRate;
                var frequency = 440.0f + (random.NextSingle() - 0.5f) * 200; // 440Hz +/- 100Hz
                var sample = amplitude * (float)Math.Sin(2 * Math.PI * frequency * t);
                
                // 添加随机噪声
                sample += (random.NextSingle() - 0.5f) * amplitude * 0.1f;
                
                // 转换为16-bit整数
                var sampleInt = (short)(sample * short.MaxValue);
                var byteIndex = i * 2;
                audioData[byteIndex] = (byte)(sampleInt & 0xFF);
                audioData[byteIndex + 1] = (byte)((sampleInt >> 8) & 0xFF);
            }
            
            // 触发音频数据事件
            DataAvailable?.Invoke(this, audioData);
            
            // 模拟实时处理延迟
            Thread.Sleep(frameMs);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            ForceCleanup();
            _disposed = true;
        }
    }
}
