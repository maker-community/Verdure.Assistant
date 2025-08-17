using PortAudioSharp;
using Verdure.Assistant.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// PortAudioSharp2实现的音频播放器
/// </summary>
public class PortAudioPlayer : IAudioPlayer
{    
    private PortAudioSharp.Stream? _outputStream;
    private bool _isPlaying;
    private readonly Queue<byte[]> _audioQueue = new();
    private readonly object _lock = new();
    private int _sampleRate;
    private int _channels;
    private int _emptyFrameCount = 0; // 空帧计数器
    private const int MaxEmptyFrames = 50; // 最大空帧数（约1秒的静音后停止）
    private DateTime _lastDataTime = DateTime.Now;
    private readonly Timer _playbackTimer;
    private readonly ILogger<PortAudioPlayer>? _logger;
    private bool _isDisposed = false;
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
                            System.Console.WriteLine($"Error in playback completion handler: {ex.Message}");
                        }
                    });
                }
            }
        }
    }    public async Task InitializeAsync(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _channels = channels;

        try
        {
            // 使用 PortAudioManager 确保正确初始化
            if (!PortAudioManager.Instance.AcquireReference())
            {
                throw new InvalidOperationException("无法初始化 PortAudio");
            }
            
            // 获取默认输出设备
            var defaultOutputDevice = PortAudio.DefaultOutputDevice;            if (defaultOutputDevice == -1)
            {
                PortAudioManager.Instance.ReleaseReference();
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

            // 计算正确的帧大小 - 匹配Python的OUTPUT_FRAME_SIZE
            int frameSize = sampleRate * 60 / 1000; // 60ms帧，匹配Python的FRAME_DURATION

            // 创建输出流
            _outputStream = new PortAudioSharp.Stream(
                null,
                outputParameters,
                sampleRate,
                (uint)frameSize, // 使用计算出的帧大小
                StreamFlags.ClipOff, // 使用ClipOff匹配其他实现
                OnAudioDataRequested,
                IntPtr.Zero);

            System.Console.WriteLine($"音频播放器初始化成功: {sampleRate}Hz, {channels}声道, 帧大小: {frameSize}");
            await Task.CompletedTask;
        }        catch (Exception ex)
        {
            PortAudioManager.Instance.ReleaseReference();
            throw new Exception($"初始化音频播放器失败: {ex.Message}", ex);
        }
    }    public async Task PlayAsync(byte[] audioData, int sampleRate, int channels)
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
            Console.WriteLine($"播放音频时出错: {ex.Message}");
        }
    }    public async Task StopAsync()
    {
        if (!_isPlaying) return;

        try
        {
            // 停止定时器
            _playbackTimer.Change(Timeout.Infinite, Timeout.Infinite);

            _isPlaying = false;            // 安全停止音频流
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

            // 释放 PortAudio 引用
            PortAudioManager.Instance.ReleaseReference();

            _logger?.LogInformation("音频播放已停止");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止音频播放时出错");
            Console.WriteLine($"停止音频播放时出错: {ex.Message}");
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
            Console.WriteLine($"音频播放回调错误: {ex.Message}");
            
            // 发生错误时填充静音以避免杂音
            if (output != IntPtr.Zero && frameCount > 0)
            {
                var silenceBuffer = new byte[frameCount * _channels * 2];
                System.Runtime.InteropServices.Marshal.Copy(silenceBuffer, 0, output, silenceBuffer.Length);
            }
            
            return StreamCallbackResult.Continue; // 继续而不是中止，避免音频流断开
        }
    }    
    public void Dispose()
    {
        if (_isDisposed) return;
        
        _isDisposed = true;
        
        try
        {
            _playbackTimer?.Dispose();
            StopAsync().Wait(5000); // 5秒超时
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
                
                // 确保释放 PortAudio 引用
                try
                {
                    PortAudioManager.Instance.ReleaseReference();
                }
                catch (Exception releaseEx)
                {
                    _logger?.LogWarning(releaseEx, "Dispose 时释放 PortAudio 引用出现警告");
                }
            }
        }
        finally
        {
            // 抑制终结器调用，避免双重释放
            GC.SuppressFinalize(this);
        }
    }
}
