using PortAudioSharp;
using Verdure.Assistant.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// PortAudioSharp2实现的音频播放器 - 直接实现接口
/// </summary>
public class PortAudioPlayer : IAudioPlayer, IDisposable
{    
    private readonly ILogger<PortAudioPlayer>? _logger;
    private PortAudioSharp.Stream? _outputStream;
    private readonly Queue<byte[]> _audioQueue = new();
    private readonly object _lock = new();
    private bool _isPlaying = false;
    private bool _isDisposed = false;
    private bool _portAudioInitialized = false;
    private int _sampleRate = 16000;
    private int _channels = 1;
    private int _emptyFrameCount = 0; // 空帧计数器
    private const int MaxEmptyFrames = 50; // 最大空帧数（约1秒的静音后停止）
    private DateTime _lastDataTime = DateTime.Now;
    private readonly Timer _playbackTimer;
    private const int MaxQueueSize = 20; // 最大队列大小，防止内存积累

    public event EventHandler? PlaybackStopped;
    public bool IsPlaying => _isPlaying;

    public PortAudioPlayer(ILogger<PortAudioPlayer>? logger = null)
    {
        _logger = logger;
        // 创建定时器来检测播放完成（类似Python中的延迟状态变更）
        _playbackTimer = new Timer(CheckPlaybackCompletion, null, Timeout.Infinite, Timeout.Infinite);
    }

    private void CheckPlaybackCompletion(object? state)
    {
        lock (_lock)
        {
            // Check if playback should be considered complete (similar to Python's queue monitoring)
            if (_isPlaying && _audioQueue.Count == 0)
            {
                // More conservative timing - wait longer to ensure all audio is played
                var timeSinceLastData = (DateTime.Now - _lastDataTime).TotalMilliseconds;
                var shouldStop = timeSinceLastData > 1500; // Increased from 1000ms to 1500ms

                if (shouldStop)
                {
                    _logger?.LogDebug("Playback completion detected - no data for {TimeSinceLastData}ms", timeSinceLastData);

                    // Stop timer first to prevent multiple triggers
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
                            _logger?.LogError(ex, "Error in playback completion handler");
                        }
                    });
                }
            }
        }
    }

    /// <summary>
    /// 确保 PortAudio 已初始化
    /// </summary>
    private bool EnsurePortAudioInitialized()
    {
        if (!_portAudioInitialized)
        {
            try
            {
                PortAudio.Initialize();
                _portAudioInitialized = true;
                _logger?.LogDebug("PortAudio 播放器初始化成功");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "PortAudio 播放器初始化失败");
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 清理 PortAudio
    /// </summary>
    private void CleanupPortAudio()
    {
        if (_portAudioInitialized)
        {
            try
            {
                // 平台自适应超时：ARM设备用更短的超时时间
                var timeout = Environment.ProcessorCount <= 4 ? 1000 : 2000;
                
                var terminateTask = Task.Run(() =>
                {
                    try
                    {
                        PortAudio.Terminate();
                        return true;
                    }
                    catch (PortAudioException paEx)
                    {
                        _logger?.LogDebug(paEx, "PortAudio 播放器终止时的预期异常");
                        return true; // 对于 PortAudio 异常，认为是成功的
                    }
                });

                var completed = terminateTask.Wait(timeout);
                
                if (completed && terminateTask.Result)
                {
                    _logger?.LogDebug("PortAudio 播放器已终止");
                }
                else
                {
                    _logger?.LogWarning("PortAudio 播放器终止超时");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "PortAudio 播放器终止时出错");
            }
            finally
            {
                _portAudioInitialized = false;
            }
        }
    }

    /// <summary>
    /// 初始化音频播放器
    /// </summary>
    public async Task InitializeAsync(int sampleRate, int channels)
    {
        if (!ValidateAudioParameters(sampleRate, channels))
        {
            throw new ArgumentException("Invalid audio parameters");
        }

        _sampleRate = sampleRate;
        _channels = channels;

        try
        {
            // 确保 PortAudio 已初始化
            if (!EnsurePortAudioInitialized())
            {
                throw new InvalidOperationException("无法获取音频资源");
            }
            
            // 获取默认输出设备
            var defaultOutputDevice = PortAudio.DefaultOutputDevice;
            if (defaultOutputDevice == -1)
            {
                throw new InvalidOperationException("未找到音频输出设备");
            }

            // 配置音频流参数 - 匹配Python配置
            var outputParameters = new StreamParameters
            {
                device = defaultOutputDevice,
                channelCount = channels,
                sampleFormat = SampleFormat.Int16, // 使用Int16匹配Python的paInt16
                suggestedLatency = PortAudio.GetDeviceInfo(defaultOutputDevice).defaultLowOutputLatency
            };

            // 计算帧大小 (60ms帧，匹配Python配置)
            var frameSize = (uint)(sampleRate * 60 / 1000);

            // 创建输出流
            _outputStream = new PortAudioSharp.Stream(
                null,
                outputParameters,
                sampleRate,
                frameSize,
                StreamFlags.ClipOff, // 使用ClipOff匹配其他实现
                OnAudioDataRequested,
                IntPtr.Zero);

            _logger?.LogInformation("音频播放器初始化成功: {SampleRate}Hz, {Channels}声道, 帧大小: {FrameSize}", 
                sampleRate, channels, frameSize);
            await Task.CompletedTask;
        }        
        catch (Exception ex)
        {
            throw new Exception($"初始化音频播放器失败: {ex.Message}", ex);
        }
    }

    public async Task PlayAsync(byte[] audioData, int sampleRate = 16000, int channels = 1)
    {
        if (_isDisposed) return;
        
        try
        {
            // 如果参数不匹配，重新初始化
            if (_sampleRate != sampleRate || _channels != channels || _outputStream == null)
            {
                await StopAsync();
                await InitializeAsync(sampleRate, channels);
            }

            lock (_lock)
            {
                // 防止音频队列过大导致延迟和内存问题
                if (_audioQueue.Count >= MaxQueueSize)
                {
                    _logger?.LogWarning("音频队列过大，清理旧数据以防止杂音");
                    while (_audioQueue.Count > MaxQueueSize / 2)
                    {
                        _audioQueue.Dequeue();
                    }
                }
                
                _audioQueue.Enqueue(audioData);
                _lastDataTime = DateTime.Now; // 更新最后接收数据的时间
            }

            if (!_isPlaying && _outputStream != null)
            {
                _outputStream.Start();
                _isPlaying = true;

                // 启动定时器检测播放完成，更频繁的检查
                _playbackTimer.Change(200, 200); // 每200ms检查一次，提高响应性
                
                _logger?.LogDebug("开始播放音频，队列长度: {QueueCount}", _audioQueue.Count);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "播放音频时出错");
        }
    }

    public async Task StopAsync()
    {
        if (!_isPlaying) return;

        try
        {
            // 停止定时器
            _playbackTimer.Change(Timeout.Infinite, Timeout.Infinite);

            _isPlaying = false;

            // 安全停止音频流
            if (_outputStream != null)
            {
                try
                {
                    _outputStream.Stop();
                    _outputStream.Close();
                    _outputStream.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "停止音频流时出现警告");
                }
                finally
                {
                    _outputStream = null;
                }
            }

            // 安全清理队列
            lock (_lock)
            {
                _audioQueue.Clear();
            }

            _logger?.LogInformation("音频播放已停止");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止音频播放时出错");
        }
    }    
    
    private StreamCallbackResult OnAudioDataRequested(
        IntPtr input,
        IntPtr output,
        uint frameCount,
        ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags,
        IntPtr userData)
    {
        try
        {
            if (output != IntPtr.Zero && frameCount > 0)
            {
                byte[]? audioData = null;
                int queueCount;

                lock (_lock)
                {
                    queueCount = _audioQueue.Count;
                    if (queueCount > 0)
                    {
                        audioData = _audioQueue.Dequeue();
                        _emptyFrameCount = 0; // 重置空帧计数
                    }
                }

                if (audioData != null)
                {
                    // 计算要复制的数据大小 (16位音频 = 2字节/样本)
                    int bytesToCopy = Math.Min(audioData.Length, (int)(frameCount * _channels * 2));

                    // 清零输出缓冲区以防止杂音
                    var totalBytes = (int)(frameCount * _channels * 2);
                    var zeroBuffer = new byte[totalBytes];
                    System.Runtime.InteropServices.Marshal.Copy(zeroBuffer, 0, output, totalBytes);
                    
                    // 复制实际音频数据
                    System.Runtime.InteropServices.Marshal.Copy(audioData, 0, output, bytesToCopy);

                    // 如果数据不足，剩余部分已经被零填充（防止杂音）
                    return StreamCallbackResult.Continue;
                }
                else
                {
                    // 没有更多数据，播放静音（零填充）
                    var silenceBuffer = new byte[frameCount * _channels * 2];
                    System.Runtime.InteropServices.Marshal.Copy(silenceBuffer, 0, output, silenceBuffer.Length);

                    _emptyFrameCount++;

                    // 如果连续播放静音超过阈值，保持继续但不立即停止
                    // 让定时器来处理播放完成的逻辑
                    return StreamCallbackResult.Continue;
                }
            }

            return StreamCallbackResult.Continue;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "音频播放回调错误");
            
            // 发生错误时填充静音以避免杂音
            if (output != IntPtr.Zero && frameCount > 0)
            {
                var silenceBuffer = new byte[frameCount * _channels * 2];
                System.Runtime.InteropServices.Marshal.Copy(silenceBuffer, 0, output, silenceBuffer.Length);
            }
            
            return StreamCallbackResult.Continue; // 继续而不是中止，避免音频流断开
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
            _playbackTimer?.Dispose();
            
            // 停止播放
            StopAsync().Wait(3000); // 3秒超时
            
            // 清理 PortAudio
            CleanupPortAudio();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "释放音频播放器资源时出现警告");
            
            // 即使停止失败，也要尝试清理资源
            lock (_lock)
            {
                if (_outputStream != null)
                {
                    try
                    {
                        // 尝试强制释放
                        _outputStream.Dispose();
                    }
                    catch (Exception disposeEx)
                    {
                        _logger?.LogWarning(disposeEx, "强制释放 Stream 时出现警告");
                    }
                    finally
                    {
                        _outputStream = null;
                        _isPlaying = false;
                    }
                }
            }
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }
}
