using OpusSharp.Core;
using XiaoZhi.Core.Interfaces;

namespace XiaoZhi.Core.Services;

/// <summary>
/// OpusSharp音频编解码器实现 - 作为Concentus的替代方案进行对比测试
/// </summary>
public class OpusSharpAudioCodec : IAudioCodec
{
    private OpusEncoder? _encoder;
    private OpusDecoder? _decoder;
    private readonly object _lock = new();
    private int _currentSampleRate;
    private int _currentChannels;

    public byte[] Encode(byte[] pcmData, int sampleRate, int channels)
    {
        lock (_lock)
        {
            if (_encoder == null || _currentSampleRate != sampleRate || _currentChannels != channels)
            {
                _encoder?.Dispose();
                _encoder = new OpusEncoder(sampleRate, channels, OpusPredefinedValues.OPUS_APPLICATION_AUDIO);
                _currentSampleRate = sampleRate;
                _currentChannels = channels;
            }

            try
            {
                // 计算帧大小 (采样数，不是字节数)
                int frameSize = sampleRate * 60 / 1000; // 60ms帧，匹配Python配置
                
                // 确保输入数据长度正确 (16位音频 = 2字节/样本)
                int expectedBytes = frameSize * channels * 2;
                if (pcmData.Length != expectedBytes)
                {
                    // 调整数据长度或填充零
                    byte[] adjustedData = new byte[expectedBytes];
                    if (pcmData.Length < expectedBytes)
                    {
                        // 数据不足，复制现有数据并填充零
                        Array.Copy(pcmData, adjustedData, pcmData.Length);
                    }
                    else
                    {
                        // 数据过多，截断
                        Array.Copy(pcmData, adjustedData, expectedBytes);
                    }
                    pcmData = adjustedData;
                }

                // 转换为16位短整型数组
                short[] pcmShorts = new short[frameSize * channels];
                for (int i = 0; i < pcmShorts.Length && i * 2 + 1 < pcmData.Length; i++)
                {
                    pcmShorts[i] = BitConverter.ToInt16(pcmData, i * 2);
                }                // OpusSharp编码 - 使用正确的API
                byte[] outputBuffer = new byte[4000]; // Opus最大包大小
                int encodedLength = _encoder.Encode(pcmShorts, frameSize, outputBuffer, outputBuffer.Length);

                if (encodedLength > 0)
                {
                    // 返回实际编码的数据
                    byte[] result = new byte[encodedLength];
                    Array.Copy(outputBuffer, result, encodedLength);
                    return result;
                }

                return Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"OpusSharp编码失败: {ex.Message}");
                return Array.Empty<byte>();
            }
        }
    }

    public byte[] Decode(byte[] encodedData, int sampleRate, int channels)
    {
        lock (_lock)
        {
            if (_decoder == null || _currentSampleRate != sampleRate || _currentChannels != channels)
            {
                _decoder?.Dispose();
                _decoder = new OpusDecoder(sampleRate, channels);
                _currentSampleRate = sampleRate;
                _currentChannels = channels;
            }

            try
            {
                // 计算帧大小 (采样数，不是字节数)
                int frameSize = sampleRate * 60 / 1000; // 60ms帧                // OpusSharp解码 - 使用正确的API
                short[] outputBuffer = new short[frameSize * channels];
                int decodedSamples = _decoder.Decode(encodedData, encodedData.Length, outputBuffer, frameSize, false);
                
                if (decodedSamples > 0)
                {
                    // 转换为字节数组
                    byte[] pcmBytes = new byte[decodedSamples * channels * 2];
                    Buffer.BlockCopy(outputBuffer, 0, pcmBytes, 0, decodedSamples * channels * 2);
                    return pcmBytes;
                }
                
                // 返回静音数据而不是空数组，保持音频流连续性
                int silenceFrameSize = frameSize * channels * 2;
                byte[] silenceData = new byte[silenceFrameSize];
                return silenceData;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"OpusSharp解码失败: {ex.Message}");
                
                // 返回静音数据而不是空数组，保持音频流连续性
                int frameSize = sampleRate * 60 / 1000; // 60ms帧
                byte[] silenceData = new byte[frameSize * channels * 2];
                return silenceData;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _encoder?.Dispose();
            _decoder?.Dispose();
        }
    }
}
