namespace Verdure.Assistant.Core.Interfaces;

/// <summary>
/// 音频编解码接口
/// </summary>
public interface IAudioCodec
{
    /// <summary>
    /// 编码音频数据
    /// </summary>
    /// <param name="pcmData">PCM音频数据</param>
    /// <param name="sampleRate">采样率</param>
    /// <param name="channels">声道数</param>
    /// <returns>编码后的音频数据</returns>
    byte[] Encode(byte[] pcmData, int sampleRate, int channels);

    /// <summary>
    /// 解码音频数据
    /// </summary>
    /// <param name="encodedData">编码的音频数据</param>
    /// <param name="sampleRate">采样率</param>
    /// <param name="channels">声道数</param>
    /// <returns>PCM音频数据</returns>
    byte[] Decode(byte[] encodedData, int sampleRate, int channels);
}
