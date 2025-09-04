using Microsoft.Extensions.Logging;

namespace InterruptArchitectureTest.Sources;

/// <summary>
/// 简化的音频数据处理器 - 参考SoundFlowRecordingCodecTest
/// 专门用于InterruptArchitectureTest中的音频处理和转码验证
/// 模拟编码器行为并提供音频数据格式转换
/// </summary>
public class InterruptAudioProcessor : IDisposable
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _frameSize;
    private readonly ILogger? _logger;
    private bool _disposed = false;
    private int _processedFrameCount = 0;
    private int _totalSamplesProcessed = 0;
    private DateTime _startTime = DateTime.Now;

    public InterruptAudioProcessor(int sampleRate = 16000, int channels = 1, ILogger? logger = null)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _logger = logger;
        
        // 计算帧大小 (60ms) - 参考SoundFlowRecordingCodecTest
        _frameSize = sampleRate * 60 / 1000; // 60ms frame at given sample rate
        
        _logger?.LogInformation("AudioProcessor initialized: {SampleRate}Hz, {Channels}ch, {FrameSize} samples/frame", 
            _sampleRate, _channels, _frameSize);
    }

    /// <summary>
    /// 处理音频帧 - 核心转码逻辑
    /// 参考SoundFlowRecordingCodecTest的ProcessAudioData方法
    /// </summary>
    public AudioProcessResult ProcessFrame(float[] inputSamples)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(InterruptAudioProcessor));
            
        if (inputSamples == null)
            throw new ArgumentNullException(nameof(inputSamples));

        _processedFrameCount++;
        _totalSamplesProcessed += inputSamples.Length;
        
        try
        {
            // 1. F32 → Int16 转换 (参考SoundFlowRecordingCodecTest的核心转码)
            var int16Samples = ConvertF32ToInt16(inputSamples);
            
            // 2. 转换为byte[]格式 (匹配AudioStreamManager.OnAudioDataReceived的输出)
            var audioDataBytes = ConvertInt16ToBytes(int16Samples);
            
            // 3. 验证数据格式兼容性
            var compatibility = VerifyDataCompatibility(audioDataBytes, inputSamples.Length);
            
            // 4. 计算音频统计信息
            var statistics = CalculateAudioStatistics(inputSamples);
            
            return new AudioProcessResult
            {
                Success = true,
                Int16Samples = int16Samples,
                AudioDataBytes = audioDataBytes,
                InputSampleCount = inputSamples.Length,
                OutputByteCount = audioDataBytes.Length,
                FrameNumber = _processedFrameCount,
                AudioStatistics = statistics,
                CompatibilityInfo = compatibility
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing audio frame #{FrameNumber}", _processedFrameCount);
            return new AudioProcessResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FrameNumber = _processedFrameCount
            };
        }
    }

    /// <summary>
    /// F32 → Int16 转换 (参考SoundFlowRecordingCodecTest)
    /// </summary>
    private short[] ConvertF32ToInt16(float[] samples)
    {
        var int16Samples = new short[samples.Length];
        
        for (int i = 0; i < samples.Length; i++)
        {
            // 限制范围到 [-1.0, 1.0] 并转换为 Int16
            var clampedSample = Math.Max(-1.0f, Math.Min(1.0f, samples[i]));
            int16Samples[i] = (short)(clampedSample * short.MaxValue);
        }
        
        return int16Samples;
    }

    /// <summary>
    /// Int16 → byte[] 转换 (参考SoundFlowRecordingCodecTest)
    /// </summary>
    private byte[] ConvertInt16ToBytes(short[] samples)
    {
        var audioDataBytes = new byte[samples.Length * 2]; // 2 bytes per Int16
        for (int i = 0; i < samples.Length; i++)
        {
            var bytes = BitConverter.GetBytes(samples[i]);
            audioDataBytes[i * 2] = bytes[0];
            audioDataBytes[i * 2 + 1] = bytes[1];
        }
        return audioDataBytes;
    }

    /// <summary>
    /// 验证转换后的数据与现有系统的兼容性 (参考SoundFlowRecordingCodecTest)
    /// </summary>
    private CompatibilityInfo VerifyDataCompatibility(byte[] audioDataBytes, int sampleCount)
    {
        var info = new CompatibilityInfo();
        
        // 验证与AudioStreamManager的兼容性
        var expectedDataSize = sampleCount * _channels * 2; // samples * channels * sizeof(short)
        info.AudioStreamManagerCompatible = (audioDataBytes.Length == expectedDataSize);
        
        // 验证与OpusSharpAudioCodec的兼容性  
        if (sampleCount == _frameSize) // 60ms @ _sampleRate
        {
            var expectedBytes = _frameSize * _channels * 2; // frameSize * channels * 2
            info.OpusSharpCodecCompatible = (audioDataBytes.Length == expectedBytes);
        }
        
        // 验证帧大小
        info.CorrectFrameSize = (sampleCount == _frameSize);
        info.ExpectedFrameSize = _frameSize;
        info.ActualSampleCount = sampleCount;
        
        return info;
    }

    /// <summary>
    /// 计算音频统计信息
    /// </summary>
    private AudioStatistics CalculateAudioStatistics(float[] samples)
    {
        if (samples.Length == 0) 
            return new AudioStatistics { IsEmpty = true };

        // 计算RMS值
        double sum = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }
        var rms = (float)Math.Sqrt(sum / samples.Length);
        
        // 计算dB值
        var db = 20 * Math.Log10(rms + 1e-10); // 避免log(0)
        
        // 检测静音
        var isSilent = rms < 0.001f;
        
        // 计算峰值
        var peak = samples.Max(Math.Abs);
        
        return new AudioStatistics
        {
            RMS = rms,
            DB = (float)db,
            Peak = peak,
            IsSilent = isSilent,
            SampleCount = samples.Length,
            IsEmpty = false
        };
    }

    /// <summary>
    /// 获取处理器统计信息
    /// </summary>
    public ProcessorStatistics GetStatistics()
    {
        var elapsed = DateTime.Now - _startTime;
        var avgFramesPerSecond = _processedFrameCount / Math.Max(elapsed.TotalSeconds, 1);
        var expectedFrameRate = (double)_sampleRate / _frameSize; // 理论帧率
        var efficiency = (avgFramesPerSecond / expectedFrameRate) * 100;
        
        return new ProcessorStatistics
        {
            ProcessedFrameCount = _processedFrameCount,
            TotalSamplesProcessed = _totalSamplesProcessed,
            ElapsedTime = elapsed,
            AvgFramesPerSecond = avgFramesPerSecond,
            ExpectedFrameRate = expectedFrameRate,
            ProcessingEfficiency = efficiency,
            SampleRate = _sampleRate,
            Channels = _channels,
            FrameSize = _frameSize
        };
    }

    public int FrameSize => _frameSize;
    public int SampleRate => _sampleRate;
    public int Channels => _channels;
    public int ProcessedFrameCount => _processedFrameCount;

    public void Dispose()
    {
        if (!_disposed)
        {
            var stats = GetStatistics();
            _logger?.LogInformation(
                "AudioProcessor disposed. Frames: {Frames}, Samples: {Samples}, Efficiency: {Efficiency:F1}%",
                stats.ProcessedFrameCount, stats.TotalSamplesProcessed, stats.ProcessingEfficiency);
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 音频处理结果
/// </summary>
public class AudioProcessResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public short[]? Int16Samples { get; set; }
    public byte[]? AudioDataBytes { get; set; }
    public int InputSampleCount { get; set; }
    public int OutputByteCount { get; set; }
    public int FrameNumber { get; set; }
    public AudioStatistics? AudioStatistics { get; set; }
    public CompatibilityInfo? CompatibilityInfo { get; set; }
}

/// <summary>
/// 音频统计信息
/// </summary>
public class AudioStatistics
{
    public float RMS { get; set; }
    public float DB { get; set; }
    public float Peak { get; set; }
    public bool IsSilent { get; set; }
    public int SampleCount { get; set; }
    public bool IsEmpty { get; set; }
}

/// <summary>
/// 兼容性信息
/// </summary>
public class CompatibilityInfo
{
    public bool AudioStreamManagerCompatible { get; set; }
    public bool OpusSharpCodecCompatible { get; set; }
    public bool CorrectFrameSize { get; set; }
    public int ExpectedFrameSize { get; set; }
    public int ActualSampleCount { get; set; }
}

/// <summary>
/// 处理器统计信息
/// </summary>
public class ProcessorStatistics
{
    public int ProcessedFrameCount { get; set; }
    public int TotalSamplesProcessed { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public double AvgFramesPerSecond { get; set; }
    public double ExpectedFrameRate { get; set; }
    public double ProcessingEfficiency { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int FrameSize { get; set; }
}
