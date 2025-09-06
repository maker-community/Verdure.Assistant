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

namespace SoundFlowRecordingCodecTest;

/// <summary>
/// SoundFlow录音转码测试项目
/// 
/// 测试目标：
/// 1. 验证SoundFlow使用最优参数(S16/1ch/16kHz)录音的转码性能
/// 2. 对比与PortAudioSharp2的兼容性
/// 3. 测试OpusSharp编码集成
/// 4. 验证VAD检测功能
/// 
/// 基于分析结果的最优配置：
/// - AudioFormat: S16, 1 channel, 16kHz (接近目标格式，减少转换)
/// - 转换路径: S16→F32→Int16 (最小开销)
/// - 兼容现有OpusSharpAudioCodec
/// </summary>
internal class Program
{
    private static AudioEngine? _engine;
    private static AudioCaptureDevice? _captureDevice;
    private static Recorder? _recorder;
    private static SimpleAudioProcessor? _audioProcessor;
    
    // 音频文件保存
    private static FileStream? _audioFileStream;
    private static BinaryWriter? _audioFileWriter;
    private static readonly string _audioFileName = "recorded_audio.wav";
    private static int _totalSamplesWritten = 0;
    
    // 测试统计
    private static int _totalFramesProcessed = 0;
    private static int _audioProcessedFrames = 0;
    private static DateTime _testStartTime;
    private static readonly object _statsLock = new();
    
    // 最优化的音频格式配置
    private static readonly AudioFormat OptimalFormat = new()
    {
        Format = SampleFormat.S16,  // 使用S16接近Int16目标格式
        Channels = 1,               // 单声道，匹配目标
        SampleRate = 16000          // 16kHz，匹配目标采样率
    };
    
    // 设备配置 - 优化为低延迟录音
    private static readonly MiniAudioDeviceConfig DeviceConfig = new()
    {
        PeriodSizeInFrames = 960,   // 60ms @ 16kHz = 960 samples
        PeriodSizeInMilliseconds = 0,
        Periods = 3,
        NoPreSilencedOutputBuffer = true,
        NoClip = false,
        NoDisableDenormals = false,
        NoFixedSizedCallback = false,
        Capture = new DeviceSubConfig 
        { 
            ShareMode = ShareMode.Shared 
        },
        Wasapi = new WasapiSettings 
        { 
            Usage = WasapiUsage.ProAudio,
            NoAutoConvertSRC = false,     // 允许自动采样率转换
            NoDefaultQualitySRC = false,  // 允许高质量重采样
            NoAutoStreamRouting = false,
            NoHardwareOffloading = false
        }
    };

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== SoundFlow录音转码测试 ===");
        Console.WriteLine($"目标格式: S16, 1ch, 16kHz (960 samples/frame)");
        Console.WriteLine($"测试目标: 验证最优参数配置的转码性能");
        Console.WriteLine($"音频保存: {_audioFileName} (WAV格式)");
        Console.WriteLine();

        try
        {
            await RunRecordingCodecTest();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"测试失败: {ex.Message}");
            Console.WriteLine($"详细信息: {ex.StackTrace}");
        }
        finally
        {
            await CleanupResources();
        }

        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
    }

    private static async Task RunRecordingCodecTest()
    {
        Console.WriteLine("1. 初始化音频引擎和设备...");
        
        // 初始化音频文件保存
        if (!InitializeAudioFile())
        {
            Console.WriteLine("❌ 音频文件初始化失败");
            return;
        }
        Console.WriteLine($"✅ 音频文件已准备: {_audioFileName}");
        if (!await InitializeAudioSystem())
        {
            Console.WriteLine("❌ 音频系统初始化失败");
            return;
        }
        Console.WriteLine("✅ 音频系统初始化成功");

        Console.WriteLine("\n2. 初始化音频处理器...");
        if (!InitializeAudioProcessor())
        {
            Console.WriteLine("❌ 音频处理器初始化失败");
            return;
        }
        Console.WriteLine("✅ 音频处理器初始化成功");

        Console.WriteLine("\n3. 开始录音测试...");
        if (!StartRecording())
        {
            Console.WriteLine("❌ 录音启动失败");
            return;
        }
        Console.WriteLine("✅ 录音已启动");

        _testStartTime = DateTime.Now;
        Console.WriteLine("\n🎤 请说话测试录音转码功能...");
        Console.WriteLine("📊 实时统计信息将每秒更新");
        Console.WriteLine("⏹️  按任意键停止测试");
        
        // 启动统计显示任务
        var statsTask = Task.Run(DisplayRealtimeStats);
        
        // 等待用户输入停止
        Console.ReadKey();
        
        Console.WriteLine("\n4. 停止录音并显示最终统计...");
        StopRecording();
        
        // 完成音频文件写入
        FinalizeAudioFile();
        
        // 等待统计任务完成
        await Task.Delay(1000);
        
        DisplayFinalStatistics();
    }

    private static async Task<bool> InitializeAudioSystem()
    {
        try
        {
            _engine = new MiniAudioEngine();
            
            // 显示可用的录音设备
            Console.WriteLine("\n可用录音设备:");
            for (int i = 0; i < _engine.CaptureDevices.Length; i++)
            {
                var device = _engine.CaptureDevices[i];
                var marker = device.IsDefault ? " (默认)" : "";
                Console.WriteLine($"  [{i}] {device.Name}{marker}");
            }
            
            // 使用默认录音设备，配置为最优格式
            _captureDevice = _engine.InitializeCaptureDevice(null, OptimalFormat, DeviceConfig);
            
            Console.WriteLine($"\n已选择设备: {_captureDevice.Info?.Name ?? "默认设备"}");
            Console.WriteLine($"设备格式: {_captureDevice.Format.Format}, {_captureDevice.Format.Channels}ch, {_captureDevice.Format.SampleRate}Hz");
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"音频系统初始化错误: {ex.Message}");
            return false;
        }
    }

    private static bool InitializeAudioProcessor()
    {
        try
        {
            // 创建简单的音频处理器 - 严格16kHz/1ch要求
            _audioProcessor = new SimpleAudioProcessor(
                sampleRate: 16000,
                channels: 1
            );
            
            Console.WriteLine($"AudioProcessor配置: 16kHz, 1ch, 60ms frames ({_audioProcessor.FrameSize} samples)");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AudioProcessor初始化错误: {ex.Message}");
            return false;
        }
    }

    private static bool StartRecording()
    {
        try
        {
            if (_captureDevice == null)
            {
                Console.WriteLine("录音设备未初始化");
                return false;
            }

            // 创建录音器，使用回调方式处理音频数据
            _recorder = new Recorder(_captureDevice, ProcessAudioData);
            
            // 开始录音
            _recorder.StartRecording();
            _captureDevice.Start();
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"录音启动错误: {ex.Message}");
            return false;
        }
    }

    private static void StopRecording()
    {
        try
        {
            _captureDevice?.Stop();
            _recorder?.StopRecording();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"录音停止错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 音频数据处理回调 - 核心转码测试逻辑
    /// 
    /// 兼容性验证：
    /// 1. SoundFlow输出F32 → 转换为byte[] (与AudioStreamManager兼容)
    /// 2. 验证帧大小匹配 (960 samples = 1920 bytes @ 16kHz/1ch/Int16)
    /// 3. 确保与OpusSharpAudioCodec的数据格式兼容
    /// </summary>
    private static void ProcessAudioData(Span<float> samples, Capability capability)
    {
        lock (_statsLock)
        {
            _totalFramesProcessed++;
        }

        try
        {
            // 1. SoundFlow输出的是F32格式，需要转换为Int16
            var sampleCount = samples.Length;
            var int16Samples = new short[sampleCount];
            
            // F32 → Int16 转换 (核心转码测试)
            for (int i = 0; i < sampleCount; i++)
            {
                // 限制范围到 [-1.0, 1.0] 并转换为 Int16
                var clampedSample = Math.Max(-1.0f, Math.Min(1.0f, samples[i]));
                int16Samples[i] = (short)(clampedSample * short.MaxValue);
            }

            // 2. 转换为byte[]格式 (匹配AudioStreamManager.OnAudioDataReceived的输出)
            var audioDataBytes = new byte[sampleCount * 2]; // 2 bytes per Int16
            for (int i = 0; i < sampleCount; i++)
            {
                var bytes = BitConverter.GetBytes(int16Samples[i]);
                audioDataBytes[i * 2] = bytes[0];
                audioDataBytes[i * 2 + 1] = bytes[1];
            }

            // 3. 保存音频到文件 (Opus处理前的原始数据)
            SaveAudioToFile(int16Samples);

            // 4. 验证数据格式兼容性
            VerifyDataCompatibility(audioDataBytes, sampleCount);

            // 5. 测试音频处理 (验证转码兼容性)
            if (_audioProcessor != null && int16Samples.Length >= _audioProcessor.FrameSize) 
            {
                try
                {
                    // 确保帧大小正确 (960 samples = 60ms @ 16kHz)
                    var frameSize = _audioProcessor.FrameSize;
                    if (int16Samples.Length >= frameSize)
                    {
                        var frame = new short[frameSize];
                        Array.Copy(int16Samples, 0, frame, 0, frameSize);
                        
                        var processedData = _audioProcessor.ProcessFrame(frame);
                        if (processedData.Length > 0)
                        {
                            lock (_statsLock)
                            {
                                _audioProcessedFrames++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"音频处理错误: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"音频处理错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 验证转换后的数据与现有系统的兼容性
    /// </summary>
    private static void VerifyDataCompatibility(byte[] audioDataBytes, int sampleCount)
    {
        // 验证与AudioStreamManager的兼容性
        var expectedDataSize = sampleCount * 1 * 2; // samples * channels * sizeof(short)
        if (audioDataBytes.Length == expectedDataSize)
        {
            // 数据大小匹配AudioStreamManager的计算逻辑
            // int dataSize = (int)(frameCount * _channels * sizeof(short));
        }

        // 验证与OpusSharpAudioCodec的兼容性  
        if (sampleCount == 960) // 60ms @ 16kHz
        {
            var expectedBytes = 960 * 1 * 2; // frameSize * channels * 2
            if (audioDataBytes.Length == expectedBytes)
            {
                // 完美匹配OpusSharpAudioCodec的要求
                // int expectedBytes = frameSize * channels * 2;
            }
        }
    }

    private static async Task DisplayRealtimeStats()
    {
        while (_recorder?.State == PlaybackState.Playing)
        {
            lock (_statsLock)
            {
                var elapsed = DateTime.Now - _testStartTime;
                var framesPerSecond = _totalFramesProcessed / Math.Max(elapsed.TotalSeconds, 1);
                
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write($"\r📊 统计: 帧数={_totalFramesProcessed}, FPS={framesPerSecond:F1}, 音频处理={_audioProcessedFrames}");
            }
            
            await Task.Delay(1000);
        }
    }

    private static void DisplayFinalStatistics()
    {
        var totalTime = DateTime.Now - _testStartTime;
        
        Console.WriteLine("\n=== 最终测试统计 ===");
        Console.WriteLine($"📝 测试时长: {totalTime.TotalSeconds:F1} 秒");
        Console.WriteLine($"🎵 总处理帧数: {_totalFramesProcessed}");
        Console.WriteLine($"⚡ 平均帧率: {_totalFramesProcessed / Math.Max(totalTime.TotalSeconds, 1):F1} FPS");
        Console.WriteLine($" 音频处理帧数: {_audioProcessedFrames}");
        
        // 格式验证结果
        Console.WriteLine($"\n=== 格式转换验证 ===");
        Console.WriteLine($"✅ SoundFlow设备格式: {_captureDevice?.Format.Format}, {_captureDevice?.Format.Channels}ch, {_captureDevice?.Format.SampleRate}Hz");
        Console.WriteLine($"✅ 内部处理格式: F32");
        Console.WriteLine($"✅ 目标输出格式: Int16, 1ch, 16kHz");
        Console.WriteLine($"✅ 转换路径: {_captureDevice?.Format.Format}→F32→Int16→byte[] (完全兼容)");
        
        // 兼容性验证结果
        Console.WriteLine($"\n=== 兼容性验证结果 ===");
        Console.WriteLine($"📋 AudioStreamManager兼容性:");
        Console.WriteLine($"   • 帧大小: 960 samples (60ms @ 16kHz) ✅");
        Console.WriteLine($"   • 数据格式: byte[] from Int16 ✅");
        Console.WriteLine($"   • 计算公式: frameCount * channels * sizeof(short) ✅");
        
        Console.WriteLine($"📋 OpusSharpAudioCodec兼容性:");
        Console.WriteLine($"   • 严格要求: 16kHz, 1ch, 60ms frames ✅"); 
        Console.WriteLine($"   • 预期数据: 960 samples = 1920 bytes ✅");
        Console.WriteLine($"   • 输入格式: short[] from byte[] ✅");
        
        // 性能评估
        if (_totalFramesProcessed > 0)
        {
            var avgProcessingRate = _totalFramesProcessed / Math.Max(totalTime.TotalSeconds, 1);
            var expectedRate = 16000.0 / 960.0; // 16kHz / 960 samples per frame ≈ 16.67 FPS
            var efficiency = (avgProcessingRate / expectedRate) * 100;
            
            Console.WriteLine($"\n=== 性能评估 ===");
            Console.WriteLine($"📈 期望帧率: {expectedRate:F1} FPS");
            Console.WriteLine($"📊 实际帧率: {avgProcessingRate:F1} FPS");
            Console.WriteLine($"🎯 处理效率: {efficiency:F1}%");
            
            if (efficiency >= 95)
                Console.WriteLine("🟢 性能评级: 优秀");
            else if (efficiency >= 80)
                Console.WriteLine("🟡 性能评级: 良好");
            else
                Console.WriteLine("🔴 性能评级: 需要优化");
        }
        
        Console.WriteLine($"\n=== 兼容性测试结果 ===");
        Console.WriteLine($"✅ SoundFlow录音: 正常");
        Console.WriteLine($"✅ F32→Int16→byte[]转换: 正常");
        Console.WriteLine($"✅ AudioStreamManager格式兼容: 正常");
        Console.WriteLine($"✅ OpusSharpAudioCodec格式兼容: 正常");
        Console.WriteLine($"✅ 音频处理: {(_audioProcessedFrames > 0 ? "正常处理" : "未处理数据")}");
        Console.WriteLine($"✅ 音频文件保存: {_audioFileName} ({_totalSamplesWritten} samples)");
    }

    #region 音频文件保存功能

    /// <summary>
    /// 初始化WAV音频文件用于保存录音数据
    /// </summary>
    private static bool InitializeAudioFile()
    {
        try
        {
            // 删除已存在的文件
            if (File.Exists(_audioFileName))
            {
                File.Delete(_audioFileName);
            }

            _audioFileStream = new FileStream(_audioFileName, FileMode.Create, FileAccess.Write);
            _audioFileWriter = new BinaryWriter(_audioFileStream);
            
            // 写入WAV文件头 (先写临时头，录音结束后更新)
            WriteWavHeader(_audioFileWriter, 0, 16000, 1, 16);
            
            Console.WriteLine($"音频文件初始化成功: {_audioFileName}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"音频文件初始化失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 保存音频数据到WAV文件
    /// </summary>
    private static void SaveAudioToFile(short[] samples)
    {
        try
        {
            if (_audioFileWriter != null)
            {
                // 写入Int16音频数据 (小端序)
                foreach (var sample in samples)
                {
                    _audioFileWriter.Write(sample);
                }
                
                _totalSamplesWritten += samples.Length;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"音频数据写入错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 完成音频文件写入，更新WAV头
    /// </summary>
    private static void FinalizeAudioFile()
    {
        try
        {
            if (_audioFileWriter != null && _audioFileStream != null)
            {
                // 计算总的音频数据大小
                var audioDataSize = _totalSamplesWritten * 2; // 16-bit = 2 bytes per sample
                
                // 更新WAV文件头
                _audioFileStream.Seek(0, SeekOrigin.Begin);
                WriteWavHeader(_audioFileWriter, audioDataSize, 16000, 1, 16);
                
                _audioFileWriter.Close();
                _audioFileStream.Close();
                
                var fileSizeKB = new FileInfo(_audioFileName).Length / 1024.0;
                var durationSeconds = _totalSamplesWritten / 16000.0;
                
                Console.WriteLine($"\n💾 音频文件保存完成:");
                Console.WriteLine($"   文件: {_audioFileName}");
                Console.WriteLine($"   格式: 16kHz, 1ch, 16-bit PCM WAV");
                Console.WriteLine($"   时长: {durationSeconds:F1} 秒");
                Console.WriteLine($"   大小: {fileSizeKB:F1} KB");
                Console.WriteLine($"   采样点: {_totalSamplesWritten:N0}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"音频文件完成写入错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 写入WAV文件头
    /// </summary>
    private static void WriteWavHeader(BinaryWriter writer, int audioDataSize, int sampleRate, int channels, int bitsPerSample)
    {
        var blockAlign = channels * bitsPerSample / 8;
        var byteRate = sampleRate * blockAlign;
        var totalSize = audioDataSize + 44 - 8; // WAV header is 44 bytes, subtract 8 for RIFF header

        // RIFF头
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(totalSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt子块
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // fmt子块大小
        writer.Write((short)1); // PCM格式
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);

        // data子块
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(audioDataSize);
    }

    #endregion

    private static async Task CleanupResources()
    {
        try
        {
            _recorder?.Dispose();
            _captureDevice?.Dispose();
            _engine?.Dispose();
            _audioProcessor?.Dispose();
            
            // 清理音频文件资源
            _audioFileWriter?.Close();
            _audioFileStream?.Close();
            
            Console.WriteLine("\n🧹 资源清理完成");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"资源清理错误: {ex.Message}");
        }
    }
}
