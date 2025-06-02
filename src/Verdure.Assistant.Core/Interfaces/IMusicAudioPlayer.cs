using System;
using System.Threading.Tasks;

namespace Verdure.Assistant.Core.Interfaces
{
    /// <summary>
    /// 平台无关的音乐播放器接口
    /// </summary>
    public interface IMusicAudioPlayer : IDisposable
    {
        /// <summary>
        /// 当前播放位置
        /// </summary>
        TimeSpan CurrentPosition { get; }

        /// <summary>
        /// 音频总时长
        /// </summary>
        TimeSpan Duration { get; }

        /// <summary>
        /// 是否正在播放
        /// </summary>
        bool IsPlaying { get; }

        /// <summary>
        /// 是否已暂停
        /// </summary>
        bool IsPaused { get; }

        /// <summary>
        /// 音量 (0-100)
        /// </summary>
        double Volume { get; set; }

        /// <summary>
        /// 播放状态变化事件
        /// </summary>
        event EventHandler<MusicPlayerStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// 播放进度更新事件
        /// </summary>
        event EventHandler<MusicPlayerProgressEventArgs>? ProgressUpdated;

        /// <summary>
        /// 加载音频文件
        /// </summary>
        /// <param name="filePath">音频文件路径</param>
        Task LoadAsync(string filePath);

        /// <summary>
        /// 加载音频流
        /// </summary>
        /// <param name="url">音频流URL</param>
        Task LoadFromUrlAsync(string url);

        /// <summary>
        /// 开始播放
        /// </summary>
        Task PlayAsync();

        /// <summary>
        /// 暂停播放
        /// </summary>
        Task PauseAsync();

        /// <summary>
        /// 停止播放
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// 跳转到指定位置
        /// </summary>
        /// <param name="position">目标位置</param>
        Task SeekAsync(TimeSpan position);
    }

    /// <summary>
    /// 音乐播放器状态变化事件参数
    /// </summary>
    public class MusicPlayerStateChangedEventArgs : EventArgs
    {
        public MusicPlayerState State { get; }
        public string? ErrorMessage { get; }

        public MusicPlayerStateChangedEventArgs(MusicPlayerState state, string? errorMessage = null)
        {
            State = state;
            ErrorMessage = errorMessage;
        }
    }

    /// <summary>
    /// 音乐播放器进度事件参数
    /// </summary>
    public class MusicPlayerProgressEventArgs : EventArgs
    {
        public TimeSpan Position { get; }
        public TimeSpan Duration { get; }

        public MusicPlayerProgressEventArgs(TimeSpan position, TimeSpan duration)
        {
            Position = position;
            Duration = duration;
        }
    }

    /// <summary>
    /// 音乐播放器状态
    /// </summary>
    public enum MusicPlayerState
    {
        /// <summary>
        /// 空闲状态
        /// </summary>
        Idle,

        /// <summary>
        /// 加载中
        /// </summary>
        Loading,

        /// <summary>
        /// 已加载
        /// </summary>
        Loaded,

        /// <summary>
        /// 播放中
        /// </summary>
        Playing,

        /// <summary>
        /// 已暂停
        /// </summary>
        Paused,

        /// <summary>
        /// 已停止
        /// </summary>
        Stopped,

        /// <summary>
        /// 播放结束
        /// </summary>
        Ended,

        /// <summary>
        /// 错误状态
        /// </summary>
        Error
    }
}
