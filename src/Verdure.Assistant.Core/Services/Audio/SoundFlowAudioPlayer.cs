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
using System.Threading.Channels;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// SoundFlow实现的音频播放器
/// 基于SoundFlowPlaybackTest的实现，使用SoundPlayer + QueueDataProvider进行连续流式音频播放
/// 提供与PortAudioPlayer相同的接口和功能
/// 优化了播放逻辑，参考PortAudioPlayer的连续数据流方式
/// </summary>
public class SoundFlowAudioPlayer : IAudioPlayer, IDisposable
{
    private readonly ILogger<SoundFlowAudioPlayer>? _logger;
    private AudioEngine? _engine;
    private AudioPlaybackDevice? _playbackDevice;
    private SoundPlayer? _soundPlayer;
    private QueueDataProvider? _dataProvider;
    private readonly Channel<byte[]> _audioChannel; // 使用 Channel 优化队列
    private readonly ChannelWriter<byte[]> _audioWriter;
    private readonly ChannelReader<byte[]> _audioReader;
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
        
        // 创建有界通道用于音频数据缓冲，优化播放性能
        var options = new BoundedChannelOptions(MaxQueueSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest, // 满时丢弃最旧数据，避免延迟
            SingleReader = true,   // 只有播放任务读取
            SingleWriter = false,  // 多个来源可能写入音频数据
            AllowSynchronousContinuations = false // 避免死锁
        };
        
        _audioChannel = Channel.CreateBounded<byte[]>(options);
        _audioWriter = _audioChannel.Writer;
        _audioReader = _audioChannel.Reader;
        
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
                if (_isPlaying && !_audioReader.TryPeek(out _))
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
    /// 初始化SoundPlayer与QueueDataProvider（仅在参数变化时调用）
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
                Format = SampleFormat.F32 // QueueDataProvider使用Float32格式
            };
            
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
            
            // 创建QueueDataProvider - 专为流式数据设计
            _dataProvider = new QueueDataProvider(format);
            
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
                // 使用 Channel 的非阻塞写入，如果满了会自动丢弃最旧数据
                if (!_audioWriter.TryWrite(audioData))
                {
                    _logger?.LogWarning("SoundFlow音频通道已满，丢弃音频数据");
                }
                _lastDataTime = DateTime.Now; // 更新最后接收数据的时间
            }

            // 如果还没开始播放，启动播放
            if (!_isPlaying && _playbackDevice != null && _soundPlayer != null)
            {
                await StartPlayback();
                _logger?.LogDebug("开始SoundFlow播放音频，使用Channel优化缓冲");
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
            
            // 启动播放器
            _soundPlayer.Play();
            
            // 启动定时器检查播放完成，类似PortAudioPlayer
            _playbackTimer.Change(200, 200); // 每200ms检查一次
            
            // 启动音频数据馈送任务，使用Channel优化流式播放
            _feedTask = Task.Run(async () =>
            {
                try
                {
                    // 使用Channel的异步枚举，更高效的音频数据处理
                    await foreach (var audioData in _audioReader.ReadAllAsync(_cancellationTokenSource.Token))
                    {
                        if (!_isPlaying) break;
                        
                        try
                        {
                            // 将数据添加到QueueDataProvider，它会自动处理流式播放
                            await UpdateAudioData(audioData);
                            
                            // 简化的延迟，不需要精确计算播放时长
                            await Task.Delay(10, _cancellationTokenSource.Token);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "处理音频数据时出错");
                        }
                    }
                    
                    // Channel完成后结束添加
                    _dataProvider?.CompleteAdding();
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，忽略
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "音频播放任务异常");
                }
                finally
                {
                    _isPlaying = false;
                    _logger?.LogDebug("🔇 SoundFlow播放器任务结束");
                }
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
    /// 更新下一块音频数据到播放器 - 使用QueueDataProvider的AddSamples方法
    /// </summary>
    private async Task UpdateNextAudioData()
    {
        if (_engine == null || _playbackDevice == null || _dataProvider == null)
        {
            _logger?.LogWarning("SoundFlow设备未就绪，忽略音频数据");
            return;
        }

        byte[]? audioData = null;
        // 尝试从Channel读取下一个音频数据
        if (!_audioReader.TryRead(out audioData))
        {
            // 没有数据
            audioData = null;
        }

        if (audioData == null || audioData.Length == 0)
        {
            // 没有数据，直接返回
            return;
        }

        try
        {
            // 将字节数据转换为float数组
            var floatSamples = new float[audioData.Length / 2]; // 16-bit = 2 bytes per sample
            for (int i = 0; i < floatSamples.Length; i++)
            {
                var sample = BitConverter.ToInt16(audioData, i * 2);
                floatSamples[i] = sample / 32768.0f; // 归一化到 [-1, 1]
            }
            
            // 添加到队列中
            _dataProvider.AddSamples(floatSamples);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "更新SoundFlow音频数据时出错");
        }
    }

    /// <summary>
    /// 更新音频数据到播放器，基于QueueDataProvider的流式播放实现
    /// </summary>
    private async Task UpdateAudioData(byte[] audioData)
    {
        if (_engine == null || _playbackDevice == null || _dataProvider == null)
        {
            _logger?.LogWarning("SoundFlow设备未就绪，忽略音频数据");
            return;
        }

        try
        {
            // 将字节数据转换为float数组
            var floatSamples = new float[audioData.Length / 2]; // 16-bit = 2 bytes per sample
            for (int i = 0; i < floatSamples.Length; i++)
            {
                var sample = BitConverter.ToInt16(audioData, i * 2);
                floatSamples[i] = sample / 32768.0f; // 归一化到 [-1, 1]
            }
            
            // 添加到队列中，QueueDataProvider会自动处理流式播放
            _dataProvider.AddSamples(floatSamples);
            
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

            // 完成Channel写入，清理剩余数据
            _audioWriter.TryComplete();
            
            // 消费剩余数据以清空Channel
            while (_audioReader.TryRead(out _)) { }

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
