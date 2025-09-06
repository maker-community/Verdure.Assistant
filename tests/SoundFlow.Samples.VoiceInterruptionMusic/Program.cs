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

namespace SoundFlow.Samples.VoiceInterruptionMusic;

/// <summary>
/// 演示如何实现音乐播放时的人声打断功能
/// 使用VAD检测人声，智能控制音乐播放
/// 使用SoundFlow的StreamDataProvider从MP3文件播放真实音乐
/// 不依赖额外的解码库，直接利用SoundFlow内置的音频支持
/// </summary>
internal class Program
{
    private static SoundPlayer? _musicPlayer;
    private static Recorder? _micRecorder;
    private static AudioPlaybackDevice? _outputDevice;
    private static AudioCaptureDevice? _inputDevice;
    private static FileStream? _musicFileStream;
    private static readonly AudioEngine Engine = new MiniAudioEngine();
    private static readonly AudioFormat Format = AudioFormat.DvdHq; // 48kHz, 2 channels, F32
    private static bool _isMusicPaused;
    private static float _originalVolume = 1.0f;
    private static readonly object _volumeLock = new();
    private static string? _musicFilePath;

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
        Console.WriteLine("=== SoundFlow MP3音乐人声打断演示 ===");
        Console.WriteLine("此演示展示如何在播放真实MP3音乐时检测人声并智能打断");
        Console.WriteLine("使用SoundFlow的StreamDataProvider直接播放MP3文件");
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
        // 1. 检查音乐文件
        if (!CheckMusicFile())
        {
            return;
        }

        // 2. 选择音频设备
        var outputDeviceInfo = SelectDevice(DeviceType.Playback);
        var inputDeviceInfo = SelectDevice(DeviceType.Capture);

        if (!outputDeviceInfo.HasValue || !inputDeviceInfo.HasValue)
        {
            throw new InvalidOperationException("无法获取音频设备");
        }

        Console.WriteLine($"输出设备: {outputDeviceInfo.Value.Name}");
        Console.WriteLine($"输入设备: {inputDeviceInfo.Value.Name}");
        Console.WriteLine();

        // 3. 初始化设备
        _outputDevice = Engine.InitializePlaybackDevice(outputDeviceInfo.Value, Format, DeviceConfig);
        _inputDevice = Engine.InitializeCaptureDevice(inputDeviceInfo.Value, Format, DeviceConfig);

        // 4. 启动设备
        _outputDevice.Start();
        _inputDevice.Start();

        Console.WriteLine($"✓ 音频设备启动成功");
        Console.WriteLine($"  输出设备状态: {_outputDevice.IsRunning}");
        Console.WriteLine($"  输入设备状态: {_inputDevice.IsRunning}");
        Console.WriteLine($"  音频格式: {Format.SampleRate}Hz, {Format.Channels}声道");

        // 5. 设置音乐播放
        SetupMusicPlayer();

        // 6. 设置麦克风录音与处理
        SetupMicrophoneWithVad();

        // 7. 开始播放和录音
        Console.WriteLine("开始播放音乐和监听人声...");
        _musicPlayer?.Play();
        _micRecorder?.StartRecording();

        Console.WriteLine($"✓ 音乐播放状态: {_musicPlayer?.State}");
        Console.WriteLine($"✓ 录音状态: {_micRecorder?.State}");

        // 8. 用户交互
        await HandleUserInteraction();

        // 9. 清理资源
        Cleanup();
    }

    private static bool CheckMusicFile()
    {
        // 查找test.mp3文件
        var possiblePaths = new[]
        {
            "test.mp3",
            Path.Combine(".", "test.mp3"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test.mp3")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                _musicFilePath = Path.GetFullPath(path);
                Console.WriteLine($"✓ 找到音乐文件: {_musicFilePath}");
                
                var fileInfo = new FileInfo(_musicFilePath);
                Console.WriteLine($"  文件大小: {fileInfo.Length / 1024:N0} KB");
                return true;
            }
        }

        Console.WriteLine("❌ 未找到test.mp3文件");
        Console.WriteLine("请确保test.mp3文件位于以下位置之一:");
        foreach (var path in possiblePaths)
        {
            Console.WriteLine($"  - {Path.GetFullPath(path)}");
        }
        return false;
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
        Console.WriteLine("正在设置MP3音乐播放器...");

        if (string.IsNullOrEmpty(_musicFilePath))
        {
            throw new InvalidOperationException("音乐文件路径为空");
        }

        try
        {
            // 使用SoundFlow的StreamDataProvider来播放MP3文件
            // 这是官方推荐的方式，可以直接支持MP3等多种音频格式
            _musicFileStream = new FileStream(_musicFilePath, FileMode.Open, FileAccess.Read);
            var musicProvider = new StreamDataProvider(Engine, Format, _musicFileStream);

            _musicPlayer = new SoundPlayer(Engine, Format, musicProvider);

            // 添加到设备的混音器
            if (_outputDevice != null)
            {
                _outputDevice.MasterMixer.AddComponent(_musicPlayer);
            }

            Console.WriteLine($"✓ MP3音乐播放器设置完成");
            Console.WriteLine($"  使用StreamDataProvider直接播放MP3文件");
            Console.WriteLine($"  文件: {Path.GetFileName(_musicFilePath)}");
            
            var fileInfo = new FileInfo(_musicFilePath);
            Console.WriteLine($"  大小: {fileInfo.Length / 1024.0:F1} KB");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 设置音乐播放器失败: {ex.Message}");
            _musicFileStream?.Dispose();
            _musicFileStream = null;
            throw;
        }
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
        Console.WriteLine("- 使用SoundFlow的StreamDataProvider播放真实的MP3音乐");
        Console.WriteLine("- 对着麦克风说话，音乐音量会自动降低");
        Console.WriteLine("- 停止说话后，音乐音量会恢复");
        Console.WriteLine("- 按 'q' 退出演示");
        Console.WriteLine("- 按 's' 查看当前状态");
        Console.WriteLine("- 按 'v' 手动调整音量");
        Console.WriteLine("- 按 'd' 查看调试信息");
        Console.WriteLine("- 按 't' 测试VAD阈值");
        Console.WriteLine("- 按 'r' 重新开始播放");
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

                case 'r':
                case 'R':
                    RestartMusic();
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

    private static void RestartMusic()
    {
        try
        {
            Console.WriteLine("重新开始播放MP3音乐...");
            
            _musicPlayer?.Stop();
            
            // 如果音乐播放器存在，需要重新初始化
            if (_musicPlayer != null && _outputDevice != null)
            {
                _outputDevice.MasterMixer.RemoveComponent(_musicPlayer);
                _musicPlayer.Dispose();
                
                // 重新设置文件流和播放器
                _musicFileStream?.Dispose();
                if (!string.IsNullOrEmpty(_musicFilePath))
                {
                    _musicFileStream = new FileStream(_musicFilePath, FileMode.Open, FileAccess.Read);
                    var musicProvider = new StreamDataProvider(Engine, Format, _musicFileStream);
                    _musicPlayer = new SoundPlayer(Engine, Format, musicProvider);
                    _outputDevice.MasterMixer.AddComponent(_musicPlayer);
                }
            }
            
            Thread.Sleep(100); // 短暂等待初始化完成
            _musicPlayer?.Play();
            Console.WriteLine("✓ MP3音乐重新开始播放");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 重新开始播放失败: {ex.Message}");
        }
    }

    private static void ShowStatus()
    {
        Console.WriteLine("\n=== 当前状态 ===");
        Console.WriteLine($"音乐文件: {Path.GetFileName(_musicFilePath ?? "未知")}");
        Console.WriteLine($"音乐播放: {(_musicPlayer?.State.ToString() ?? "未知")}");
        Console.WriteLine($"音乐音量: {(_musicPlayer?.Volume ?? 0):P0}");
        Console.WriteLine($"播放时间: {TimeSpan.FromSeconds(_musicPlayer?.Time ?? 0):mm\\:ss} / {TimeSpan.FromSeconds(_musicPlayer?.Duration ?? 0):mm\\:ss}");
        Console.WriteLine($"人声打断: {(_isMusicPaused ? "是" : "否")}");
        Console.WriteLine($"录音状态: {(_micRecorder?.State.ToString() ?? "未知")}");
        Console.WriteLine($"输出设备运行: {(_outputDevice?.IsRunning == true ? "是" : "否")}");
        Console.WriteLine($"输入设备运行: {(_inputDevice?.IsRunning == true ? "是" : "否")}");
        Console.WriteLine($"文件流状态: {(_musicFileStream != null && _musicFileStream.CanRead ? "正常" : "异常")}");
        Console.WriteLine($"音频帧计数: {_audioFrameCount}");
        Console.WriteLine($"VAD触发计数: {_vadTriggerCount}");
        Console.WriteLine();
    }

    private static void ShowDebugInfo()
    {
        Console.WriteLine("\n=== 调试信息 ===");
        Console.WriteLine($"音频文件路径: {_musicFilePath}");
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

        // 清理音乐文件流
        _musicFileStream?.Dispose();
        _musicFileStream = null;

        _outputDevice?.Stop();
        _outputDevice?.Dispose();

        _inputDevice?.Stop();
        _inputDevice?.Dispose();

        Engine?.Dispose();

        Console.WriteLine("资源清理完成");
    }
}

public class PositionChangedEventArgs : EventArgs
{
    public int NewPosition { get; }

    public PositionChangedEventArgs(int newPosition)
    {
        NewPosition = newPosition;
    }
}
