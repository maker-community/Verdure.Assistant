using Android.Media;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.MAUI.Platforms.Android.Services;

/// <summary>
/// Android平台的音乐播放器实现
/// 使用Android MediaPlayer
/// </summary>
public class AndroidMusicAudioPlayer : IMusicAudioPlayer
{
    private readonly ILogger<AndroidMusicAudioPlayer> _logger;
    private MediaPlayer? _mediaPlayer;
    private bool _disposed;

    public event EventHandler<MusicPlayerStateChangedEventArgs>? StateChanged;
    public event EventHandler<MusicPlayerProgressEventArgs>? ProgressUpdated;

    public TimeSpan CurrentPosition => _mediaPlayer != null 
        ? TimeSpan.FromMilliseconds(_mediaPlayer.CurrentPosition) 
        : TimeSpan.Zero;

    public TimeSpan Duration => _mediaPlayer != null 
        ? TimeSpan.FromMilliseconds(_mediaPlayer.Duration) 
        : TimeSpan.Zero;

    private double _currentVolume = 50.0;

    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;
    public bool IsPaused => _mediaPlayer != null && !_mediaPlayer.IsPlaying;

    public double Volume
    {
        get => _currentVolume;
        set
        {
            _currentVolume = Math.Max(0, Math.Min(100, value));
            if (_mediaPlayer != null)
            {
                var volume = (float)(_currentVolume / 100.0);
                _mediaPlayer.SetVolume(volume, volume);
            }
        }
    }

    public AndroidMusicAudioPlayer(ILogger<AndroidMusicAudioPlayer> logger)
    {
        _logger = logger;
        InitializeMediaPlayer();
    }

    private void InitializeMediaPlayer()
    {
        _mediaPlayer = new MediaPlayer();
        _mediaPlayer.Completion += OnCompletion;
        _mediaPlayer.Error += OnError;
        _mediaPlayer.Prepared += OnPrepared;
        
        _logger.LogInformation("Android音乐播放器初始化完成");
    }

    public async Task LoadAsync(string filePath)
    {
        try
        {
            if (_mediaPlayer == null) return;

            _mediaPlayer.Reset();
            _mediaPlayer.SetDataSource(filePath);
            _mediaPlayer.PrepareAsync();
            
            OnStateChanged(MusicPlayerState.Loading);
            await Task.CompletedTask;
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
            if (_mediaPlayer == null) return;

            _mediaPlayer.Reset();
            _mediaPlayer.SetDataSource(url);
            _mediaPlayer.PrepareAsync();
            
            OnStateChanged(MusicPlayerState.Loading);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载网络音频失败: {Url}", url);
            OnStateChanged(MusicPlayerState.Error, ex.Message);
            throw;
        }
    }

    public async Task PlayAsync()
    {
        try
        {
            if (_mediaPlayer == null) return;

            _mediaPlayer.Start();
            OnStateChanged(MusicPlayerState.Playing);
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
            if (_mediaPlayer == null) return;

            _mediaPlayer.Pause();
            OnStateChanged(MusicPlayerState.Paused);
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
            if (_mediaPlayer == null) return;

            _mediaPlayer.Stop();
            _mediaPlayer.SeekTo(0);
            OnStateChanged(MusicPlayerState.Stopped);
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
            if (_mediaPlayer == null) return;

            _mediaPlayer.SeekTo((int)position.TotalMilliseconds);
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

    private void OnPrepared(object? sender, EventArgs e)
    {
        OnStateChanged(MusicPlayerState.Idle);
    }

    private void OnCompletion(object? sender, EventArgs e)
    {
        OnStateChanged(MusicPlayerState.Ended);
    }

    private void OnError(object? sender, MediaPlayer.ErrorEventArgs e)
    {
        _logger.LogError("MediaPlayer错误: {What}, {Extra}", e.What, e.Extra);
        OnStateChanged(MusicPlayerState.Error, $"播放错误: {e.What}");
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

        _mediaPlayer?.Release();
        _mediaPlayer?.Dispose();
        _mediaPlayer = null;
        
        _disposed = true;
    }
}
