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

        // 4. 设置音乐播放
        SetupMusicPlayer();

        // 5. 设置麦克风录音与处理
        SetupMicrophoneWithVad();

        // 6. 开始播放和录音
        Console.WriteLine("开始播放音乐和监听人声...");
        _musicPlayer?.Play();
        _micRecorder?.StartRecording();

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
        var vad = new VoiceActivityDetector(
            format: Format,
            fftSize: 1024,              // FFT大小
            energyThreshold: 0.02f      // 调整阈值以适应你的环境
        );

        // 设置VAD参数
        vad.ActivationTimeMs = 200f;    // 200ms确认是人声
        vad.HangoverTimeMs = 800f;      // 800ms延迟关闭
        vad.SpeechLowBand = 300;        // 人声频带下限
        vad.SpeechHighBand = 3400;      // 人声频带上限

        // 绑定人声检测事件
        vad.SpeechDetected += OnVoiceActivityDetected;
        _micRecorder.AddAnalyzer(vad);

        Console.WriteLine("麦克风和处理器设置完成");
    }

    private static void OnVoiceActivityDetected(bool isVoiceActive)
    {
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
        Console.WriteLine();
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
