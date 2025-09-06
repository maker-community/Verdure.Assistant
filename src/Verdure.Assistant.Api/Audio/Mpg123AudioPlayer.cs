using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace Verdure.Assistant.Api.Audio
{
    /// <summary>
    /// 基于SoundFlow的音频播放器实现
    /// 参考SoundFlow.Samples.VoiceInterruptionMusic的MP3播放逻辑
    /// 使用SoundFlow的StreamDataProvider直接播放MP3文件
    /// </summary>
    public class Mpg123AudioPlayer : IMusicAudioPlayer
    {
        private readonly ILogger<Mpg123AudioPlayer> _logger;
        private AudioEngine? _engine;
        private AudioPlaybackDevice? _playbackDevice;
        private SoundPlayer? _musicPlayer;
        private FileStream? _musicFileStream;
        private StreamDataProvider? _musicProvider;
        private string? _currentFilePath;
        private MusicPlayerState _currentState = MusicPlayerState.Idle;
        private TimeSpan _duration = TimeSpan.Zero;
        private TimeSpan _currentPosition = TimeSpan.Zero;
        private double _volume = 50.0;
        private bool _disposed;
        private readonly object _lock = new object();
        private CancellationTokenSource? _positionUpdateCancellationTokenSource;

        // SoundFlow配置
        private static readonly AudioFormat Format = AudioFormat.DvdHq; // 48kHz, 2 channels, F32
        private static readonly DeviceConfig DeviceConfig = new MiniAudioDeviceConfig
        {
            PeriodSizeInFrames = 960,
            Playback = new DeviceSubConfig { ShareMode = ShareMode.Shared },
            Wasapi = new WasapiSettings { Usage = WasapiUsage.ProAudio }
        };

        public event EventHandler<MusicPlayerStateChangedEventArgs>? StateChanged;
        public event EventHandler<MusicPlayerProgressEventArgs>? ProgressUpdated;

        public TimeSpan CurrentPosition 
        { 
            get 
            { 
                lock (_lock) 
                { 
                    return _currentPosition; 
                } 
            } 
        }

        public TimeSpan Duration 
        { 
            get 
            { 
                lock (_lock) 
                { 
                    return _duration; 
                } 
            } 
        }

        public bool IsPlaying => _currentState == MusicPlayerState.Playing;
        public bool IsPaused => _currentState == MusicPlayerState.Paused;

        public double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Max(0, Math.Min(100, value));
                _logger.LogDebug("音量设置为: {Volume}%", _volume);
                
                // 实时调节SoundFlow播放器音量
                if (_musicPlayer != null)
                {
                    _musicPlayer.Volume = (float)(_volume / 100.0);
                }
            }
        }

        public Mpg123AudioPlayer(ILogger<Mpg123AudioPlayer> logger)
        {
            _logger = logger;
            InitializeSoundFlowEngine();
            _logger.LogInformation("SoundFlow音频播放器初始化完成");
        }

        private void InitializeSoundFlowEngine()
        {
            try
            {
                _engine = new MiniAudioEngine();
                
                // 选择默认播放设备
                _engine.UpdateDevicesInfo();
                var playbackDevices = _engine.PlaybackDevices;
                
                if (playbackDevices.Length == 0)
                {
                    throw new InvalidOperationException("未找到可用的播放设备");
                }

                // 选择默认设备或第一个可用设备
                var deviceInfo = playbackDevices.FirstOrDefault(d => d.IsDefault);
                if (deviceInfo.Equals(default(DeviceInfo)))
                {
                    deviceInfo = playbackDevices[0];
                }
                _playbackDevice = _engine.InitializePlaybackDevice(deviceInfo, Format, DeviceConfig);
                _playbackDevice.Start();
                
                _logger.LogDebug("SoundFlow引擎初始化成功，播放设备: {DeviceName}", deviceInfo.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化SoundFlow引擎失败");
                throw;
            }
        }

        public async Task LoadAsync(string filePath)
        {
            try
            {
                _logger.LogInformation("加载音频文件: {FilePath}", filePath);
                Console.WriteLine($"[音乐缓存] 加载音频文件路径: {filePath}");
                
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"音频文件不存在: {filePath}");
                }

                await StopInternalAsync();
                
                _currentFilePath = filePath;
                _duration = await GetAudioDurationAsync(filePath);
                _currentPosition = TimeSpan.Zero;
                
                OnStateChanged(MusicPlayerState.Loaded);
                _logger.LogInformation("音频文件加载成功，时长: {Duration}", _duration);
                Console.WriteLine($"[音乐缓存] 音频文件时长: {_duration}");
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
                _logger.LogInformation("从URL加载音频: {Url}", url);
                
                // 对于URL，我们需要先下载到临时文件
                var tempFile = Path.GetTempFileName() + ".mp3";
                Console.WriteLine($"[音乐缓存] 下载音频到临时文件: {tempFile}");
                
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    
                    await using var fileStream = File.Create(tempFile);
                    await response.Content.CopyToAsync(fileStream);
                }
                
                await LoadAsync(tempFile);
                _logger.LogInformation("音频流加载成功");
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
                if (string.IsNullOrEmpty(_currentFilePath))
                {
                    _logger.LogWarning("没有加载音频文件，无法播放");
                    return;
                }

                if (_currentState == MusicPlayerState.Playing)
                {
                    _logger.LogDebug("音频已在播放中");
                    return;
                }

                _logger.LogInformation("开始播放音频 (SoundFlow)");
                Console.WriteLine($"[音乐缓存] 使用SoundFlow播放: {_currentFilePath}");
                
                await SetupMusicPlayerAsync();
                _musicPlayer?.Play();
                
                OnStateChanged(MusicPlayerState.Playing);
                
                // 启动位置更新任务
                StartPositionUpdateTask();
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
                
                _musicPlayer?.Pause();
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
                _logger.LogInformation("停止播放");
                
                await StopInternalAsync();
                _currentPosition = TimeSpan.Zero;
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
                if (string.IsNullOrEmpty(_currentFilePath))
                {
                    _logger.LogWarning("没有加载音频文件，无法跳转");
                    return;
                }

                _logger.LogInformation("跳转到位置: {Position}", position);
                
                // SoundFlow StreamDataProvider支持跳转
                if (_musicProvider != null)
                {
                    // 计算字节位置（粗略估算）
                    var ratio = position.TotalSeconds / _duration.TotalSeconds;
                    var fileInfo = new FileInfo(_currentFilePath);
                    var targetPosition = (long)(fileInfo.Length * ratio);
                    
                    // 重新创建播放器以跳转到新位置
                    var wasPlaying = _currentState == MusicPlayerState.Playing;
                    await StopInternalAsync();
                    
                    lock (_lock)
                    {
                        _currentPosition = position;
                    }
                    
                    if (wasPlaying)
                    {
                        await PlayAsync();
                    }
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

        #region 私有方法

        private async Task<TimeSpan> GetAudioDurationAsync(string filePath)
        {
            try
            {
                // 使用简单的文件大小估算时长（临时方案）
                var fileInfo = new FileInfo(filePath);
                // 假设MP3平均比特率为128kbps
                var estimatedDurationSeconds = fileInfo.Length / (128 * 1024 / 8);
                await Task.Delay(1); // 避免async警告
                return TimeSpan.FromSeconds(estimatedDurationSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取音频时长失败，使用默认值");
                await Task.Delay(1); // 避免async警告
                return TimeSpan.FromMinutes(3); // 默认3分钟
            }
        }

        private async Task SetupMusicPlayerAsync()
        {
            if (string.IsNullOrEmpty(_currentFilePath) || _playbackDevice == null || _engine == null)
                return;

            try
            {
                // 清理现有播放器
                await StopInternalAsync();

                // 创建文件流和数据提供器
                _musicFileStream = new FileStream(_currentFilePath, FileMode.Open, FileAccess.Read);
                _musicProvider = new StreamDataProvider(_engine, Format, _musicFileStream);
                _musicPlayer = new SoundPlayer(_engine, Format, _musicProvider);

                // 设置音量
                _musicPlayer.Volume = (float)(_volume / 100.0);

                // 添加到设备混音器
                _playbackDevice.MasterMixer.AddComponent(_musicPlayer);

                _logger.LogDebug("SoundFlow音乐播放器设置完成");
                Console.WriteLine($"[音乐缓存] SoundFlow音乐播放器设置完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置音乐播放器失败");
                await StopInternalAsync();
                throw;
            }
        }

        private async Task StopInternalAsync()
        {
            // 停止位置更新任务
            _positionUpdateCancellationTokenSource?.Cancel();
            
            try
            {
                if (_musicPlayer != null && _playbackDevice != null)
                {
                    _musicPlayer.Stop();
                    _playbackDevice.MasterMixer.RemoveComponent(_musicPlayer);
                    _musicPlayer.Dispose();
                    _musicPlayer = null;
                }

                _musicProvider?.Dispose();
                _musicProvider = null;

                _musicFileStream?.Dispose();
                _musicFileStream = null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理音乐播放器时发生异常");
            }

            await Task.CompletedTask;
        }

        private void StartPositionUpdateTask()
        {
            _positionUpdateCancellationTokenSource?.Cancel();
            _positionUpdateCancellationTokenSource = new CancellationTokenSource();
            
            _ = Task.Run(async () =>
            {
                var token = _positionUpdateCancellationTokenSource.Token;
                var startTime = DateTime.Now;
                var initialPosition = _currentPosition;
                
                while (!token.IsCancellationRequested && _currentState == MusicPlayerState.Playing)
                {
                    try
                    {
                        await Task.Delay(1000, token);
                        
                        var elapsed = DateTime.Now - startTime;
                        var newPosition = initialPosition + elapsed;
                        
                        lock (_lock)
                        {
                            _currentPosition = newPosition;
                        }
                        
                        var progressArgs = new MusicPlayerProgressEventArgs(newPosition, _duration);
                        ProgressUpdated?.Invoke(this, progressArgs);
                        
                        // 检查是否播放完成
                        if (newPosition >= _duration && _duration > TimeSpan.Zero)
                        {
                            OnStateChanged(MusicPlayerState.Ended);
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "更新播放位置时发生异常");
                        break;
                    }
                }
            }, _positionUpdateCancellationTokenSource.Token);
        }

        private void OnStateChanged(MusicPlayerState state, string? errorMessage = null)
        {
            _currentState = state;
            var args = new MusicPlayerStateChangedEventArgs(state, errorMessage);
            StateChanged?.Invoke(this, args);
            
            _logger.LogDebug("播放状态变更: {State}", state);
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            
            try
            {
                StopInternalAsync().Wait(5000);
                
                _playbackDevice?.Stop();
                _playbackDevice?.Dispose();
                _engine?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放资源时发生异常");
            }
            
            _positionUpdateCancellationTokenSource?.Dispose();
            _logger.LogInformation("SoundFlow音频播放器已释放");
        }
    }
}
