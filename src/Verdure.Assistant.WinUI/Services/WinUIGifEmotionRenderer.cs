using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.WinUI.Services
{
    /// <summary>
    /// WinUI GIF 表情渲染器
    /// </summary>
    public class WinUIGifEmotionRenderer : IEmotionRenderer
    {
        private readonly ILogger<WinUIGifEmotionRenderer> _logger;
        private CancellationTokenSource? _currentCancellationTokenSource;

        public string RendererType => "gif";
        public int Priority => 100; // GIF优先级最高

        public event EventHandler<EmotionRenderEventArgs>? RenderCompleted;

        public WinUIGifEmotionRenderer(ILogger<WinUIGifEmotionRenderer> logger)
        {
            _logger = logger;
        }

        public async Task<bool> CanRenderAsync(EmotionRenderRequest request)
        {
            if (string.IsNullOrEmpty(request.AssetPath))
                return false;

            if (!request.AssetPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                return false;

            return await Task.FromResult(File.Exists(request.AssetPath));
        }

        public async Task RenderAsync(EmotionRenderRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                _currentCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                _logger.LogDebug("Starting GIF emotion render: {EmotionType} -> {AssetPath}", 
                    request.EmotionType, request.AssetPath);

                if (string.IsNullOrEmpty(request.AssetPath) || !File.Exists(request.AssetPath))
                {
                    throw new FileNotFoundException($"GIF file not found: {request.AssetPath}");
                }

                // 在UI线程上创建BitmapImage
                var bitmapImage = await CreateBitmapImageAsync(request.AssetPath);

                // 通过事件通知UI更新
                OnGifRenderRequested(new GifRenderEventArgs
                {
                    EmotionType = request.EmotionType,
                    GifSource = bitmapImage,
                    AssetPath = request.AssetPath,
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
                
                _logger.LogDebug("GIF emotion render completed: {EmotionType}", request.EmotionType);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("GIF emotion render cancelled: {EmotionType}", request.EmotionType);
                // 取消不算错误
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GIF emotion render failed: {EmotionType}", request.EmotionType);
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

        private async Task<BitmapImage> CreateBitmapImageAsync(string imagePath)
        {
            var tcs = new TaskCompletionSource<BitmapImage>();

            // 在UI线程上创建BitmapImage
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var bitmapImage = new BitmapImage();
                    bitmapImage.UriSource = new Uri(imagePath);
                    tcs.SetResult(bitmapImage);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return await tcs.Task;
        }

        private void OnGifRenderRequested(GifRenderEventArgs args)
        {
            GifRenderRequested?.Invoke(this, args);
        }

        private void OnGifRenderStopped()
        {
            GifRenderStopped?.Invoke(this, EventArgs.Empty);
        }

        // UI事件
        public static event EventHandler<GifRenderEventArgs>? GifRenderRequested;
        public static event EventHandler? GifRenderStopped;
    }

    /// <summary>
    /// GIF渲染事件参数
    /// </summary>
    public class GifRenderEventArgs : EventArgs
    {
        public string EmotionType { get; set; } = string.Empty;
        public BitmapImage? GifSource { get; set; }
        public string? AssetPath { get; set; }
        public int Loops { get; set; } = 1;
        public TimeSpan Duration { get; set; }
    }
}
