using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.WinUI.Services
{
    /// <summary>
    /// WinUI Emoji 表情渲染器 - 作为后备渲染器
    /// </summary>
    public class WinUIEmojiEmotionRenderer : IEmotionRenderer
    {
        private readonly ILogger<WinUIEmojiEmotionRenderer> _logger;

        public string RendererType => "emoji";
        public int Priority => 1; // 最低优先级，作为后备

        public event EventHandler<EmotionRenderEventArgs>? RenderCompleted;

        public WinUIEmojiEmotionRenderer(ILogger<WinUIEmojiEmotionRenderer> logger)
        {
            _logger = logger;
        }

        public async Task<bool> CanRenderAsync(EmotionRenderRequest request)
        {
            // Emoji渲染器总是可用
            return await Task.FromResult(true);
        }

        public async Task RenderAsync(EmotionRenderRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Starting Emoji emotion render: {EmotionType}", request.EmotionType);

                // 获取表情符号
                var emoji = request.AssetPath ?? GetEmotionEmoji(request.EmotionType);

                // 通过事件通知UI更新
                OnEmojiRenderRequested(new EmojiRenderEventArgs
                {
                    EmotionType = request.EmotionType,
                    EmojiText = emoji,
                    Duration = request.Duration
                });

                // 等待显示完成
                await Task.Delay(request.Duration, cancellationToken);

                // 播放完成
                RenderCompleted?.Invoke(this, new EmotionRenderEventArgs(request.EmotionType, RendererType, true));
                
                _logger.LogDebug("Emoji emotion render completed: {EmotionType} -> {Emoji}", request.EmotionType, emoji);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Emoji emotion render cancelled: {EmotionType}", request.EmotionType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Emoji emotion render failed: {EmotionType}", request.EmotionType);
                RenderCompleted?.Invoke(this, new EmotionRenderEventArgs(request.EmotionType, RendererType, false, ex.Message));
            }
        }

        public async Task StopAsync()
        {
            // 通知UI停止显示
            OnEmojiRenderStopped();
            await Task.CompletedTask;
        }

        private string GetEmotionEmoji(string emotionType)
        {
            return emotionType.ToLowerInvariant() switch
            {
                "neutral" => "😊",
                "happy" => "😄",
                "sad" => "😢",
                "angry" => "😠",
                "surprised" => "😲",
                "confused" => "😕",
                "thinking" => "🤔",
                "speaking" => "🗣️",
                "listening" => "👂",
                "laughing" => "😂",
                "loving" => "😍",
                "embarrassed" => "😳",
                "shocked" => "😱",
                "winking" => "😉",
                "cool" => "😎",
                "relaxed" => "😌",
                "sleepy" => "😴",
                "silly" => "🤪",
                "talking" => "🗣️",
                _ => "😊"
            };
        }

        private void OnEmojiRenderRequested(EmojiRenderEventArgs args)
        {
            EmojiRenderRequested?.Invoke(this, args);
        }

        private void OnEmojiRenderStopped()
        {
            EmojiRenderStopped?.Invoke(this, EventArgs.Empty);
        }

        // UI事件
        public static event EventHandler<EmojiRenderEventArgs>? EmojiRenderRequested;
        public static event EventHandler? EmojiRenderStopped;
    }

    /// <summary>
    /// Emoji渲染事件参数
    /// </summary>
    public class EmojiRenderEventArgs : EventArgs
    {
        public string EmotionType { get; set; } = string.Empty;
        public string EmojiText { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
    }
}
