namespace XiaoZhi.Core.Interfaces;

/// <summary>
/// 音频录制接口
/// </summary>
public interface IAudioRecorder
{    
    /// <summary>
    /// 音频数据接收事件
    /// </summary>
    event EventHandler<byte[]>? DataAvailable;

    /// <summary>
    /// 开始录制
    /// </summary>
    /// <param name="sampleRate">采样率</param>
    /// <param name="channels">声道数</param>
    Task StartRecordingAsync(int sampleRate = 16000, int channels = 1);

    /// <summary>
    /// 停止录制
    /// </summary>
    Task StopRecordingAsync();

    /// <summary>
    /// 是否正在录制
    /// </summary>
    bool IsRecording { get; }
}
