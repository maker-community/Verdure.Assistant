using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.MAUI.Services
{
    /// <summary>
    /// MAUI GIF 表情渲染器
    /// 提供固定的表情路径映射，无需文件系统访问
    /// </summary>
    public class MauiGifEmotionRenderer : IEmotionRenderer
    {
        private readonly ILogger<MauiGifEmotionRenderer> _logger;
        private readonly HashSet<string> _availableEmotions;
        private readonly Dictionary<string, string> _emotionMappings;
        private CancellationTokenSource? _currentCancellationTokenSource;

        public string RendererType => "gif";
        public int Priority => 100; // GIF优先级最高

        public event EventHandler<EmotionRenderEventArgs>? RenderCompleted;

        // 静态事件，供HomePage订阅
        public static event EventHandler<MauiGifRenderEventArgs>? GifRenderRequested;
        public static event EventHandler? GifRenderStopped;

        public MauiGifEmotionRenderer(ILogger<MauiGifEmotionRenderer> logger)
        {
            _logger = logger;
            _availableEmotions = InitializeAvailableEmotions();
            _emotionMappings = InitializeEmotionMappings();
        }

        public Task<bool> CanRenderAsync(EmotionRenderRequest request)
        {
            if (string.IsNullOrEmpty(request.AssetPath))
                return Task.FromResult(false);

            // 检查是否是已知的表情名称
            var emotionName = GetEmotionName(request.AssetPath, request.EmotionType);
            var normalizedEmotion = NormalizeEmotionName(emotionName);
            
            return Task.FromResult(_availableEmotions.Contains(normalizedEmotion));
        }

        public Task RenderAsync(EmotionRenderRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                _currentCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                _logger.LogDebug("Starting MAUI GIF emotion render: {EmotionType} -> {AssetPath}", 
                    request.EmotionType, request.AssetPath);

                var emotionName = GetEmotionName(request.AssetPath, request.EmotionType);
                var normalizedEmotion = NormalizeEmotionName(emotionName);
                
                if (!_availableEmotions.Contains(normalizedEmotion))
                {
                    _logger.LogWarning("Emotion not available: {EmotionName}", emotionName);
                    normalizedEmotion = "neutral"; // 回退到默认表情
                }

                var gifPath = $"Emotions/{normalizedEmotion}.gif";

                // 通过事件通知UI更新
                OnGifRenderRequested(new MauiGifRenderEventArgs
                {
                    EmotionType = request.EmotionType,
                    GifPath = gifPath,
                    Loops = request.Loops,
                    Duration = request.Duration
                });

                // 模拟渲染完成
                var renderEventArgs = new EmotionRenderEventArgs(request.EmotionType, RendererType, true);

                RenderCompleted?.Invoke(this, renderEventArgs);

                _logger.LogDebug("MAUI GIF emotion render completed: {EmotionType} -> {GifPath}", 
                    request.EmotionType, gifPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during MAUI GIF emotion render");
                
                var renderEventArgs = new EmotionRenderEventArgs(request.EmotionType, RendererType, false, ex.Message);

                RenderCompleted?.Invoke(this, renderEventArgs);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            try
            {
                _currentCancellationTokenSource?.Cancel();
                _currentCancellationTokenSource?.Dispose();
                _currentCancellationTokenSource = null;

                OnGifRenderStopped();

                _logger.LogDebug("MAUI GIF emotion render stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping MAUI GIF emotion render");
            }

            return Task.CompletedTask;
        }

        private string GetEmotionName(string? assetPath, string emotionType)
        {
            // 优先使用emotionType
            if (!string.IsNullOrEmpty(emotionType))
                return emotionType;

            // 如果assetPath是表情名称，使用它
            if (!string.IsNullOrEmpty(assetPath) && !assetPath.Contains("/") && !assetPath.Contains("\\"))
            {
                return assetPath.Replace(".gif", "", StringComparison.OrdinalIgnoreCase);
            }

            return "neutral";
        }

        private string NormalizeEmotionName(string emotionType)
        {
            if (string.IsNullOrEmpty(emotionType))
                return "neutral";

            var normalized = emotionType.ToLowerInvariant().Trim();

            // 应用映射表
            if (_emotionMappings.TryGetValue(normalized, out var mapped))
            {
                return mapped;
            }

            return normalized;
        }

        private HashSet<string> InitializeAvailableEmotions()
        {
            // 定义所有可用的表情（与Emotions目录中的GIF文件对应）
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "angry", "confident", "confused", "cool", "crying", "delicious",
                "embarrassed", "funny", "happy", "kissy", "laughing", "loving",
                "neutral", "relaxed", "sad", "shocked", "silly", "sleepy",
                "surprised", "thinking", "winking"
            };
        }

        private Dictionary<string, string> InitializeEmotionMappings()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // 状态映射
                ["listening"] = "thinking",
                ["speaking"] = "happy",
                ["talking"] = "happy",
                ["processing"] = "thinking",
                ["idle"] = "neutral",
                ["waiting"] = "neutral",
                
                // 情感映射
                ["joy"] = "happy",
                ["excited"] = "happy",
                ["upset"] = "sad",
                ["furious"] = "angry",
                ["amazed"] = "surprised",
                ["puzzled"] = "confused",
                ["playful"] = "silly"
            };
        }

        private void OnGifRenderRequested(MauiGifRenderEventArgs args)
        {
            GifRenderRequested?.Invoke(this, args);
        }

        private void OnGifRenderStopped()
        {
            GifRenderStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// MAUI GIF渲染事件参数
    /// </summary>
    public class MauiGifRenderEventArgs : EventArgs
    {
        public string EmotionType { get; set; } = string.Empty;
        public string GifPath { get; set; } = string.Empty;
        public int Loops { get; set; } = 1;
        public TimeSpan Duration { get; set; }
    }
}