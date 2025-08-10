using System;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Console.Services.Audio;
using Verdure.Assistant.Console.Audio;

namespace Verdure.Assistant.Console
{
    /// <summary>
    /// Console平台的音乐播放器实现
    /// 使用 NLayer 解码 MP3，PortAudioSharp2 进行音频播放，提供跨平台音频播放能力
    /// 参考 WinUIMusicAudioPlayer 的架构设计
    /// </summary>
    public class ConsoleMusicAudioPlayer : IMusicAudioPlayer
    {
        private readonly ILogger<ConsoleMusicAudioPlayer> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private Mp3Decoder? _decoder;
        private AudioBuffer? _audioBuffer;
        private AudioDecodeThread? _decodeThread;
        private PortAudioPlayer? _portAudioPlayer;
        private readonly System.Timers.Timer _progressTimer;
        
        private bool _disposed;
        private MusicPlayerState _currentState = MusicPlayerState.Idle;
        private double _volume = 50.0;
        private TimeSpan _currentPosition = TimeSpan.Zero;
        private TimeSpan _duration = TimeSpan.Zero;
        private string? _currentSource;

        public event EventHandler<MusicPlayerStateChangedEventArgs>? StateChanged;
        public event EventHandler<MusicPlayerProgressEventArgs>? ProgressUpdated;

        public TimeSpan CurrentPosition => _currentPosition;
        public TimeSpan Duration => _duration;
        public bool IsPlaying => _currentState == MusicPlayerState.Playing;
        public bool IsPaused => _currentState == MusicPlayerState.Paused;

        public double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Max(0, Math.Min(100, value));
                _logger.LogDebug("音量设置为: {Volume}%", _volume);
            }
        }

        public ConsoleMusicAudioPlayer(ILogger<ConsoleMusicAudioPlayer> logger)
        {
            _logger = logger;
            _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            
            // 初始化 PortAudioPlayer
            _portAudioPlayer = new PortAudioPlayer(_loggerFactory.CreateLogger<PortAudioPlayer>());
            
            // 设置进度更新定时器
            _progressTimer = new System.Timers.Timer(1000); // 每秒更新一次
            _progressTimer.Elapsed += OnProgressTimerElapsed;
            
            _logger.LogInformation("Console音乐播放器初始化完成 (NLayer + PortAudioSharp2)");
        }

        public async Task LoadAsync(string filePath)
        {
            try
            {
                _logger.LogInformation("加载音频文件: {FilePath}", filePath);
                
                // 重置状态
                await ResetAsync();
                
                // 创建解码器和相关组件
                _decoder = new Mp3Decoder(_loggerFactory.CreateLogger<Mp3Decoder>());
                await _decoder.LoadFromFileAsync(filePath);
                
                _audioBuffer = new AudioBuffer();
                _decodeThread = new AudioDecodeThread(_loggerFactory.CreateLogger<AudioDecodeThread>(), _decoder, _audioBuffer);
                
                // 绑定事件
                _decodeThread.ProgressUpdated += OnDecodeProgressUpdated;
                
                // 更新状态
                _currentSource = filePath;
                _duration = _decoder.Duration;
                _currentPosition = TimeSpan.Zero;
                
                OnStateChanged(MusicPlayerState.Loaded);
                _logger.LogInformation("音频文件加载成功，时长: {Duration}", _duration);
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
                
                // 重置状态
                await ResetAsync();
                
                // 创建解码器和相关组件
                _decoder = new Mp3Decoder(_loggerFactory.CreateLogger<Mp3Decoder>());
                await _decoder.LoadFromUrlAsync(url);
                
                _audioBuffer = new AudioBuffer();
                _decodeThread = new AudioDecodeThread(_loggerFactory.CreateLogger<AudioDecodeThread>(), _decoder, _audioBuffer);
                
                // 绑定事件
                _decodeThread.ProgressUpdated += OnDecodeProgressUpdated;
                
                // 更新状态
                _currentSource = url;
                _duration = _decoder.Duration;
                _currentPosition = TimeSpan.Zero;
                
                OnStateChanged(MusicPlayerState.Loaded);
                _logger.LogInformation("音频流加载成功，时长: {Duration}", _duration);
                
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
                if (_decoder == null || _decodeThread == null || _audioBuffer == null || _portAudioPlayer == null)
                {
                    _logger.LogWarning("没有加载音频文件，无法播放");
                    return;
                }

                if (_currentState == MusicPlayerState.Playing)
                {
                    _logger.LogDebug("音频已在播放中");
                    return;
                }

                _logger.LogInformation("开始播放音频 (NLayer 解码 + PortAudio 播放)");
                
                // 启动解码线程
                _decodeThread.Start();
                
                // 启动 PortAudio 播放
                await _portAudioPlayer.StartAsync(_audioBuffer, _decoder.SampleRate);
                
                // 启动进度定时器
                _progressTimer.Start();
                
                OnStateChanged(MusicPlayerState.Playing);
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
                if (_currentState != MusicPlayerState.Playing)
                {
                    _logger.LogDebug("当前不在播放状态，无法暂停");
                    return;
                }

                _logger.LogInformation("暂停播放");
                
                // 停止 PortAudio 播放
                if (_portAudioPlayer != null)
                {
                    await _portAudioPlayer.StopAsync();
                }
                
                // 停止进度定时器
                _progressTimer.Stop();
                
                OnStateChanged(MusicPlayerState.Paused);
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
                _logger.LogInformation("停止播放");
                
                // 停止 PortAudio 播放
                if (_portAudioPlayer != null)
                {
                    await _portAudioPlayer.StopAsync();
                }
                
                // 停止解码线程
                _decodeThread?.Stop();
                
                // 停止进度定时器
                _progressTimer.Stop();
                
                // 重置播放位置
                _currentPosition = TimeSpan.Zero;
                if (_decoder != null)
                {
                    _decoder.SeekTo(TimeSpan.Zero);
                }
                
                OnStateChanged(MusicPlayerState.Stopped);
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
                if (_decoder == null)
                {
                    _logger.LogWarning("没有加载音频文件，无法跳转");
                    return;
                }

                _logger.LogInformation("跳转到位置: {Position}", position);
                
                var wasPlaying = _currentState == MusicPlayerState.Playing;
                
                // 如果正在播放，先暂停
                if (wasPlaying)
                {
                    await PauseAsync();
                }
                
                // 执行跳转
                _decoder.SeekTo(position);
                _currentPosition = position;
                
                // 清空缓冲区
                _audioBuffer?.Clear();
                
                // 如果之前在播放，继续播放
                if (wasPlaying)
                {
                    await PlayAsync();
                }
                
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

        private void OnProgressTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (_decoder != null && _currentState == MusicPlayerState.Playing)
            {
                _currentPosition = _decoder.GetCurrentPosition();
                
                var progressArgs = new MusicPlayerProgressEventArgs(_currentPosition, _duration);
                ProgressUpdated?.Invoke(this, progressArgs);
            }
        }

        private void OnDecodeProgressUpdated(object? sender, DecodeProgressEventArgs e)
        {
            // 检查是否到达流末尾
            if (_audioBuffer?.IsEndOfStream == true && _currentState == MusicPlayerState.Playing)
            {
                OnStateChanged(MusicPlayerState.Ended);
            }
        }

        private void OnStateChanged(MusicPlayerState state, string? errorMessage = null)
        {
            _currentState = state;
            var args = new MusicPlayerStateChangedEventArgs(state, errorMessage);
            StateChanged?.Invoke(this, args);
            
            _logger.LogDebug("播放状态变更: {State}", state);
        }

        #endregion

        #region 私有方法

        private async Task ResetAsync()
        {
            // 停止所有活动
            if (_portAudioPlayer != null)
            {
                await _portAudioPlayer.StopAsync();
            }
            _decodeThread?.Stop();
            _progressTimer.Stop();
            
            // 清理资源
            _decodeThread?.Dispose();
            _decoder?.Dispose();
            _audioBuffer?.Dispose();
            
            // 重置状态
            _decodeThread = null;
            _decoder = null;
            _audioBuffer = null;
            _currentSource = null;
            _currentPosition = TimeSpan.Zero;
            _duration = TimeSpan.Zero;
            _currentState = MusicPlayerState.Idle;
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            
            try
            {
                ResetAsync().Wait(1000);
                _portAudioPlayer?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放资源时发生异常");
            }
            
            _progressTimer?.Dispose();
            _logger.LogInformation("Console音乐播放器已释放");
        }
    }
}
