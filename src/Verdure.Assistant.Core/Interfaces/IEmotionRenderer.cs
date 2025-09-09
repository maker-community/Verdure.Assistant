using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Verdure.Assistant.Core.Interfaces
{
    /// <summary>
    /// 表情渲染器接口 - 支持多种渲染方式（GIF、Lottie、Emoji等）
    /// </summary>
    public interface IEmotionRenderer
    {
        /// <summary>
        /// 渲染器类型标识
        /// </summary>
        string RendererType { get; }

        /// <summary>
        /// 渲染器优先级（数值越高优先级越高）
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// 检查是否可以渲染指定的请求
        /// </summary>
        Task<bool> CanRenderAsync(EmotionRenderRequest request);

        /// <summary>
        /// 渲染表情
        /// </summary>
        Task RenderAsync(EmotionRenderRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止当前渲染
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// 渲染完成事件
        /// </summary>
        event EventHandler<EmotionRenderEventArgs>? RenderCompleted;
    }

    /// <summary>
    /// 表情渲染请求
    /// </summary>
    public class EmotionRenderRequest
    {
        public string EmotionType { get; set; } = string.Empty;
        public string? AssetPath { get; set; }
        public int Loops { get; set; } = 1;
        public int FPS { get; set; } = 30;
        public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(3);
        public Dictionary<string, object> Properties { get; set; } = new();
    }

    /// <summary>
    /// 表情渲染事件参数
    /// </summary>
    public class EmotionRenderEventArgs : EventArgs
    {
        public string EmotionType { get; set; } = string.Empty;
        public string RendererType { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        public EmotionRenderEventArgs(string emotionType, string rendererType, bool success, string? errorMessage = null)
        {
            EmotionType = emotionType;
            RendererType = rendererType;
            Success = success;
            ErrorMessage = errorMessage;
        }
    }
}
