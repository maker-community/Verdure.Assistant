using System;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.WinUI.Services
{
    /// <summary>
    /// WinUI平台的音乐播放器实现
    /// 使用Windows.Media.Playback.MediaPlayer
    /// </summary>
    public class WinUIMusicAudioPlayer : IMusicAudioPlayer
    {
        private readonly ILogger<WinUIMusicAudioPlayer> _logger;
        private readonly MediaPlayer _mediaPlayer;
        private bool _disposed;

        public event EventHandler<MusicPlayerStateChangedEventArgs>? StateChanged;
        public event EventHandler<MusicPlayerProgressEventArgs>? ProgressUpdated;

        public TimeSpan CurrentPosition => _mediaPlayer.Position;
        public TimeSpan Duration => _mediaPlayer.NaturalDuration;
        public bool IsPlaying => _mediaPlayer.CurrentState == MediaPlayerState.Playing;
        public bool IsPaused => _mediaPlayer.CurrentState == MediaPlayerState.Paused;

        public double Volume
        {
            get => _mediaPlayer.Volume * 100;
            set => _mediaPlayer.Volume = Math.Max(0, Math.Min(1, value / 100.0));
        }        
        
        public WinUIMusicAudioPlayer(ILogger<WinUIMusicAudioPlayer> logger)
        {
            _logger = logger;
            _mediaPlayer = new MediaPlayer();
            
            // 绑定事件
            _mediaPlayer.CurrentStateChanged += OnCurrentStateChanged;
            _mediaPlayer.MediaEnded += OnMediaEnded;
            _mediaPlayer.MediaFailed += OnMediaFailed;
            _mediaPlayer.PlaybackSession.PositionChanged += OnPositionChanged;

            _logger.LogInformation("WinUI音乐播放器初始化完成");
        }

        public async Task LoadAsync(string filePath)
        {
            try
            {
                _logger.LogInformation("加载音频文件: {FilePath}", filePath);
                
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                var mediaSource = MediaSource.CreateFromStorageFile(file);
                
                _mediaPlayer.Source = mediaSource;
                
                OnStateChanged(MusicPlayerState.Loaded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载音频文件失败: {FilePath}", filePath);
                OnStateChanged(MusicPlayerState.Error, ex.Message);
                throw;
            }
        }

        public async Task LoadFromUrlAsync(string url)
        {
            try
            {
                _logger.LogInformation("加载音频流: {Url}", url);
                
                var mediaSource = MediaSource.CreateFromUri(new Uri(url));
                _mediaPlayer.Source = mediaSource;
                
                OnStateChanged(MusicPlayerState.Loaded);
                
                await Task.CompletedTask; // 保持异步接口一致性
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载音频流失败: {Url}", url);
                OnStateChanged(MusicPlayerState.Error, ex.Message);
                throw;
            }
        }

        public async Task PlayAsync()
        {
            try
            {
                _mediaPlayer.Play();
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "播放失败");
                OnStateChanged(MusicPlayerState.Error, ex.Message);
                throw;
            }
        }

        public async Task PauseAsync()
        {
            try
            {
                _mediaPlayer.Pause();
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "暂停失败");
                OnStateChanged(MusicPlayerState.Error, ex.Message);
                throw;
            }
        }

        public async Task StopAsync()
        {
            try
            {
                _mediaPlayer.Pause();
                _mediaPlayer.Position = TimeSpan.Zero;
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止失败");
                OnStateChanged(MusicPlayerState.Error, ex.Message);
                throw;
            }
        }

        public async Task SeekAsync(TimeSpan position)
        {
            try
            {
                _mediaPlayer.Position = position;
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "跳转失败");
                OnStateChanged(MusicPlayerState.Error, ex.Message);
                throw;
            }
        }

        #region 事件处理

        private void OnCurrentStateChanged(MediaPlayer sender, object args)
        {
            var state = sender.CurrentState switch
            {
                MediaPlayerState.Closed => MusicPlayerState.Idle,
                MediaPlayerState.Opening => MusicPlayerState.Loading,
                MediaPlayerState.Buffering => MusicPlayerState.Loading,
                MediaPlayerState.Playing => MusicPlayerState.Playing,
                MediaPlayerState.Paused => MusicPlayerState.Paused,
                MediaPlayerState.Stopped => MusicPlayerState.Stopped,
                _ => MusicPlayerState.Idle
            };

            OnStateChanged(state);
        }

        private void OnMediaEnded(MediaPlayer sender, object args)
        {
            OnStateChanged(MusicPlayerState.Ended);
        }

        private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            var errorMessage = $"播放失败: {args.Error} - {args.ErrorMessage}";
            _logger.LogError("媒体播放失败: {Error}", errorMessage);
            OnStateChanged(MusicPlayerState.Error, errorMessage);
        }        
        
        private void OnPositionChanged(MediaPlaybackSession sender, object args)
        {
            // Only fire progress events when media is properly loaded and has valid duration
            if (sender.NaturalDuration > TimeSpan.Zero)
            {
                var progressArgs = new MusicPlayerProgressEventArgs(sender.Position, sender.NaturalDuration);
                ProgressUpdated?.Invoke(this, progressArgs);
            }
        }

        private void OnStateChanged(MusicPlayerState state, string? errorMessage = null)
        {
            var args = new MusicPlayerStateChangedEventArgs(state, errorMessage);
            StateChanged?.Invoke(this, args);
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;

            _mediaPlayer?.Dispose();
            _disposed = true;
        }
    }
}
