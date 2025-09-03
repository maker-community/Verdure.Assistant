using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Extensions.WebRtc.Apm;
using SoundFlow.Extensions.WebRtc.Apm.Modifiers;
using SoundFlow.Interfaces;
using SoundFlow.Providers;
using SoundFlow.Structs;
using System.Text;

namespace SoundFlow.Samples.VoiceInterruption;

/// <summary>
/// 演示如何实现音乐播放时的人声打断功能
/// 使用VAD检测人声，智能控制音乐播放
/// </summary>
internal class Program
{
    private static SoundPlayer? _musicPlayer;
    private static Recorder? _micRecorder;
    private static AudioPlaybackDevice? _outputDevice;
    private static AudioCaptureDevice? _inputDevice;
    private static readonly AudioEngine Engine = new MiniAudioEngine();
    private static readonly AudioFormat Format = AudioFormat.DvdHq; // 48kHz, 2 channels, F32
    private static bool _isMusicPaused;
    private static float _originalVolume = 1.0f;
    private static readonly object _volumeLock = new();

    // 调试统计信息
    private static int _audioFrameCount = 0;
    private static int _vadTriggerCount = 0;
    private static DateTime _lastAudioFrameTime = DateTime.Now;
    private static DateTime _lastVadTriggerTime = DateTime.Now;
    private static VoiceActivityDetector? _vad;

    // 设备配置
    private static readonly DeviceConfig DeviceConfig = new MiniAudioDeviceConfig
    {
        PeriodSizeInFrames = 960,
        Playback = new DeviceSubConfig { ShareMode = ShareMode.Shared },
        Capture = new DeviceSubConfig { ShareMode = ShareMode.Shared },
        Wasapi = new WasapiSettings { Usage = WasapiUsage.ProAudio }
    };

    static async Task Main()
    {
        Console.WriteLine("=== SoundFlow 人声打断音乐演示 ===");
        Console.WriteLine("此演示展示如何在播放音乐时检测人声并智能打断");
        Console.WriteLine();

        try
        {
            await RunVoiceInterruptionDemo();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"错误: {ex.Message}");
            Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
        }

        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
    }

    private static async Task RunVoiceInterruptionDemo()
    {
        // 1. 选择音频设备
        var outputDeviceInfo = SelectDevice(DeviceType.Playback);
        var inputDeviceInfo = SelectDevice(DeviceType.Capture);

        if (!outputDeviceInfo.HasValue || !inputDeviceInfo.HasValue)
        {
            throw new InvalidOperationException("无法获取音频设备");
        }

        Console.WriteLine($"输出设备: {outputDeviceInfo.Value.Name}");
        Console.WriteLine($"输入设备: {inputDeviceInfo.Value.Name}");
        Console.WriteLine();

        // 2. 初始化设备
        _outputDevice = Engine.InitializePlaybackDevice(outputDeviceInfo.Value, Format, DeviceConfig);
        _inputDevice = Engine.InitializeCaptureDevice(inputDeviceInfo.Value, Format, DeviceConfig);

        // 3. 启动设备
        _outputDevice.Start();
        _inputDevice.Start();

        Console.WriteLine($"✓ 音频设备启动成功");
        Console.WriteLine($"  输出设备状态: {_outputDevice.IsRunning}");
        Console.WriteLine($"  输入设备状态: {_inputDevice.IsRunning}");
        Console.WriteLine($"  音频格式: {Format.SampleRate}Hz, {Format.Channels}声道");

        // 4. 设置音乐播放
        SetupMusicPlayer();

        // 5. 设置麦克风录音与处理
        SetupMicrophoneWithVad();

        // 6. 开始播放和录音
        Console.WriteLine("开始播放音乐和监听人声...");
        _musicPlayer?.Play();
        _micRecorder?.StartRecording();

        Console.WriteLine($"✓ 音乐播放状态: {_musicPlayer?.State}");
        Console.WriteLine($"✓ 录音状态: {_micRecorder?.State}");

        // 7. 用户交互
        await HandleUserInteraction();

        // 8. 清理资源
        Cleanup();
    }

    private static DeviceInfo? SelectDevice(DeviceType type)
    {
        Engine.UpdateDevicesInfo();
        var devices = type == DeviceType.Playback ? Engine.PlaybackDevices : Engine.CaptureDevices;

        if (devices.Length == 0)
        {
            Console.WriteLine($"未找到{type}设备。");
            return null;
        }

        Console.WriteLine($"\n请选择{type}设备:");
        for (var i = 0; i < devices.Length; i++)
        {
            Console.WriteLine($"  {i}: {devices[i].Name} {(devices[i].IsDefault ? "(默认)" : "")}");
        }

        while (true)
        {
            Console.Write("输入设备索引: ");
            if (int.TryParse(Console.ReadLine(), out var index) && index >= 0 && index < devices.Length)
            {
                return devices[index];
            }
            Console.WriteLine("无效索引，请重试。");
        }
    }

    private static void SetupMusicPlayer()
    {
        Console.WriteLine("正在设置音乐播放器...");

        // 创建一个简单的音乐信号（多频率正弦波混合）
        var musicProvider = CreateDemoMusicProvider();

        _musicPlayer = new SoundPlayer(Engine, Format, musicProvider);

        // 添加到设备的混音器
        if (_outputDevice != null)
        {
            _outputDevice.MasterMixer.AddComponent(_musicPlayer);
        }

        Console.WriteLine("音乐播放器设置完成");
    }

    private static ISoundDataProvider CreateDemoMusicProvider()
    {
        // 创建一个简单的音乐信号（多频率正弦波混合）
        var sampleRate = Format.SampleRate;
        var channels = Format.Channels;
        var duration = 30; // 30秒循环
        var samples = new List<float>();

        for (int i = 0; i < sampleRate * duration; i++)
        {
            var t = (float)i / sampleRate;

            // 混合多个频率创建"音乐"效果
            var music = 0.3f * MathF.Sin(2 * MathF.PI * 220 * t) +  // A3
                       0.2f * MathF.Sin(2 * MathF.PI * 330 * t) +  // E4
                       0.1f * MathF.Sin(2 * MathF.PI * 440 * t);   // A4

            // 为每个声道添加相同的信号
            for (int ch = 0; ch < channels; ch++)
            {
                samples.Add(music * 0.3f); // 降低音量避免过载
            }
        }

        return new RawDataProvider(samples.ToArray());
    }

    private static void SetupMicrophoneWithVad()
    {
        Console.WriteLine("正在设置麦克风录音和处理...");

        if (_inputDevice == null) return;

        // 1. 创建录音器（录制到内存流进行实时处理）
        var memoryStream = new MemoryStream();
        _micRecorder = new Recorder(_inputDevice, memoryStream);

        // 2. 添加VAD进行人声检测
        _vad = new VoiceActivityDetector(
            format: Format,
            fftSize: 1024,              // FFT大小
            energyThreshold: 0.001f     // 降低阈值以便更容易触发
        );

        // 设置VAD参数 - 使用更宽松的设置便于调试
        _vad.ActivationTimeMs = 100f;   // 100ms确认是人声（更快响应）
        _vad.HangoverTimeMs = 500f;     // 500ms延迟关闭（更短延迟）
        _vad.SpeechLowBand = 200;       // 人声频带下限（更宽范围）
        _vad.SpeechHighBand = 4000;     // 人声频带上限（更宽范围）

        Console.WriteLine($"VAD配置:");
        Console.WriteLine($"  能量阈值: {_vad.EnergyThreshold}");
        Console.WriteLine($"  激活时间: {_vad.ActivationTimeMs}ms");
        Console.WriteLine($"  保持时间: {_vad.HangoverTimeMs}ms");
        Console.WriteLine($"  频带范围: {_vad.SpeechLowBand}Hz - {_vad.SpeechHighBand}Hz");

        // 绑定人声检测事件
        _vad.SpeechDetected += OnVoiceActivityDetected;

        // 创建一个定时器来监控音频数据
        var audioMonitorTimer = new System.Timers.Timer(1000); // 每秒检查一次
        audioMonitorTimer.Elapsed += (sender, e) => 
        {
            if (_micRecorder != null && _inputDevice?.IsRunning == true)
            {
                var timeSinceLastFrame = DateTime.Now - _lastAudioFrameTime;
                if (timeSinceLastFrame.TotalSeconds > 5)
                {
                    Console.WriteLine($"⚠️ 警告: 已经 {timeSinceLastFrame.TotalSeconds:F1} 秒没有检测到音频活动");
                }
            }
        };
        audioMonitorTimer.Start();

        _micRecorder.AddAnalyzer(_vad);

        // 启动音频监控
        MonitorAudioData();

        Console.WriteLine("麦克风和处理器设置完成");
    }

    // 创建一个简单的音频数据监控方法
    private static void MonitorAudioData()
    {
        // 由于Recorder可能没有直接的音频数据事件，我们通过其他方式监控
        Task.Run(async () =>
        {
            int monitorCount = 0;
            while (_micRecorder != null && _inputDevice?.IsRunning == true)
            {
                await Task.Delay(1000);
                monitorCount++;
                _lastAudioFrameTime = DateTime.Now; // 更新时间戳
                
                if (monitorCount % 10 == 0)
                {
                    Console.WriteLine($"🎙️ 录音监控 - 运行时间: {monitorCount} 秒，VAD触发次数: {_vadTriggerCount}");
                    
                    // 如果长时间没有VAD触发，给出提示
                    if (_vadTriggerCount == 0 && monitorCount > 10)
                    {
                        Console.WriteLine($"💡 提示: 已运行 {monitorCount} 秒但未检测到声音，请检查:");
                        Console.WriteLine("   1. 麦克风是否正常工作");
                        Console.WriteLine("   2. 音量是否足够大");
                        Console.WriteLine("   3. 是否选择了正确的输入设备");
                        Console.WriteLine("   4. 尝试按 't' 调整VAD阈值");
                    }
                }
            }
        });
    }

    private static void OnAudioDataReceived(object? sender, byte[] audioData)
    {
        _audioFrameCount++;
        _lastAudioFrameTime = DateTime.Now;

        // 每100帧打印一次统计信息
        if (_audioFrameCount % 100 == 0)
        {
            Console.WriteLine($"🎙️ 音频数据接收正常 - 已处理 {_audioFrameCount} 帧，最新帧大小: {audioData.Length} 字节");

            // 简单的音量检测
            var samples = new float[audioData.Length / 4]; // 假设是32位浮点格式
            Buffer.BlockCopy(audioData, 0, samples, 0, audioData.Length);

            var rms = CalculateRMS(samples);
            var db = 20 * Math.Log10(rms + 1e-10); // 避免log(0)

            Console.WriteLine($"📊 音频统计 - RMS: {rms:F6}, dB: {db:F2}");

            if (rms > 0.001f)
            {
                Console.WriteLine($"🔊 检测到音频信号 (RMS > 0.001)");
            }
        }
    }

    private static float CalculateRMS(float[] samples)
    {
        if (samples.Length == 0) return 0;

        double sum = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }

    private static void OnVoiceActivityDetected(bool isVoiceActive)
    {
        _vadTriggerCount++;
        _lastVadTriggerTime = DateTime.Now;
        
        Console.WriteLine($"🎯 VAD事件触发 #{_vadTriggerCount} - 人声活动: {isVoiceActive} (时间: {DateTime.Now:HH:mm:ss.fff})");
        
        lock (_volumeLock)
        {
            if (isVoiceActive && !_isMusicPaused)
            {
                // 检测到人声，降低音乐音量
                Console.WriteLine("🎤 检测到人声 - 降低音乐音量");
                _originalVolume = _musicPlayer?.Volume ?? 1.0f;
                if (_musicPlayer != null)
                {
                    _musicPlayer.Volume = 0.1f; // 降低到10%
                }
                _isMusicPaused = true;
            }
            else if (!isVoiceActive && _isMusicPaused)
            {
                // 人声结束，恢复音乐音量
                Console.WriteLine("🔇 人声结束 - 恢复音乐音量");
                if (_musicPlayer != null)
                {
                    _musicPlayer.Volume = _originalVolume;
                }
                _isMusicPaused = false;
            }
        }
    }

    private static async Task HandleUserInteraction()
    {
        Console.WriteLine("\n=== 使用说明 ===");
        Console.WriteLine("- 音乐正在播放");
        Console.WriteLine("- 对着麦克风说话，音乐音量会自动降低");
        Console.WriteLine("- 停止说话后，音乐音量会恢复");
        Console.WriteLine("- 按 'q' 退出演示");
        Console.WriteLine("- 按 's' 查看当前状态");
        Console.WriteLine("- 按 'v' 手动调整音量");
        Console.WriteLine("- 按 'd' 查看调试信息");
        Console.WriteLine("- 按 't' 测试VAD阈值");
        Console.WriteLine();

        bool running = true;
        while (running)
        {
            var key = Console.ReadKey(true);
            switch (key.KeyChar)
            {
                case 'q':
                case 'Q':
                    running = false;
                    break;

                case 's':
                case 'S':
                    ShowStatus();
                    break;

                case 'v':
                case 'V':
                    AdjustVolume();
                    break;

                case 'd':
                case 'D':
                    ShowDebugInfo();
                    break;

                case 't':
                case 'T':
                    TestVadThreshold();
                    break;

                default:
                    Console.WriteLine($"未知命令: {key.KeyChar}");
                    break;
            }

            await Task.Delay(100);
        }
    }

    private static void AdjustVolume()
    {
        Console.WriteLine("请输入新的音量 (0.0 - 1.0):");
        if (float.TryParse(Console.ReadLine(), out float volume) && volume >= 0 && volume <= 1.0f)
        {
            if (_musicPlayer != null)
            {
                _musicPlayer.Volume = volume;
                _originalVolume = volume;
                Console.WriteLine($"音量已调整为: {volume:P0}");
            }
        }
        else
        {
            Console.WriteLine("无效的音量，请输入0.0到1.0之间的数值");
        }
    }

    private static void ShowStatus()
    {
        Console.WriteLine("\n=== 当前状态 ===");
        Console.WriteLine($"音乐播放: {(_musicPlayer?.State.ToString() ?? "未知")}");
        Console.WriteLine($"音乐音量: {(_musicPlayer?.Volume ?? 0):P0}");
        Console.WriteLine($"人声打断: {(_isMusicPaused ? "是" : "否")}");
        Console.WriteLine($"录音状态: {(_micRecorder?.State.ToString() ?? "未知")}");
        Console.WriteLine($"输出设备运行: {(_outputDevice?.IsRunning == true ? "是" : "否")}");
        Console.WriteLine($"输入设备运行: {(_inputDevice?.IsRunning == true ? "是" : "否")}");
        Console.WriteLine($"音频帧计数: {_audioFrameCount}");
        Console.WriteLine($"VAD触发计数: {_vadTriggerCount}");
        Console.WriteLine();
    }

    private static void ShowDebugInfo()
    {
        Console.WriteLine("\n=== 调试信息 ===");
        Console.WriteLine($"音频帧计数: {_audioFrameCount}");
        Console.WriteLine($"VAD触发计数: {_vadTriggerCount}");
        Console.WriteLine($"最后音频帧时间: {_lastAudioFrameTime:HH:mm:ss.fff}");
        Console.WriteLine($"最后VAD触发时间: {_lastVadTriggerTime:HH:mm:ss.fff}");

        var timeSinceLastAudio = DateTime.Now - _lastAudioFrameTime;
        var timeSinceLastVad = DateTime.Now - _lastVadTriggerTime;

        Console.WriteLine($"距离最后音频帧: {timeSinceLastAudio.TotalSeconds:F1}秒");
        Console.WriteLine($"距离最后VAD触发: {timeSinceLastVad.TotalSeconds:F1}秒");

        if (_vad != null)
        {
            Console.WriteLine($"VAD当前配置:");
            Console.WriteLine($"  能量阈值: {_vad.EnergyThreshold}");
            Console.WriteLine($"  激活时间: {_vad.ActivationTimeMs}ms");
            Console.WriteLine($"  保持时间: {_vad.HangoverTimeMs}ms");
            Console.WriteLine($"  频带: {_vad.SpeechLowBand}-{_vad.SpeechHighBand}Hz");
        }

        Console.WriteLine($"麦克风录音器状态: {_micRecorder?.State}");
        Console.WriteLine($"输入设备运行状态: {_inputDevice?.IsRunning}");
        Console.WriteLine();
    }

    private static void TestVadThreshold()
    {
        if (_vad == null)
        {
            Console.WriteLine("VAD未初始化");
            return;
        }

        Console.WriteLine("\n=== VAD阈值测试 ===");
        Console.WriteLine("当前阈值: " + _vad.EnergyThreshold);
        Console.WriteLine("请输入新的阈值 (0.0001 - 1.0，推荐 0.001 - 0.1):");

        if (float.TryParse(Console.ReadLine(), out float threshold) && threshold > 0 && threshold <= 1.0f)
        {
            _vad.EnergyThreshold = threshold;
            Console.WriteLine($"✓ VAD阈值已设置为: {threshold}");
            Console.WriteLine("现在尝试说话测试效果...");
        }
        else
        {
            Console.WriteLine("无效的阈值");
        }
    }

    private static void Cleanup()
    {
        Console.WriteLine("正在清理资源...");

        _micRecorder?.StopRecording();
        _micRecorder?.Dispose();

        _musicPlayer?.Stop();
        if (_outputDevice != null && _musicPlayer != null)
        {
            _outputDevice.MasterMixer.RemoveComponent(_musicPlayer);
        }
        _musicPlayer?.Dispose();

        _outputDevice?.Stop();
        _outputDevice?.Dispose();

        _inputDevice?.Stop();
        _inputDevice?.Dispose();

        Engine?.Dispose();

        Console.WriteLine("资源清理完成");
    }
}
