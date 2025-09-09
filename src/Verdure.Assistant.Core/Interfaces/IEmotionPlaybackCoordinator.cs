using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Verdure.Assistant.Core.Interfaces
{
    /// <summary>
    /// 表情播放协调器接口 - 统一管理不同渲染器的表情播放
    /// </summary>
    public interface IEmotionPlaybackCoordinator
    {
        /// <summary>
        /// 播放指定表情
        /// </summary>
        Task PlayEmotionAsync(string emotionType, EmotionPlaybackOptions? options = null);

        /// <summary>
        /// 停止当前表情播放
        /// </summary>
        Task StopCurrentEmotionAsync();

        /// <summary>
        /// 获取当前播放状态
        /// </summary>
        EmotionPlaybackStatus GetCurrentStatus();

        /// <summary>
        /// 表情播放开始事件
        /// </summary>
        event EventHandler<EmotionPlaybackEventArgs>? EmotionPlaybackStarted;

        /// <summary>
        /// 表情播放完成事件
        /// </summary>
        event EventHandler<EmotionPlaybackEventArgs>? EmotionPlaybackCompleted;

        /// <summary>
        /// 表情播放错误事件
        /// </summary>
        event EventHandler<EmotionPlaybackErrorEventArgs>? EmotionPlaybackError;
    }

    /// <summary>
    /// 表情播放选项
    /// </summary>
    public class EmotionPlaybackOptions
    {
        public int Loops { get; set; } = 1;
        public int FPS { get; set; } = 30;
        public TimeSpan? Duration { get; set; }
        public string? PreferredRendererType { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new();
    }

    /// <summary>
    /// 表情播放状态
    /// </summary>
    public class EmotionPlaybackStatus
    {
        public bool IsPlaying { get; set; }
        public string? CurrentEmotion { get; set; }
        public string? CurrentRenderer { get; set; }
        public DateTime? StartTime { get; set; }
        public TimeSpan? Duration { get; set; }
    }

    /// <summary>
    /// 表情播放事件参数
    /// </summary>
    public class EmotionPlaybackEventArgs : EventArgs
    {
        public string EmotionType { get; set; } = string.Empty;
        public string RendererType { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public EmotionPlaybackEventArgs(string emotionType, string rendererType)
        {
            EmotionType = emotionType;
            RendererType = rendererType;
        }
    }

    /// <summary>
    /// 表情播放错误事件参数
    /// </summary>
    public class EmotionPlaybackErrorEventArgs : EventArgs
    {
        public string EmotionType { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public Exception? Exception { get; set; }

        public EmotionPlaybackErrorEventArgs(string emotionType, string errorMessage, Exception? exception = null)
        {
            EmotionType = emotionType;
            ErrorMessage = errorMessage;
            Exception = exception;
        }
    }
}
