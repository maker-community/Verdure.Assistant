namespace Verdure.Assistant.MAUI.Services;

/// <summary>
/// 音频服务管理器接口
/// </summary>
public interface IAudioServiceManager
{
    bool IsServiceRunning { get; }
    bool IsRecording { get; }
    bool HasLastRecording { get; }
    
    event EventHandler<string>? StatusChanged;
    event EventHandler<string>? AudioDataReceived;
    event EventHandler<bool>? RecordingAvailable;
    
    Task<bool> StartServiceAsync();
    Task<bool> StopServiceAsync();
    Task<bool> StartRecordingAsync();
    Task<bool> StopRecordingAsync();
    Task<bool> SpeakTextAsync(string text);
    Task<bool> PlayMp3FileAsync(string fileName = "test.mp3");
    Task<bool> StopMp3PlaybackAsync();
    Task<bool> PlayLastRecordingAsync();
    Task<bool> StopRecordingPlaybackAsync();
}
