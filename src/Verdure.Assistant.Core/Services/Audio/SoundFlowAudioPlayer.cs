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
/// 优化了播放逻辑，参考PortAudioPlayer的连续数据流方式
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
    private readonly Timer _playbackTimer;
    private DateTime _lastDataTime = DateTime.Now;
    private EventHandler<EventArgs>? _onPlaybackEnded;
    private Task? _feedTask;
    private CancellationTokenSource? _cancellationTokenSource;

    // 设备配置 - 优化为更低延迟播放，减少断断续续
    private static readonly MiniAudioDeviceConfig DeviceConfig = new()
    {
        PeriodSizeInFrames = 480,   // 30ms @ 16kHz = 480 samples (减少到30ms提高响应性)
        PeriodSizeInMilliseconds = 0,
        Periods = 4,                // 增加到4个周期，提供更好的缓冲
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
            Usage = WasapiUsage.ProAudio,    // 专业音频模式，降低延迟
            NoAutoConvertSRC = false,        // 允许自动采样率转换
            NoDefaultQualitySRC = false,     // 允许高质量重采样
            NoAutoStreamRouting = false,
            NoHardwareOffloading = false
        }
    };

    public event EventHandler? PlaybackStopped;
    public bool IsPlaying => _isPlaying;

    public SoundFlowAudioPlayer(ILogger<SoundFlowAudioPlayer>? logger = null)
    {
        _logger = logger;
        
        // 创建定时器来检测播放完成，类似PortAudioPlayer
        _playbackTimer = new Timer(CheckPlaybackCompletion, null, Timeout.Infinite, Timeout.Infinite);
        
        InitializeAudioEngine();
        // 以默认参数预初始化播放设备与播放器，便于后续快速切换/播放
        try
        {
            InitializePlaybackDevice(_sampleRate, _channels);
        }
        catch (Exception ex)
        {
            // 预初始化失败不致命，延迟到首次播放再初始化
            _logger?.LogWarning(ex, "SoundFlow预初始化失败，将在首次播放时重试");
        }
    }

    /// <summary>
    /// 检查播放完成条件（类似PortAudioPlayer的定时器逻辑）
    /// </summary>
    private void CheckPlaybackCompletion(object? state)
    {
        try
        {
            lock (_lock)
            {
                // 检查播放完成条件
                if (_isPlaying && _audioQueue.Count == 0)
                {
                    var timeSinceLastData = (DateTime.Now - _lastDataTime).TotalMilliseconds;
                    if (timeSinceLastData > 1500) // 类似PortAudioPlayer的1500ms超时
                    {
                        _logger?.LogDebug("SoundFlow播放完成检测 - 无数据 {TimeSinceLastData}ms", timeSinceLastData);
                        
                        // 停止定时器防止多次触发
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
                                _logger?.LogError(ex, "播放完成处理时出错");
                            }
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "检查播放状态时出错");
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
    /// 初始化SoundPlayer与RawDataProvider（仅在参数变化时调用）
    /// </summary>
    private async Task InitializePlayer(int sampleRate, int channels)
    {
        if (_engine == null || _playbackDevice == null)
        {
            throw new InvalidOperationException("SoundFlow引擎或播放设备未初始化");
        }

        _sampleRate = sampleRate;
        _channels = channels;

        try
        {
            // 创建音频格式 - 匹配测试项目的要求
            var format = new AudioFormat
            {
                SampleRate = sampleRate,
                Channels = channels,
                Format = SampleFormat.S16 // 使用16位整数格式
            };
            
            // 创建初始的空音频缓冲区（避免未及时收到首包时的点击声）
            var initialBuffer = new byte[960 * channels * 2]; // 60ms @ 16kHz = 960 samples * 2 bytes
            
            // 清理旧播放器
            if (_soundPlayer != null)
            {
                try
                {
                    _soundPlayer.Stop();
                    _playbackDevice.MasterMixer.RemoveComponent(_soundPlayer);
                    _soundPlayer.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "清理旧播放器时出错");
                }
            }
            
            _dataProvider?.Dispose();
            
            // 创建RawDataProvider - 专为PCM字节数据设计
            _dataProvider = new RawDataProvider(initialBuffer, SampleFormat.S16, sampleRate, channels);
            
            // 创建播放器
            _soundPlayer = new SoundPlayer(_engine, format, _dataProvider);
            
            // 添加到播放设备的混音器
            _playbackDevice.MasterMixer.AddComponent(_soundPlayer);
            
            _logger?.LogDebug("SoundFlow播放器初始化完成: {SampleRate}Hz, {Channels}ch", sampleRate, channels);
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SoundFlow播放器初始化错误");
            throw;
        }
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
                InitializePlaybackDevice(sampleRate, channels);
                await InitializePlayer(sampleRate, channels);
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
                _lastDataTime = DateTime.Now; // 更新最后接收数据的时间
            }

            // 如果还没开始播放，启动播放
            if (!_isPlaying && _playbackDevice != null && _soundPlayer != null)
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
            
            // 启动第一块数据的播放
            _soundPlayer.Play();
            
            // 启动定时器检查播放完成，类似PortAudioPlayer
            _playbackTimer.Change(200, 200); // 每200ms检查一次
            
            // 启动音频数据馈送任务，优化连续性
            _feedTask = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested && _isPlaying)
                {
                    byte[]? audioData = null;
                    
                    lock (_lock)
                    {
                        if (_audioQueue.Count > 0)
                        {
                            audioData = _audioQueue.Dequeue();
                        }
                    }

                    if (audioData != null)
                    {
                        // 更新音频数据，基于测试项目的实现
                        await UpdateAudioData(audioData);
                        
                        // 使用更精确的时序控制，减少间隙
                        // 计算播放时长：frames = bytes / (bytesPerSample * channels)
                        var frames = audioData.Length / (2 * _channels); // 16-bit = 2 bytes per sample
                        var durationMs = frames * 1000.0 / _sampleRate;
                        
                        // 使用较短的延迟避免累积误差，并检查队列状态
                        var delay = Math.Max(10, (int)(durationMs * 0.8)); // 只延迟80%的时间，提前准备下一块
                        await Task.Delay(delay, _cancellationTokenSource.Token);
                    }
                    else
                    {
                        // 没有数据时等待更短时间，提高响应性
                        await Task.Delay(5, _cancellationTokenSource.Token);
                        
                        // 减少空闲检查时间，避免长时间静音
                        var idleTime = 0;
                        while (_audioQueue.Count == 0 && idleTime < 200 && !_cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            await Task.Delay(5, _cancellationTokenSource.Token);
                            idleTime += 5;
                        }
                        
                        if (idleTime >= 200 && _audioQueue.Count == 0)
                        {
                            // 更快地检测到播放完成
                            break;
                        }
                    }
                }
                
                _isPlaying = false;
                _logger?.LogDebug("🔇 SoundFlow播放器任务结束");
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
    /// 更新下一块音频数据到播放器
    /// </summary>
    private async Task UpdateNextAudioData()
    {
        if (_engine == null || _playbackDevice == null)
        {
            _logger?.LogWarning("SoundFlow设备未就绪，忽略音频数据");
            return;
        }

        byte[]? audioData = null;
        lock (_lock)
        {
            if (_audioQueue.Count > 0)
            {
                audioData = _audioQueue.Dequeue();
            }
        }

        if (audioData == null || audioData.Length == 0)
        {
            // 没有数据，直接返回
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
            
            // 重新设置PlaybackEnded事件处理器（仅作为调试用途）
            if (_onPlaybackEnded == null)
            {
                _onPlaybackEnded = (sender, args) =>
                {
                    _logger?.LogDebug("SoundFlow PlaybackEnded 触发");
                };
            }
            _soundPlayer.PlaybackEnded += _onPlaybackEnded;

            _playbackDevice.MasterMixer.AddComponent(_soundPlayer);
            _soundPlayer.Play();

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "更新SoundFlow音频数据时出错");
        }
    }

    /// <summary>
    /// 更新音频数据到播放器，基于测试项目的成功实现
    /// </summary>
    private async Task UpdateAudioData(byte[] audioData)
    {
        if (_engine == null || _playbackDevice == null)
        {
            _logger?.LogWarning("SoundFlow设备未就绪，忽略音频数据");
            return;
        }

        try
        {
            // 优化策略：减少播放器重建的开销，改善连续性
            
            // 尝试更优雅的播放器更新过程
            if (_soundPlayer != null)
            {
                // 先暂停播放器，但不立即移除
                _soundPlayer.Pause();
                
                // 给一个很短的时间让当前音频缓冲区清空
                await Task.Delay(2, _cancellationTokenSource?.Token ?? CancellationToken.None);
                
                // 然后移除组件
                try { _playbackDevice.MasterMixer.RemoveComponent(_soundPlayer); } catch { /* ignore */ }
            }
            
            // 清理旧的provider
            _dataProvider?.Dispose();
            
            // 创建新的provider使用新数据
            _dataProvider = new RawDataProvider(audioData, SampleFormat.S16, _sampleRate, _channels);
            
            // 重新创建播放器
            var format = new AudioFormat
            {
                SampleRate = _sampleRate,
                Channels = _channels,
                Format = SampleFormat.S16
            };
            
            // 保存旧播放器引用
            var oldPlayer = _soundPlayer;
            
            // 创建新播放器
            _soundPlayer = new SoundPlayer(_engine, format, _dataProvider);
            
            // 立即添加到混音器并开始播放（减少间隙）
            _playbackDevice.MasterMixer.AddComponent(_soundPlayer);
            _soundPlayer.Play();
            
            // 异步清理旧播放器（避免阻塞）
            if (oldPlayer != null)
            {
                _ = Task.Run(() =>
                {
                    try { oldPlayer.Dispose(); } catch { /* ignore */ }
                });
            }
            
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
            // 停止定时器
            _playbackTimer.Change(Timeout.Infinite, Timeout.Infinite);

            // 取消音频馈送任务
            if (_isPlaying && _cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                if (_feedTask != null)
                {
                    try { await _feedTask; } catch { /* ignore */ }
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
            _playbackTimer?.Dispose();
            _cancellationTokenSource?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
