namespace XiaoZhi.Core.Interfaces;

/// <summary>
/// 音频播放接口
/// </summary>
public interface IAudioPlayer
{
    /// <summary>
    /// 播放音频数据
    /// </summary>
    /// <param name="audioData">音频数据</param>
    /// <param name="sampleRate">采样率</param>
    /// <param name="channels">声道数</param>
    Task PlayAsync(byte[] audioData, int sampleRate = 16000, int channels = 1);

    /// <summary>
    /// 停止播放
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// 是否正在播放
    /// </summary>
    bool IsPlaying { get; }
}
