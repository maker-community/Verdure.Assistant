using Microsoft.Extensions.Logging;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;
using System.Threading.Channels;
using Verdure.Assistant.Core.Interfaces;

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
    private readonly object _lock = new();
    private bool _isPlaying = false;
    private bool _isDisposed = false;
    private int _sampleRate = 16000;
    private int _channels = 1;
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

        // 创建无界通道用于音频数据缓冲，避免阻塞问题
        var options = new UnboundedChannelOptions
        {
            SingleReader = true,   // 只有播放任务读取
            SingleWriter = false,  // 多个来源可能写入音频数据
            AllowSynchronousContinuations = false // 避免死锁
        };

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

            // 修复：统一使用F32格式，与QueueDataProvider保持一致
            var format = new AudioFormat
            {
                SampleRate = sampleRate,
                Channels = channels,
                Format = SampleFormat.F32  // 改为F32，与播放器格式一致
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

            _dataProvider.EndOfStreamReached += (s, e) =>
            {
                _logger?.LogDebug("SoundFlow数据提供者已到达流末尾");
                PlaybackStopped?.Invoke(this, EventArgs.Empty);
            };

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
            // 验证输入数据
            if (audioData == null || audioData.Length == 0)
            {
                _logger?.LogWarning("接收到空的音频数据");
                return;
            }

            // 验证数据格式
            if (audioData.Length % 2 != 0)
            {
                _logger?.LogWarning("音频数据长度不是2的倍数: {Length}字节", audioData.Length);
                return;
            }

            // 如果参数不匹配或设备未就绪，重新初始化设备与播放器
            if (_sampleRate != sampleRate || _channels != channels || _playbackDevice == null || _soundPlayer == null)
            {
                await StopAsync();
                InitializePlaybackDevice(sampleRate, channels);
                await InitializePlayer(sampleRate, channels);
            }

            // 直接转换并播放音频数据，不使用Channel
            await PlayAudioDataDirectly(audioData);

            // 如果还没开始播放设备，启动播放
            if (!_isPlaying && _playbackDevice != null && _soundPlayer != null)
            {
                await StartPlayback();
                _logger?.LogDebug("开始SoundFlow播放音频");
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
            // 启动播放设备
            _playbackDevice.Start();
            _logger?.LogDebug("🔊 SoundFlow播放设备启动");

            if (_dataProvider?.SamplesAvailable > 1000)
            {
                _isPlaying = true;

                // 启动播放器
                _soundPlayer.Play();
                _logger?.LogDebug("🔊 SoundFlow播放器启动");

                await Task.CompletedTask;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "启动SoundFlow播放时出错");
            _isPlaying = false;
        }
    }

    /// <summary>
    /// 直接播放音频数据，不使用Channel缓冲
    /// </summary>
    private async Task PlayAudioDataDirectly(byte[] audioData)
    {
        if (_engine == null || _playbackDevice == null || _dataProvider == null)
        {
            _logger?.LogWarning("SoundFlow设备未就绪，忽略音频数据");
            return;
        }

        try
        {
            // 验证输入数据
            if (audioData.Length == 0 || audioData.Length % 2 != 0)
            {
                _logger?.LogWarning("音频数据长度无效: {Length}字节", audioData.Length);
                return;
            }

            // 将16位PCM字节数据正确转换为32位float数组
            var sampleCount = audioData.Length / 2; // 16-bit = 2 bytes per sample
            var floatSamples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                // 正确处理字节序列（小端序）
                var sample = BitConverter.ToInt16(audioData, i * 2);
                // 正确的归一化：-32768到32767映射到-1.0到1.0
                floatSamples[i] = sample / 32768.0f;
            }

            // 添加调试信息
            var rms = Math.Sqrt(floatSamples.Select(s => s * s).Average());
            _logger?.LogDebug("直接播放音频: {SampleCount}样本, RMS={Rms:F4}", sampleCount, rms);

            // 直接添加到QueueDataProvider
            _dataProvider.AddSamples(floatSamples);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "直接播放音频数据时出错");
        }
    }

    public async Task StopAsync()
    {
        try
        {
            _isPlaying = false;
            // 停止底层设备与播放器
            try
            {
                //_soundPlayer?.Stop();
                _logger?.LogDebug("🔇 SoundFlow播放器已停止");
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "停止播放器时出现警告");
            }

            if (_playbackDevice != null)
            {
                try
                {
                    //_playbackDevice.Stop();
                    _logger?.LogDebug("🔇 SoundFlow播放设备已停止");
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "停止SoundFlow播放设备时出现警告");
                }
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
            //StopAsync().Wait(3000); // 3秒超时
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
            GC.SuppressFinalize(this);
        }
    }

    public void CompleteAdding()
    {
        _dataProvider?.CompleteAdding();
    }

    public void Reset()
    {
        _dataProvider?.Reset();
    }
}
