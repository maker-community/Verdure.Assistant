using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Interfaces;

/// <summary>
/// 音乐播放器服务接口
/// </summary>
public interface IMusicPlayerService
{
    /// <summary>
    /// 音乐播放状态变化事件
    /// </summary>
    event EventHandler<MusicPlaybackEventArgs>? PlaybackStateChanged;

    /// <summary>
    /// 歌词更新事件
    /// </summary>
    event EventHandler<LyricUpdateEventArgs>? LyricUpdated;

    /// <summary>
    /// 播放进度更新事件
    /// </summary>
    event EventHandler<ProgressUpdateEventArgs>? ProgressUpdated;

    /// <summary>
    /// 是否正在播放
    /// </summary>
    bool IsPlaying { get; }

    /// <summary>
    /// 是否暂停
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// 当前歌曲信息
    /// </summary>
    MusicTrack? CurrentTrack { get; }

    /// <summary>
    /// 当前播放位置（秒）
    /// </summary>
    double CurrentPosition { get; }

    /// <summary>
    /// 搜索并播放歌曲
    /// </summary>
    /// <param name="songName">歌曲名称</param>
    /// <returns>播放结果</returns>
    Task<PlaybackResult> SearchAndPlayAsync(string songName);

    /// <summary>
    /// 仅搜索歌曲（不播放）
    /// </summary>
    /// <param name="songName">歌曲名称</param>
    /// <returns>搜索结果</returns>
    Task<SearchResult> SearchSongAsync(string songName);

    /// <summary>
    /// 播放/暂停切换
    /// </summary>
    /// <returns>操作结果</returns>
    Task<PlaybackResult> TogglePlayPauseAsync();

    /// <summary>
    /// 停止播放
    /// </summary>
    /// <returns>操作结果</returns>
    Task<PlaybackResult> StopAsync();

    /// <summary>
    /// 跳转到指定位置
    /// </summary>
    /// <param name="position">跳转位置（秒）</param>
    /// <returns>操作结果</returns>
    Task<PlaybackResult> SeekAsync(double position);

    /// <summary>
    /// 获取当前歌曲歌词
    /// </summary>
    /// <returns>歌词文本</returns>
    Task<string> GetLyricsAsync();

    /// <summary>
    /// 设置音量
    /// </summary>
    /// <param name="volume">音量 (0-100)</param>
    /// <returns>操作结果</returns>
    Task<PlaybackResult> SetVolumeAsync(double volume);

    /// <summary>
    /// 清理缓存
    /// </summary>
    Task ClearCacheAsync();
}

/// <summary>
/// 音乐播放状态事件参数
/// </summary>
public class MusicPlaybackEventArgs : EventArgs
{
    public string Action { get; set; } = string.Empty; // play, pause, stop
    public MusicTrack? Track { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 歌词更新事件参数
/// </summary>
public class LyricUpdateEventArgs : EventArgs
{
    public string LyricText { get; set; } = string.Empty;
    public double Position { get; set; }
    public double Duration { get; set; }
}

/// <summary>
/// 进度更新事件参数
/// </summary>
public class ProgressUpdateEventArgs : EventArgs
{
    public double Position { get; set; }
    public double Duration { get; set; }
    public double Progress { get; set; } // 0-100百分比
}
