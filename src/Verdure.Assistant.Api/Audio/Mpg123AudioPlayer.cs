using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Api.Audio
{
    /// <summary>
    /// 基于mpg123的音频播放器实现
    /// 使用mpg123命令行工具进行音频播放，提供更好的跨平台兼容性和稳定性
    /// </summary>
    public class Mpg123AudioPlayer : IMusicAudioPlayer
    {
        private readonly ILogger<Mpg123AudioPlayer> _logger;
        private Process? _mpg123Process;
        private string? _currentFilePath;
        private MusicPlayerState _currentState = MusicPlayerState.Idle;
        private TimeSpan _duration = TimeSpan.Zero;
        private TimeSpan _currentPosition = TimeSpan.Zero;
        private double _volume = 50.0;
        private bool _disposed;
        private readonly object _lock = new object();
        private CancellationTokenSource? _positionUpdateCancellationTokenSource;

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
                // mpg123 使用 --volume 参数，范围是 0-32768
                ApplyVolumeSettings();
            }
        }

        public Mpg123AudioPlayer(ILogger<Mpg123AudioPlayer> logger)
        {
            _logger = logger;
            _logger.LogInformation("mpg123音频播放器初始化完成");
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

                _logger.LogInformation("开始播放音频 (mpg123)");
                Console.WriteLine($"[音乐缓存] 使用mpg123播放: {_currentFilePath}");
                
                await StartMpg123ProcessAsync();
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
                
                // 发送SIGSTOP信号暂停进程（在Windows上使用其他方法）
                if (_mpg123Process != null && !_mpg123Process.HasExited)
                {
                    // 在Windows上，我们停止进程并记录当前位置
                    await StopInternalAsync();
                    OnStateChanged(MusicPlayerState.Paused);
                }
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
                
                var wasPlaying = _currentState == MusicPlayerState.Playing;
                
                // 停止当前播放
                await StopInternalAsync();
                
                // 更新位置
                lock (_lock)
                {
                    _currentPosition = position;
                }
                
                // 如果之前在播放，从新位置继续播放
                if (wasPlaying)
                {
                    await StartMpg123ProcessAsync();
                    OnStateChanged(MusicPlayerState.Playing);
                    StartPositionUpdateTask();
                }
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
                var startInfo = new ProcessStartInfo
                {
                    FileName = "mpg123",
                    Arguments = $"--test \"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("无法启动mpg123进程");
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                // 解析mpg123输出获取时长
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("Time:"))
                    {
                        // 解析时长格式，例如 "Time: 03:45.123"
                        var timeMatch = System.Text.RegularExpressions.Regex.Match(line, @"Time:\s*(\d+):(\d+)\.(\d+)");
                        if (timeMatch.Success)
                        {
                            var minutes = int.Parse(timeMatch.Groups[1].Value);
                            var seconds = int.Parse(timeMatch.Groups[2].Value);
                            var milliseconds = int.Parse(timeMatch.Groups[3].Value);
                            return new TimeSpan(0, 0, minutes, seconds, milliseconds);
                        }
                    }
                }

                // 如果无法解析，返回默认值
                return TimeSpan.Zero;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取音频时长失败，使用默认值");
                return TimeSpan.Zero;
            }
        }

        private async Task StartMpg123ProcessAsync()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
                return;

            try
            {
                var volumeValue = (int)(_volume / 100.0 * 32768);
                var seekSeconds = _currentPosition.TotalSeconds;
                
                var arguments = $"--volume {volumeValue}";
                
                if (seekSeconds > 0)
                {
                    arguments += $" --skip {(int)seekSeconds}";
                }
                
                arguments += $" \"{_currentFilePath}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "mpg123",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _mpg123Process = Process.Start(startInfo);
                if (_mpg123Process == null)
                {
                    throw new InvalidOperationException("无法启动mpg123进程");
                }

                _logger.LogDebug("mpg123进程已启动，PID: {ProcessId}", _mpg123Process.Id);
                Console.WriteLine($"[音乐缓存] mpg123进程已启动，参数: {arguments}");

                // 监控进程退出
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _mpg123Process.WaitForExitAsync();
                        if (_currentState == MusicPlayerState.Playing)
                        {
                            OnStateChanged(MusicPlayerState.Ended);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "监控mpg123进程时发生异常");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动mpg123进程失败");
                throw;
            }
        }

        private async Task StopInternalAsync()
        {
            // 停止位置更新任务
            _positionUpdateCancellationTokenSource?.Cancel();
            
            if (_mpg123Process != null && !_mpg123Process.HasExited)
            {
                try
                {
                    _mpg123Process.Kill();
                    await _mpg123Process.WaitForExitAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "终止mpg123进程时发生异常");
                }
                finally
                {
                    _mpg123Process.Dispose();
                    _mpg123Process = null;
                }
            }
        }

        private void ApplyVolumeSettings()
        {
            // 对于正在播放的音频，我们需要重启进程来应用音量设置
            // 在实际应用中，可以考虑使用mpg123的控制接口
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放资源时发生异常");
            }
            
            _positionUpdateCancellationTokenSource?.Dispose();
            _logger.LogInformation("mpg123音频播放器已释放");
        }
    }
}
