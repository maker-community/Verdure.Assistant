using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Core.Services
{
    /// <summary>
    /// 表情播放协调器实现
    /// </summary>
    public class EmotionPlaybackCoordinator : IEmotionPlaybackCoordinator
    {
        private readonly IEnumerable<IEmotionRenderer> _renderers;
        private readonly IEmotionAssetResolver _assetResolver;
        private readonly ILogger<EmotionPlaybackCoordinator> _logger;
        private readonly object _lock = new object();

        private EmotionPlaybackStatus _currentStatus = new EmotionPlaybackStatus();
        private CancellationTokenSource? _currentCancellationTokenSource;

        public EmotionPlaybackCoordinator(
            IEnumerable<IEmotionRenderer> renderers,
            IEmotionAssetResolver assetResolver,
            ILogger<EmotionPlaybackCoordinator> logger)
        {
            _renderers = renderers.OrderByDescending(r => r.Priority);
            _assetResolver = assetResolver;
            _logger = logger;

            // 订阅渲染器完成事件
            foreach (var renderer in _renderers)
            {
                renderer.RenderCompleted += OnRendererCompleted;
            }
        }

        public async Task PlayEmotionAsync(string emotionType, EmotionPlaybackOptions? options = null)
        {
            options ??= new EmotionPlaybackOptions();

            try
            {
                // 停止当前播放
                await StopCurrentEmotionAsync();

                lock (_lock)
                {
                    _currentStatus = new EmotionPlaybackStatus
                    {
                        CurrentEmotion = emotionType,
                        IsPlaying = true,
                        StartTime = DateTime.Now
                    };
                    _currentCancellationTokenSource = new CancellationTokenSource();
                }

                _logger.LogDebug("Starting emotion playback: {EmotionType}", emotionType);

                // 解析可用资源
                var assets = await _assetResolver.ResolveAssetsAsync(emotionType);
                if (!assets.Any())
                {
                    _logger.LogWarning("No assets found for emotion: {EmotionType}", emotionType);
                    OnEmotionPlaybackError(emotionType, "No assets available");
                    return;
                }

                // 查找合适的渲染器
                IEmotionRenderer? selectedRenderer = null;
                EmotionAsset? selectedAsset = null;

                // 如果指定了首选渲染器类型，优先使用
                if (!string.IsNullOrEmpty(options.PreferredRendererType))
                {
                    selectedRenderer = _renderers.FirstOrDefault(r => 
                        r.RendererType.Equals(options.PreferredRendererType, StringComparison.OrdinalIgnoreCase));
                    
                    if (selectedRenderer != null)
                    {
                        selectedAsset = await _assetResolver.GetPreferredAssetAsync(emotionType, options.PreferredRendererType);
                    }
                }

                // 如果没有找到首选渲染器，按优先级查找
                if (selectedRenderer == null || selectedAsset == null)
                {
                    foreach (var asset in assets)
                    {
                        foreach (var renderer in _renderers)
                        {
                            var request = CreateRenderRequest(emotionType, asset, options);
                            if (await renderer.CanRenderAsync(request))
                            {
                                selectedRenderer = renderer;
                                selectedAsset = asset;
                                break;
                            }
                        }
                        if (selectedRenderer != null) break;
                    }
                }

                if (selectedRenderer == null || selectedAsset == null)
                {
                    _logger.LogWarning("No suitable renderer found for emotion: {EmotionType}", emotionType);
                    OnEmotionPlaybackError(emotionType, "No suitable renderer available");
                    return;
                }

                // 更新状态
                lock (_lock)
                {
                    _currentStatus.CurrentRenderer = selectedRenderer.RendererType;
                    _currentStatus.Duration = options.Duration ?? TimeSpan.FromSeconds(3);
                }

                // 创建渲染请求
                var renderRequest = CreateRenderRequest(emotionType, selectedAsset, options);

                // 开始渲染
                _logger.LogInformation("Playing emotion {EmotionType} using {RendererType} renderer with asset {AssetPath}", 
                    emotionType, selectedRenderer.RendererType, selectedAsset.Path);

                EmotionPlaybackStarted?.Invoke(this, new EmotionPlaybackEventArgs(emotionType, selectedRenderer.RendererType));

                await selectedRenderer.RenderAsync(renderRequest, _currentCancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Emotion playback cancelled: {EmotionType}", emotionType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play emotion: {EmotionType}", emotionType);
                OnEmotionPlaybackError(emotionType, ex.Message);
            }
        }

        public async Task StopCurrentEmotionAsync()
        {
            lock (_lock)
            {
                _currentCancellationTokenSource?.Cancel();
                _currentCancellationTokenSource?.Dispose();
                _currentCancellationTokenSource = null;
            }

            // 停止所有渲染器
            var stopTasks = _renderers.Select(r => r.StopAsync());
            await Task.WhenAll(stopTasks);

            lock (_lock)
            {
                var wasPlaying = _currentStatus.IsPlaying;
                var emotion = _currentStatus.CurrentEmotion;
                var renderer = _currentStatus.CurrentRenderer;

                _currentStatus = new EmotionPlaybackStatus
                {
                    IsPlaying = false
                };

                if (wasPlaying && !string.IsNullOrEmpty(emotion) && !string.IsNullOrEmpty(renderer))
                {
                    EmotionPlaybackCompleted?.Invoke(this, new EmotionPlaybackEventArgs(emotion, renderer));
                }
            }
        }

        public EmotionPlaybackStatus GetCurrentStatus()
        {
            lock (_lock)
            {
                return new EmotionPlaybackStatus
                {
                    IsPlaying = _currentStatus.IsPlaying,
                    CurrentEmotion = _currentStatus.CurrentEmotion,
                    CurrentRenderer = _currentStatus.CurrentRenderer,
                    StartTime = _currentStatus.StartTime,
                    Duration = _currentStatus.Duration
                };
            }
        }

        private EmotionRenderRequest CreateRenderRequest(string emotionType, EmotionAsset asset, EmotionPlaybackOptions options)
        {
            return new EmotionRenderRequest
            {
                EmotionType = emotionType,
                AssetPath = asset.Path,
                Loops = options.Loops,
                FPS = options.FPS,
                Duration = options.Duration ?? TimeSpan.FromSeconds(3),
                Properties = new Dictionary<string, object>(options.Properties)
            };
        }

        private void OnRendererCompleted(object? sender, EmotionRenderEventArgs e)
        {
            lock (_lock)
            {
                if (_currentStatus.IsPlaying && 
                    _currentStatus.CurrentEmotion == e.EmotionType &&
                    _currentStatus.CurrentRenderer == e.RendererType)
                {
                    _currentStatus.IsPlaying = false;
                    EmotionPlaybackCompleted?.Invoke(this, new EmotionPlaybackEventArgs(e.EmotionType, e.RendererType));
                }
            }

            if (!e.Success)
            {
                OnEmotionPlaybackError(e.EmotionType, e.ErrorMessage ?? "Renderer failed");
            }
        }

        private void OnEmotionPlaybackError(string emotionType, string errorMessage)
        {
            lock (_lock)
            {
                _currentStatus.IsPlaying = false;
            }

            EmotionPlaybackError?.Invoke(this, new EmotionPlaybackErrorEventArgs(emotionType, errorMessage));
        }

        public event EventHandler<EmotionPlaybackEventArgs>? EmotionPlaybackStarted;
        public event EventHandler<EmotionPlaybackEventArgs>? EmotionPlaybackCompleted;
        public event EventHandler<EmotionPlaybackErrorEventArgs>? EmotionPlaybackError;
    }
}
