using Concentus;
using Concentus.Enums;
using Concentus.Structs;
using XiaoZhi.Core.Interfaces;

namespace XiaoZhi.Core.Services;

/// <summary>
/// Opus音频编解码器实现
/// </summary>
public class OpusAudioCodec : IAudioCodec
{    private OpusEncoder? _encoder;
    private OpusDecoder? _decoder;
    private readonly object _lock = new();
    private int _currentSampleRate;
    private int _currentChannels;    
    public byte[] Encode(byte[] pcmData, int sampleRate, int channels)
    {
        lock (_lock)
        {
            if (_encoder == null || _currentSampleRate != sampleRate || _currentChannels != channels)
            {                _encoder?.Dispose();
                _encoder = (OpusEncoder)OpusCodecFactory.CreateEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_AUDIO);
                _currentSampleRate = sampleRate;
                _currentChannels = channels;
            }

            try
            {
                // 计算帧大小 (采样数，不是字节数)
                int frameSize = sampleRate * 60 / 1000; // 60ms帧
                
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
                }                // 转换为16位短整型数组
                short[] pcmShorts = new short[frameSize * channels];
                for (int i = 0; i < pcmShorts.Length && i * 2 + 1 < pcmData.Length; i++)
                {
                    pcmShorts[i] = BitConverter.ToInt16(pcmData, i * 2);
                }                // 编码 - 使用现代Span-based API
                byte[] opusData = new byte[4000]; // Opus最大包大小
                ReadOnlySpan<short> pcmSpan = new ReadOnlySpan<short>(pcmShorts);
                Span<byte> outputSpan = new Span<byte>(opusData);
                int encodedLength = _encoder.Encode(pcmSpan, frameSize, outputSpan, opusData.Length);

                // 返回实际编码的数据
                byte[] result = new byte[encodedLength];
                Array.Copy(opusData, result, encodedLength);
                return result;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Opus编码失败: {ex.Message}");
                // 发生错误时返回空数组，避免发送错误数据
                return Array.Empty<byte>();
            }
        }
    }

    public byte[] Decode(byte[] encodedData, int sampleRate, int channels)
    {
        lock (_lock)        {
            if (_decoder == null || _currentSampleRate != sampleRate || _currentChannels != channels)
            {                _decoder?.Dispose();
                _decoder = (OpusDecoder)OpusCodecFactory.CreateDecoder(sampleRate, channels);
                _currentSampleRate = sampleRate;
                _currentChannels = channels;
            }

            // 简化版本：直接返回输入数据（暂时跳过实际解码）
            // TODO: 实现完整的 Opus 解码逻辑
            return encodedData;
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
