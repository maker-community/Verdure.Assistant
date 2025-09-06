using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Console.Services.Audio;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Structs;
using SoundFlow.Providers;

namespace Verdure.Assistant.Console.Audio
{
    /// <summary>
    /// SoundFlow实现的音乐播放器 - 替代PortAudioPlayer
    /// 提供与PortAudioPlayer相同的接口，用于Console音乐播放
    /// 使用SoundFlow进行连续音频流播放
    /// </summary>
    public class SoundFlowMusicPlayer : IDisposable
    {
        private readonly ILogger<SoundFlowMusicPlayer> _logger;
        private MiniAudioEngine? _engine;
        private AudioPlaybackDevice? _playbackDevice;
        private SoundPlayer? _soundPlayer;
        private QueueDataProvider? _dataProvider;
        private bool _isPlaying;
        private bool _isDisposed;
        private AudioBuffer? _audioBuffer;
        private int _sampleRate;
        private int _channels;
        private Task? _feedTask;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly object _lock = new object();

        // SoundFlow 设备配置 - 针对音乐播放优化
        private static readonly MiniAudioDeviceConfig DeviceConfig = new()
        {
            PeriodSizeInFrames = 1024,   // 64ms @ 16kHz，音乐播放可以用更大的缓冲
            PeriodSizeInMilliseconds = 0,
            Periods = 3,
            NoPreSilencedOutputBuffer = false,
            NoClip = false,
            NoDisableDenormals = false,
            NoFixedSizedCallback = false
        };

        public bool IsPlaying
        {
            get
            {
                lock (_lock)
                {
                    return _isPlaying;
                }
            }
        }

        public SoundFlowMusicPlayer(ILogger<SoundFlowMusicPlayer> logger)
        {
            _logger = logger;
            _logger.LogDebug("SoundFlowMusicPlayer 初始化");
        }

        /// <summary>
        /// 启动音频播放 - 兼容PortAudioPlayer接口
        /// </summary>
        public async Task StartAsync(AudioBuffer audioBuffer, int sampleRate)
        {
            if (_isDisposed) return;

            try
            {
                lock (_lock)
                {
                    if (_isPlaying)
                    {
                        _logger.LogDebug("SoundFlow音乐播放器已在播放中");
                        return;
                    }

                    _audioBuffer = audioBuffer;
                    _sampleRate = sampleRate;
                    _channels = 1; // 音乐通常是单声道或立体声，这里先用单声道
                }

                // 初始化SoundFlow引擎
                if (_engine == null)
                {
                    _engine = new MiniAudioEngine();
                    _logger.LogDebug("SoundFlow音频引擎创建成功");
                }

                // 创建音频格式
                var format = new AudioFormat
                {
                    SampleRate = sampleRate,
                    Channels = _channels,
                    Format = SampleFormat.F32  // Float32格式，匹配AudioBuffer
                };

                // 创建播放设备
                _playbackDevice = _engine.InitializePlaybackDevice(null, format, DeviceConfig);
                _logger.LogDebug("SoundFlow播放设备创建成功");

                // 创建QueueDataProvider和音频播放器
                _dataProvider = new QueueDataProvider(format);
                _soundPlayer = new SoundPlayer(_engine, format, _dataProvider);
                
                // 添加播放器到设备混音器
                _playbackDevice.MasterMixer.AddComponent(_soundPlayer);
                _logger.LogDebug($"SoundFlow播放器创建成功，采样率: {sampleRate}Hz");

                // 启动播放设备
                _playbackDevice.Start();
                _logger.LogDebug("SoundFlow播放设备启动");

                // 启动音频数据馈送任务
                _cancellationTokenSource = new CancellationTokenSource();
                _feedTask = FeedAudioDataAsync(_cancellationTokenSource.Token);

                lock (_lock)
                {
                    _isPlaying = true;
                }

                _logger.LogInformation($"SoundFlow音乐播放器启动成功，采样率: {sampleRate}Hz");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动SoundFlow音乐播放器失败");
                throw new InvalidOperationException($"启动SoundFlow音乐播放器失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 停止音频播放
        /// </summary>
        public async Task StopAsync()
        {
            if (_isDisposed) return;

            try
            {
                lock (_lock)
                {
                    if (!_isPlaying)
                    {
                        _logger.LogDebug("SoundFlow音乐播放器已停止");
                        return;
                    }
                    _isPlaying = false;
                }

                // 停止音频数据馈送
                _cancellationTokenSource?.Cancel();
                
                if (_feedTask != null)
                {
                    try
                    {
                        await _feedTask.WaitAsync(TimeSpan.FromSeconds(2));
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogWarning("等待音频馈送任务停止超时");
                    }
                }

                // 停止播放器和设备
                _soundPlayer?.Stop();
                _playbackDevice?.Stop();

                // 清理资源
                _soundPlayer?.Dispose();
                _playbackDevice?.Dispose();
                _soundPlayer = null;
                _playbackDevice = null;

                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _feedTask = null;

                _logger.LogInformation("SoundFlow音乐播放器已停止");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "停止SoundFlow音乐播放器时出现警告");
            }
        }

        /// <summary>
        /// 音频数据馈送任务
        /// </summary>
        private async Task FeedAudioDataAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogDebug("开始音频数据馈送");

                while (!cancellationToken.IsCancellationRequested && _isPlaying)
                {
                    if (_audioBuffer == null)
                    {
                        await Task.Delay(10, cancellationToken);
                        continue;
                    }

                    // 从AudioBuffer读取数据
                    var audioChunk = await _audioBuffer.TryDequeueAsync(100);
                    if (audioChunk == null)
                    {
                        if (_audioBuffer.IsEndOfStream)
                        {
                            _logger.LogDebug("音频流结束");
                            break;
                        }
                        continue;
                    }

                    // 发送到SoundFlow播放器
                    if (_dataProvider != null && audioChunk.Length > 0)
                    {
                        // AudioBuffer已经提供float[]数据，直接使用
                        _dataProvider.AddSamples(audioChunk);
                    }
                }

                _logger.LogDebug("音频数据馈送完成");
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("音频数据馈送被取消");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "音频数据馈送过程中出错");
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            try
            {
                StopAsync().Wait(5000); // 5秒超时
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Dispose 时停止播放器出现警告");
            }
            finally
            {
                _engine?.Dispose();
                _engine = null;
                _isDisposed = true;
                _logger?.LogDebug("SoundFlowMusicPlayer 已释放");
            }
        }
    }
}