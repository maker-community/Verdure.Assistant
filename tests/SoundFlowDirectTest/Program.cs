using System;
using System.Threading.Tasks;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace SoundFlowDirectTest
{
    /// <summary>
    /// 直接测试SoundFlow是否能播放声音
    /// 不通过Verdure.Assistant的封装
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== SoundFlow直接播放测试 ===");
            Console.WriteLine("测试目标：验证SoundFlow能否发出可听见的声音");
            
            AudioEngine? engine = null;
            AudioPlaybackDevice? device = null;
            SoundPlayer? player = null;
            QueueDataProvider? provider = null;

            try
            {
                // 1. 初始化引擎
                engine = new MiniAudioEngine();
                Console.WriteLine("✅ 音频引擎初始化成功");

                // 2. 配置格式
                var format = new AudioFormat
                {
                    SampleRate = 16000,
                    Channels = 1,
                    Format = SampleFormat.F32
                };

                // 3. 配置设备
                var deviceConfig = new MiniAudioDeviceConfig
                {
                    PeriodSizeInFrames = 960,
                    Periods = 3,
                    Playback = new DeviceSubConfig { ShareMode = ShareMode.Shared },
                    Wasapi = new WasapiSettings { Usage = WasapiUsage.ProAudio }
                };

                // 4. 初始化播放设备
                device = engine.InitializePlaybackDevice(null, format, deviceConfig);
                Console.WriteLine($"✅ 播放设备初始化成功: {device.Info?.Name}");

                // 5. 创建数据提供器
                provider = new QueueDataProvider(format);
                Console.WriteLine("✅ 数据提供器创建成功");

                // 6. 创建播放器
                player = new SoundPlayer(engine, format, provider);
                device.MasterMixer.AddComponent(player);
                Console.WriteLine("✅ 播放器创建成功");

                // 7. 启动设备和播放器
                device.Start();
                player.Play();
                Console.WriteLine("✅ 开始播放");

                // 8. 生成并播放测试音频（大音量的正弦波）
                Console.WriteLine("🔊 播放大音量440Hz正弦波3秒...");
                await PlayTestTone(provider, 440.0, 3.0, 0.5); // 50%音量

                Console.WriteLine("🔊 播放大音量800Hz正弦波2秒...");
                await PlayTestTone(provider, 800.0, 2.0, 0.8); // 80%音量

                Console.WriteLine("⏸️ 播放完成，停止设备");
                
                // 9. 停止
                player.Stop();
                device.Stop();
                
                Console.WriteLine("✅ 测试完成！如果能听到声音，说明SoundFlow工作正常。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 测试失败: {ex.Message}");
                Console.WriteLine($"详细信息: {ex.StackTrace}");
            }
            finally
            {
                // 清理资源
                provider?.Dispose();
                player?.Dispose();
                device?.Dispose();
                engine?.Dispose();
            }

            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }

        /// <summary>
        /// 播放测试音调
        /// </summary>
        static async Task PlayTestTone(QueueDataProvider provider, double frequency, double duration, double amplitude)
        {
            const int sampleRate = 16000;
            const int frameSize = 960; // 60ms @ 16kHz
            
            var totalFrames = (int)(duration * 1000 / 60); // 总帧数
            
            for (int frame = 0; frame < totalFrames; frame++)
            {
                var samples = new float[frameSize];
                
                for (int i = 0; i < frameSize; i++)
                {
                    var time = (frame * frameSize + i) / (double)sampleRate;
                    samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * time));
                }
                
                provider.AddSamples(samples);
                await Task.Delay(60); // 等待60ms（一帧的时间）
            }
        }
    }
}
