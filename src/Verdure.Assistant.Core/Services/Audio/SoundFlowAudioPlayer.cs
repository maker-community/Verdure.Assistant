using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;
using Verdure.Assistant.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// SoundFlow实现的音频播放器
/// 基于SoundFlowPlaybackTest的实现，使用SoundPlayer + RawDataProvider进行字节数据播放
/// 提供与PortAudioPlayer相同的接口和功能
/// </summary>
public class SoundFlowAudioPlayer : IAudioPlayer, IDisposable
{
    private readonly ILogger<SoundFlowAudioPlayer>? _logger;
    private AudioEngine? _engine;
    private AudioPlaybackDevice? _playbackDevice;
    private SoundPlayer? _soundPlayer;
    private RawDataProvider? _dataProvider;
    private readonly Queue<byte[]> _audioQueue = new();
    private readonly object _lock = new();
    private bool _isPlaying = false;
    private bool _isDisposed = false;
    private int _sampleRate = 16000;
    private int _channels = 1;
    private const int MaxQueueSize = 20;
    private Task? _playbackTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private EventHandler<EventArgs>? _onPlaybackEnded;

    // 设备配置 - 优化为低延迟播放
    private static readonly MiniAudioDeviceConfig DeviceConfig = new()
    {
        PeriodSizeInFrames = 960,   // 60ms @ 16kHz = 960 samples
        PeriodSizeInMilliseconds = 0,
        Periods = 3,
        NoPreSilencedOutputBuffer = false,
        NoClip = false,
        NoDisableDenormals = false,
        NoFixedSizedCallback = false,
        Playback = new DeviceSubConfig 
        { 
            ShareMode = ShareMode.Shared 
        },
        Wasapi = new WasapiSettings 
        { 
            Usage = WasapiUsage.ProAudio,
            NoAutoConvertSRC = false,     // 允许自动采样率转换
            NoDefaultQualitySRC = false,  // 允许高质量重采样
            NoAutoStreamRouting = false,
            NoHardwareOffloading = false
        }
    };

    public event EventHandler? PlaybackStopped;
    public bool IsPlaying => _isPlaying;

    public SoundFlowAudioPlayer(ILogger<SoundFlowAudioPlayer>? logger = null)
    {
        _logger = logger;
        InitializeAudioEngine();
        // 以默认参数预初始化播放设备与播放器，便于后续快速切换/播放
        try
        {
            InitializePlaybackDevice(_sampleRate, _channels);
            InitializePlayer(_sampleRate, _channels);
        }
        catch (Exception ex)
        {
            // 预初始化失败不致命，延迟到首次播放再初始化
            _logger?.LogWarning(ex, "SoundFlow预初始化失败，将在首次播放时重试");
        }
    }

    /// <summary>
    /// 在构造函数中初始化音频引擎和基础组件
    /// </summary>
    private void InitializeAudioEngine()
    {
        try
        {
            // 在构造时就初始化引擎
            _engine = new MiniAudioEngine();
            
            // 显示可用的播放设备（调试模式）
            if (_logger != null && _logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("SoundFlow播放引擎初始化完成");
                _logger.LogDebug("可用SoundFlow播放设备:");
                for (int i = 0; i < _engine.PlaybackDevices.Length; i++)
                {
                    var device = _engine.PlaybackDevices[i];
                    var status = device.IsDefault ? " (默认)" : "";
                    _logger.LogDebug("  [{Index}] {Name}{Status}", i, device.Name, status);
                }
            }

            if (_engine.PlaybackDevices.Length == 0)
            {
                throw new InvalidOperationException("未找到SoundFlow音频播放设备");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "初始化SoundFlow播放引擎失败");
            throw;
        }
    }

    /// <summary>
    /// 初始化播放设备（仅在参数变化时调用）
    /// </summary>
    private void InitializePlaybackDevice(int sampleRate, int channels)
    {
        if (!ValidateAudioParameters(sampleRate, channels))
        {
            throw new ArgumentException("Invalid audio parameters");
        }

        // 如果参数相同且设备已初始化，直接返回
        if (_playbackDevice != null && _sampleRate == sampleRate && _channels == channels)
        {
            return;
        }

        // 清理现有设备
        if (_playbackDevice != null)
        {
            try
            {
                _playbackDevice.Stop();
                _playbackDevice = null;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "清理旧播放设备时出错");
            }
        }

    _sampleRate = sampleRate;
    _channels = channels;

        try
        {
            // 引擎已在构造函数中初始化
            if (_engine == null)
            {
                throw new InvalidOperationException("SoundFlow引擎未正确初始化");
            }

            var format = new AudioFormat
            {
                SampleRate = sampleRate,
                Channels = channels,
                Format = SampleFormat.S16
            };

            _playbackDevice = _engine.InitializePlaybackDevice(null, format, DeviceConfig);

            _logger?.LogDebug("已选择SoundFlow播放设备: {DeviceName}", _playbackDevice.Info?.Name ?? "默认设备");
            _logger?.LogDebug("播放设备格式: {Format}, {Channels}ch, {SampleRate}Hz", 
                _playbackDevice.Format.Format, _playbackDevice.Format.Channels, _playbackDevice.Format.SampleRate);

            _logger?.LogInformation("SoundFlow音频播放器设备初始化成功: {SampleRate}Hz, {Channels}声道", 
                sampleRate, channels);
        }        
        catch (Exception ex)
        {
            throw new Exception($"初始化SoundFlow音频播放设备失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 初始化SoundPlayer与RawDataProvider，并注册PlaybackEnded（仅在创建/重建时调用）
    /// </summary>
    private void InitializePlayer(int sampleRate, int channels)
    {
        if (_engine == null || _playbackDevice == null)
        {
            throw new InvalidOperationException("SoundFlow引擎或播放设备未初始化");
        }

        // 先释放旧实例
        try
        {
            if (_soundPlayer != null)
            {
                if (_onPlaybackEnded != null)
                {
                    _soundPlayer.PlaybackEnded -= _onPlaybackEnded;
                }
                try
                {
                    _playbackDevice.MasterMixer.RemoveComponent(_soundPlayer);
                }
                catch { /* ignore */ }
                _soundPlayer.Dispose();
            }
            _dataProvider?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "清理旧的SoundPlayer/RawDataProvider时出错");
        }

        // 创建最小静音缓冲区（避免未及时收到首包时的点击声）
        var initialBuffer = new byte[Math.Max(1, 960 * channels * 2)]; // ~60ms @16k,16bit
        _dataProvider = new RawDataProvider(initialBuffer, SampleFormat.S16, sampleRate, channels);

        var format = new AudioFormat
        {
            SampleRate = sampleRate,
            Channels = channels,
            Format = SampleFormat.S16
        };

        _soundPlayer = new SoundPlayer(_engine, format, _dataProvider);

        // 在构造时就定义一个固定的PlaybackEnded处理器，并在每次重建播放器后挂载
        _onPlaybackEnded ??= (sender, args) =>
        {
            _logger?.LogDebug("SoundFlow PlaybackEnded 触发");
            // 不直接改变_isPlaying，由馈送循环统一控制停止；仅透传事件
            try { PlaybackStopped?.Invoke(this, EventArgs.Empty); } catch { /* ignore */ }
        };
        _soundPlayer.PlaybackEnded += _onPlaybackEnded;

        // 将播放器加入混音器，待StartPlayback时启动
        _playbackDevice.MasterMixer.AddComponent(_soundPlayer);
        _logger?.LogDebug("SoundFlow播放器初始化完成: {SampleRate}Hz, {Channels}ch", sampleRate, channels);
    }

    public async Task PlayAsync(byte[] audioData, int sampleRate = 16000, int channels = 1)
    {
        if (_isDisposed) return;
        
        try
        {
            // 如果参数不匹配或设备未就绪，重新初始化设备与播放器
            if (_sampleRate != sampleRate || _channels != channels || _playbackDevice == null || _soundPlayer == null)
            {
                await StopAsync();
            }

            lock (_lock)
            {
                // 防止音频队列过大导致延迟和内存问题
                if (_audioQueue.Count >= MaxQueueSize)
                {
                    _logger?.LogWarning("SoundFlow音频队列过大，清理旧数据以防止杂音");
                    while (_audioQueue.Count > MaxQueueSize / 2)
                    {
                        _audioQueue.Dequeue();
                    }
                }
                
                _audioQueue.Enqueue(audioData);
            }

            // 如果还没开始播放，启动播放
            if (!_isPlaying && _playbackDevice != null)
            {
                await StartPlayback();
                _logger?.LogDebug("开始SoundFlow播放音频，队列长度: {QueueCount}", _audioQueue.Count);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SoundFlow播放音频时出错");
        }
    }

    private async Task StartPlayback()
    {
        if (_isPlaying || _playbackDevice == null || _soundPlayer == null) return;

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _isPlaying = true;
            
            // 启动播放设备
            _playbackDevice.Start();
            _logger?.LogDebug("🔊 SoundFlow播放器启动");
            
            // 启动音频馈送任务：按片段更新provider并驱动播放
            _playbackTask = Task.Run(async () =>
            {
                await FeedAudioLoop(_cancellationTokenSource.Token);
            });

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "启动SoundFlow播放时出错");
            _isPlaying = false;
        }
    }

    /// <summary>
    /// 音频馈送主循环：每次从队列取出一段PCM数据，替换RawDataProvider并触发播放
    /// </summary>
    private async Task FeedAudioLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _isPlaying)
            {
                byte[]? audioData = null;
                lock (_lock)
                {
                    if (_audioQueue.Count > 0)
                    {
                        audioData = _audioQueue.Dequeue();
                    }
                }

                if (audioData != null && audioData.Length > 0)
                {
                    await UpdateAudioDataAsync(audioData);

                    // 估算该片段的播放时长：frames = bytes / (bytesPerSample * channels)
                    var frames = audioData.Length / (2 * Math.Max(1, _channels)); // 16-bit = 2 bytes
                    var durationMs = (int)(frames / (double)_sampleRate * 1000.0);
                    if (durationMs > 0)
                    {
                        try { await Task.Delay(durationMs, cancellationToken); } catch { /* ignore */ }
                    }
                }
                else
                {
                    // 空闲等待，最多~500ms无数据则退出
                    var idle = 0;
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        bool hasData;
                        lock (_lock) { hasData = _audioQueue.Count > 0; }
                        if (hasData) break;

                        await Task.Delay(10, cancellationToken);
                        idle += 10;
                        if (idle >= 500)
                        {
                            _logger?.LogDebug("SoundFlow无新数据，自动停止播放循环");
                            _isPlaying = false;
                            break;
                        }
                    }
                }
            }

            _logger?.LogDebug("🔇 SoundFlow播放器任务结束");
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            _logger?.LogError(ex, "SoundFlow音频馈送循环出错");
        }
    }

    /// <summary>
    /// 用新的PCM字节数据替换RawDataProvider，并驱动SoundPlayer继续播放
    /// </summary>
    private async Task UpdateAudioDataAsync(byte[] audioData)
    {
        if (_engine == null || _playbackDevice == null)
        {
            _logger?.LogWarning("SoundFlow设备未就绪，忽略音频数据");
            return;
        }

        try
        {
            // 暂停并从混音器移除旧播放器
            if (_soundPlayer != null)
            {
                try { _soundPlayer.Pause(); } catch { /* ignore */ }
                try { _playbackDevice.MasterMixer.RemoveComponent(_soundPlayer); } catch { /* ignore */ }
            }

            // 释放旧数据源并创建新数据源
            _dataProvider?.Dispose();
            _dataProvider = new RawDataProvider(audioData, SampleFormat.S16, _sampleRate, _channels);

            // 重建播放器以使用新数据源
            var format = new AudioFormat
            {
                SampleRate = _sampleRate,
                Channels = _channels,
                Format = SampleFormat.S16
            };

            if (_soundPlayer != null)
            {
                if (_onPlaybackEnded != null)
                {
                    _soundPlayer.PlaybackEnded -= _onPlaybackEnded;
                }
                _soundPlayer.Dispose();
            }

            _soundPlayer = new SoundPlayer(_engine, format, _dataProvider);
            if (_onPlaybackEnded != null)
            {
                _soundPlayer.PlaybackEnded += _onPlaybackEnded;
            }

            _playbackDevice.MasterMixer.AddComponent(_soundPlayer);
            _soundPlayer.Play();

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "更新SoundFlow音频数据时出错");
        }
    }

    public async Task StopAsync()
    {
        try
        {
            if (_isPlaying && _cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                if (_playbackTask != null)
                {
                    try { await _playbackTask; } catch { /* ignore */ }
                }
            }

            _isPlaying = false;

            // 停止底层设备与播放器，但不在此处销毁引擎（留待Dispose），以便后续可快速重启
            try { _soundPlayer?.Stop(); } catch { /* ignore */ }
            if (_playbackDevice != null)
            {
                try { _playbackDevice.Stop(); } catch (Exception ex) { _logger?.LogWarning(ex, "停止SoundFlow播放设备时出现警告"); }
            }

            // 清理队列
            lock (_lock)
            {
                _audioQueue.Clear();
            }

            _logger?.LogInformation("SoundFlow音频播放已停止");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止SoundFlow音频播放时出错");
        }
    }

    /// <summary>
    /// 验证音频参数
    /// </summary>
    private bool ValidateAudioParameters(int sampleRate, int channels)
    {
        if (sampleRate <= 0 || sampleRate > 192000)
        {
            _logger?.LogError("无效的采样率: {SampleRate}", sampleRate);
            return false;
        }

        if (channels <= 0 || channels > 8)
        {
            _logger?.LogError("无效的声道数: {Channels}", channels);
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        _isDisposed = true;
        
        try
        {
            // 停止播放
            StopAsync().Wait(3000); // 3秒超时
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "释放SoundFlow音频播放器资源时出现警告");
            
            // 即使停止失败，也要尝试清理资源
            lock (_lock)
            {
                try
                {
                    // 尝试强制释放引擎
                    try { _soundPlayer?.Stop(); } catch { /* ignore */ }
                    try
                    {
                        if (_soundPlayer != null && _onPlaybackEnded != null)
                        {
                            _soundPlayer.PlaybackEnded -= _onPlaybackEnded;
                        }
                        if (_soundPlayer != null)
                        {
                            _playbackDevice?.MasterMixer.RemoveComponent(_soundPlayer);
                        }
                    }
                    catch { /* ignore */ }
                    _soundPlayer?.Dispose();
                    _dataProvider?.Dispose();
                    _playbackDevice?.Dispose();
                    _engine?.Dispose();
                }
                catch (Exception disposeEx)
                {
                    _logger?.LogWarning(disposeEx, "强制释放SoundFlow资源时出现警告");
                }
                finally
                {
                    _playbackDevice = null;
                    _soundPlayer = null;
                    _dataProvider = null;
                    _engine = null;
                    _isPlaying = false;
                }
            }
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
