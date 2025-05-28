using Concentus;
using Concentus.Enums;
using Concentus.Structs;
using XiaoZhi.Core.Interfaces;

namespace XiaoZhi.Core.Services;

/// <summary>
/// Opus音频编解码器实现
/// </summary>
public class OpusAudioCodec : IAudioCodec
{
    private IOpusEncoder? _encoder;
    private IOpusDecoder? _decoder;
    private readonly object _lock = new();
    private int _currentSampleRate;
    private int _currentChannels;

    public byte[] Encode(byte[] pcmData, int sampleRate, int channels)
    {
        lock (_lock)        {
            if (_encoder == null || _currentSampleRate != sampleRate || _currentChannels != channels)
            {
                _encoder?.Dispose();
                _encoder = OpusCodecFactory.CreateEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_VOIP);
                _currentSampleRate = sampleRate;
                _currentChannels = channels;
            }

            // 简化版本：直接返回输入数据（暂时跳过实际编码）
            // TODO: 实现完整的 Opus 编码逻辑
            return pcmData;
        }
    }

    public byte[] Decode(byte[] encodedData, int sampleRate, int channels)
    {
        lock (_lock)        {
            if (_decoder == null || _currentSampleRate != sampleRate || _currentChannels != channels)
            {
                _decoder?.Dispose();
                _decoder = OpusCodecFactory.CreateDecoder(sampleRate, channels);
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
