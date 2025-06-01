using NAudio.Wave;
using XiaoZhi.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace XiaoZhi.WinUI.Services;

/// <summary>
/// NAudio实现的音频播放器
/// </summary>
public class NAudioPlayer : IAudioPlayer, IDisposable
{
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _bufferedProvider;
    private bool _isPlaying;
    private readonly object _lock = new();
    private readonly ILogger<NAudioPlayer>? _logger;
    private int _sampleRate;
    private int _channels;
    private readonly Timer _playbackTimer;
    private DateTime _lastDataTime = DateTime.Now;
    private readonly ConcurrentQueue<byte[]> _audioQueue = new();
    private bool _isInitialized;

    public event EventHandler? PlaybackStopped;

    public bool IsPlaying => _isPlaying;

    public NAudioPlayer(ILogger<NAudioPlayer>? logger = null)
    {
        _logger = logger;
        // 创建定时器来检测播放完成
        _playbackTimer = new Timer(CheckPlaybackCompletion, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task InitializeAsync(int sampleRate, int channels)
    {
        try
        {
            _sampleRate = sampleRate;
            _channels = channels;

            // 创建波形格式
            var waveFormat = new WaveFormat(sampleRate, 16, channels); // 16位深度

            // 创建缓冲波形提供器
            _bufferedProvider = new BufferedWaveProvider(waveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(10), // 10秒缓冲区
                DiscardOnBufferOverflow = false
            };

            // 创建音频输出设备
            _waveOut = new WaveOutEvent
            {
                DesiredLatency = 60 // 60ms延迟，匹配Python配置
            };

            _waveOut.Init(_bufferedProvider);
            _waveOut.PlaybackStopped += OnPlaybackStopped;

            _isInitialized = true;
            _logger?.LogInformation("音频播放器初始化成功: {SampleRate}Hz, {Channels}声道", sampleRate, channels);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "初始化音频播放器失败");
            throw new Exception($"初始化音频播放器失败: {ex.Message}", ex);
        }
    }    public async Task PlayAsync(byte[] audioData, int sampleRate = 16000, int channels = 1)
    {
        try
        {
            // 如果参数不匹配，重新初始化
            if (!_isInitialized || _sampleRate != sampleRate || _channels != channels)
            {
                await StopAsync();
                await InitializeAsync(sampleRate, channels);
            }

            if (_bufferedProvider == null || _waveOut == null)
            {
                throw new InvalidOperationException("音频播放器未正确初始化");
            }

            lock (_lock)
            {
                // 清理可能存在的旧音频数据以避免杂音
                if (_bufferedProvider.BufferedDuration.TotalMilliseconds > 2000) // 如果缓冲区超过2秒
                {
                    _bufferedProvider.ClearBuffer();
                    _logger?.LogDebug("清理音频缓冲区以避免杂音");
                }
                
                // 将音频数据添加到缓冲区
                _bufferedProvider.AddSamples(audioData, 0, audioData.Length);
                _lastDataTime = DateTime.Now;
            }

            // 如果还没有开始播放，开始播放
            if (!_isPlaying && _waveOut.PlaybackState != PlaybackState.Playing)
            {
                _waveOut.Play();
                _isPlaying = true;
                
                // 启动定时器检测播放完成
                _playbackTimer.Change(200, 200); // 每200ms检查一次，更频繁的检查
                
                _logger?.LogDebug("开始播放音频，数据长度: {Length}", audioData.Length);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "播放音频时出错");
            Console.WriteLine($"播放音频时出错: {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        if (!_isPlaying) return;

        try
        {
            // 停止定时器
            _playbackTimer.Change(Timeout.Infinite, Timeout.Infinite);
            
            _waveOut?.Stop();
            _bufferedProvider?.ClearBuffer();

            _isPlaying = false;
            _logger?.LogInformation("音频播放已停止");

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止音频播放时出错");
            Console.WriteLine($"停止音频播放时出错: {ex.Message}");
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        try
        {
            _isPlaying = false;
            _playbackTimer.Change(Timeout.Infinite, Timeout.Infinite);
            
            PlaybackStopped?.Invoke(this, EventArgs.Empty);

            if (e.Exception != null)
            {
                _logger?.LogError(e.Exception, "播放过程中发生错误");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理播放停止事件时出错");
        }
    }

    private void CheckPlaybackCompletion(object? state)
    {
        try
        {
            if (_isPlaying && _bufferedProvider != null)
            {
                // 检查缓冲区是否为空
                var bufferedDuration = _bufferedProvider.BufferedDuration;
                var timeSinceLastData = (DateTime.Now - _lastDataTime).TotalMilliseconds;
                
                // 如果缓冲区基本为空且距离最后接收数据超过1.5秒，认为播放完成
                var shouldStop = bufferedDuration.TotalMilliseconds < 100 && timeSinceLastData > 1500;
                
                if (shouldStop)
                {
                    _logger?.LogDebug("播放完成检测 - 缓冲区时长: {BufferedDuration}ms, 距离最后数据: {TimeSinceLastData}ms", 
                        bufferedDuration.TotalMilliseconds, timeSinceLastData);
                    
                    // 停止定时器以防止多次触发
                    _playbackTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    
                    Task.Run(async () => 
                    {
                        try
                        {
                            await StopAsync();
                            PlaybackStopped?.Invoke(this, EventArgs.Empty);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "播放完成处理器中发生错误");
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "检查播放完成状态时出错");
        }
    }    
    public void Dispose()
    {
        try
        {
            _playbackTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _playbackTimer?.Dispose();
            
            // 确保停止播放并等待完成
            try
            {
                StopAsync().Wait(1000); // 最多等待1秒
            }
            catch (TimeoutException)
            {
                _logger?.LogWarning("停止音频播放超时");
            }
            
            lock (_lock)
            {
                if (_waveOut != null)
                {
                    try
                    {
                        _waveOut.PlaybackStopped -= OnPlaybackStopped;
                        
                        // 强制停止并释放
                        if (_waveOut.PlaybackState == PlaybackState.Playing)
                        {
                            _waveOut.Stop();
                        }
                        
                        _waveOut.Dispose();
                        _waveOut = null;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "释放WaveOut时出错");
                    }
                }
                
                if (_bufferedProvider != null)
                {
                    try
                    {
                        _bufferedProvider.ClearBuffer();
                        _bufferedProvider = null;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "清理缓冲区时出错");
                    }
                }
                
                // 清理音频队列
                while (_audioQueue.TryDequeue(out _))
                {
                    // 清空队列
                }
            }
            
            _isInitialized = false;
            _isPlaying = false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "释放NAudioPlayer资源时出错");
        }
    }
}
