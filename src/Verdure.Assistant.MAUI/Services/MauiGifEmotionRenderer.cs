using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.MAUI.Services
{
    /// <summary>
    /// MAUI GIF 表情渲染器
    /// 专门处理Android平台的GIF资源路径和显示
    /// </summary>
    public class MauiGifEmotionRenderer : IEmotionRenderer
    {
        private readonly ILogger<MauiGifEmotionRenderer> _logger;
        private readonly MauiResourceService _resourceService;
        private CancellationTokenSource? _currentCancellationTokenSource;

        public string RendererType => "gif";
        public int Priority => 100; // GIF优先级最高

        public event EventHandler<EmotionRenderEventArgs>? RenderCompleted;

        public MauiGifEmotionRenderer(
            ILogger<MauiGifEmotionRenderer> logger,
            MauiResourceService resourceService)
        {
            _logger = logger;
            _resourceService = resourceService;
        }

        public async Task<bool> CanRenderAsync(EmotionRenderRequest request)
        {
            if (string.IsNullOrEmpty(request.AssetPath))
                return false;

            // 检查是否是GIF文件（通过扩展名或表情名称）
            if (request.AssetPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                // 如果是完整路径，检查文件是否存在
                if (Path.IsPathRooted(request.AssetPath))
                {
                    return File.Exists(request.AssetPath);
                }
                // 如果是相对路径，检查是否在Resources中
                return await CheckMauiResourceExists(request.AssetPath);
            }

            // 检查是否是已知的表情名称
            if (IsKnownEmotion(request.AssetPath))
            {
                var gifFileName = $"{request.AssetPath.ToLower()}.gif";
                return await CheckMauiResourceExists($"Emotions/{gifFileName}") || 
                       await CheckMauiResourceExists(gifFileName);
            }

            return false;
        }

        public async Task RenderAsync(EmotionRenderRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                _currentCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                _logger.LogDebug("Starting MAUI GIF emotion render: {EmotionType} -> {AssetPath}", 
                    request.EmotionType, request.AssetPath);

                string gifPath = await ResolveGifPath(request.AssetPath, request.EmotionType);
                
                if (string.IsNullOrEmpty(gifPath))
                {
                    throw new FileNotFoundException($"GIF file not found for: {request.AssetPath}");
                }

                // 通过事件通知UI更新 - 使用标准的MAUI路径格式
                OnGifRenderRequested(new MauiGifRenderEventArgs
                {
                    EmotionType = request.EmotionType,
                    GifPath = gifPath,
                    Loops = request.Loops,
                    Duration = request.Duration
                });

                // 等待播放完成
                var playbackDuration = request.Duration;
                if (request.Loops > 1)
                {
                    playbackDuration = TimeSpan.FromMilliseconds(playbackDuration.TotalMilliseconds * request.Loops);
                }

                await Task.Delay(playbackDuration, _currentCancellationTokenSource.Token);

                // 播放完成
                RenderCompleted?.Invoke(this, new EmotionRenderEventArgs(request.EmotionType, RendererType, true));
                
                _logger.LogDebug("MAUI GIF emotion render completed: {EmotionType}", request.EmotionType);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("MAUI GIF emotion render cancelled: {EmotionType}", request.EmotionType);
                // 取消不算错误
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MAUI GIF emotion render failed: {EmotionType}", request.EmotionType);
                RenderCompleted?.Invoke(this, new EmotionRenderEventArgs(request.EmotionType, RendererType, false, ex.Message));
            }
        }

        public async Task StopAsync()
        {
            _currentCancellationTokenSource?.Cancel();
            _currentCancellationTokenSource?.Dispose();
            _currentCancellationTokenSource = null;

            // 通知UI停止显示
            OnGifRenderStopped();

            await Task.CompletedTask;
        }

        private async Task<string> ResolveGifPath(string assetPath, string emotionType)
        {
            // 如果已经是有效的资源路径（不包含文件系统分隔符），直接返回
            if (!string.IsNullOrEmpty(assetPath) && !assetPath.Contains(Path.DirectorySeparatorChar) && !assetPath.Contains(Path.AltDirectorySeparatorChar))
            {
                if (assetPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    return assetPath;
                }
            }

            // 尝试构建标准的表情GIF路径
            string emotionName = !string.IsNullOrEmpty(emotionType) ? emotionType.ToLower() : 
                                 Path.GetFileNameWithoutExtension(assetPath)?.ToLower() ?? "neutral";

            // MAUI中的GIF资源路径格式
            var possiblePaths = new[]
            {
                $"{emotionName}.gif",  // 直接使用文件名
                $"Emotions/{emotionName}.gif",  // 在Emotions文件夹中
                $"emotions/{emotionName}.gif",  // 小写文件夹名
                assetPath  // 原始路径
            };

            foreach (var path in possiblePaths)
            {
                if (await CheckMauiResourceExists(path))
                {
                    _logger.LogDebug("Resolved GIF path: {Path}", path);
                    return path;
                }
            }

            _logger.LogWarning("Could not resolve GIF path for: {AssetPath}, EmotionType: {EmotionType}", assetPath, emotionType);
            return string.Empty;
        }

        private async Task<bool> CheckMauiResourceExists(string resourcePath)
        {
            try
            {
                // 在MAUI中，检查应用包资源是否存在
                using var stream = await Microsoft.Maui.Storage.FileSystem.Current.OpenAppPackageFileAsync(resourcePath);
                return stream != null;
            }
            catch
            {
                return false;
            }
        }

        private bool IsKnownEmotion(string emotion)
        {
            // 检查是否为已知的表情名称
            var knownEmotions = new[] { 
                "happy", "sad", "angry", "neutral", "thinking", "loving", "laughing", 
                "cool", "confused", "confident", "crying", "delicious", "embarrassed", 
                "funny", "kissy", "relaxed", "shocked", "silly", "sleepy", "winking",
                "surprised", "listening", "speaking"
            };
            
            return Array.Exists(knownEmotions, e => e.Equals(emotion, StringComparison.OrdinalIgnoreCase));
        }

        private void OnGifRenderRequested(MauiGifRenderEventArgs args)
        {
            GifRenderRequested?.Invoke(this, args);
        }

        private void OnGifRenderStopped()
        {
            GifRenderStopped?.Invoke(this, EventArgs.Empty);
        }

        // UI事件
        public static event EventHandler<MauiGifRenderEventArgs>? GifRenderRequested;
        public static event EventHandler? GifRenderStopped;
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
