namespace SoundFlowRecordingCodecTest;

/// <summary>
/// 简单的音频数据处理器
/// 专门用于测试SoundFlow录音转码兼容性
/// 模拟编码器行为但实际上只是验证数据格式
/// </summary>
public class SimpleAudioProcessor : IDisposable
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _frameSize;
    private bool _disposed = false;
    private int _processedFrameCount = 0;

    public SimpleAudioProcessor(int sampleRate = 16000, int channels = 1)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        
        // 计算帧大小 (60ms)
        _frameSize = sampleRate * 60 / 1000; // 60ms frame at given sample rate
        
        Console.WriteLine($"AudioProcessor initialized: {_sampleRate}Hz, {_channels}ch, {_frameSize} samples/frame");
    }

    public byte[] ProcessFrame(short[] pcmData)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SimpleAudioProcessor));
            
        if (pcmData == null)
            throw new ArgumentNullException(nameof(pcmData));
            
        if (pcmData.Length != _frameSize * _channels)
            throw new ArgumentException($"PCM data length must be {_frameSize * _channels} samples for {_sampleRate}Hz, {_channels}ch, 60ms frame");

        _processedFrameCount++;
        
        // 模拟编码处理 - 简单地将Int16数据转换为字节数组
        var result = new byte[pcmData.Length * 2]; // 2 bytes per Int16
        Buffer.BlockCopy(pcmData, 0, result, 0, result.Length);
        
        return result;
    }

    public int FrameSize => _frameSize;
    public int SampleRate => _sampleRate;
    public int Channels => _channels;
    public int ProcessedFrameCount => _processedFrameCount;

    public void Dispose()
    {
        if (!_disposed)
        {
            Console.WriteLine($"AudioProcessor disposed. Total frames processed: {_processedFrameCount}");
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
