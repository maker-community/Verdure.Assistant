using PortAudioSharp;
using XiaoZhi.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace XiaoZhi.Core.Services;

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
    }
    public async Task InitializeAsync(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _channels = channels;

        try
        {
            // 初始化PortAudio
            PortAudio.Initialize();
            // 获取默认输出设备
            var defaultOutputDevice = PortAudio.DefaultOutputDevice;
            if (defaultOutputDevice == -1)
                throw new InvalidOperationException("未找到音频输出设备");

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
        }
        catch (Exception ex)
        {
            throw new Exception($"初始化音频播放器失败: {ex.Message}", ex);
        }
    }

    public async Task PlayAsync(byte[] audioData, int sampleRate, int channels)
    {
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
                _audioQueue.Enqueue(audioData);
                _lastDataTime = DateTime.Now; // 更新最后接收数据的时间
            }

            if (!_isPlaying && _outputStream != null)
            {
                _outputStream.Start();
                _isPlaying = true;

                // 启动定时器检测播放完成
                _playbackTimer.Change(500, 500); // 每500ms检查一次
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"播放音频时出错: {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        if (!_isPlaying) return;

        try
        {
            // 停止定时器
            _playbackTimer.Change(Timeout.Infinite, Timeout.Infinite);

            _outputStream?.Stop();
            _outputStream?.Close();
            _outputStream?.Dispose();
            _outputStream = null;

            PortAudio.Terminate();

            lock (_lock)
            {
                _audioQueue.Clear();
            }

            _isPlaying = false;

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"停止音频播放时出错: {ex.Message}");
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

                lock (_lock)
                {
                    if (_audioQueue.Count > 0)
                    {
                        audioData = _audioQueue.Dequeue();
                        _emptyFrameCount = 0; // 重置空帧计数
                    }
                }

                if (audioData != null)
                {
                    // 计算要复制的数据大小 (16位音频 = 2字节/样本)
                    int bytesToCopy = Math.Min(audioData.Length, (int)(frameCount * _channels * 2));

                    // 直接复制数据到输出缓冲区
                    System.Runtime.InteropServices.Marshal.Copy(audioData, 0, output, bytesToCopy);

                    // 如果数据不足，用静音填充剩余部分
                    if (bytesToCopy < frameCount * _channels * 2)
                    {
                        var remainingBytes = (int)(frameCount * _channels * 2) - bytesToCopy;
                        var silenceBuffer = new byte[remainingBytes];
                        System.Runtime.InteropServices.Marshal.Copy(
                            silenceBuffer, 0,
                            IntPtr.Add(output, bytesToCopy),
                            remainingBytes);
                    }

                    return StreamCallbackResult.Continue;
                }
                else
                {
                    // 没有更多数据，播放静音
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
            System.Console.WriteLine($"音频播放回调错误: {ex.Message}");
            return StreamCallbackResult.Continue; // 继续而不是中止，避免音频流断开
        }
    }

    public void Dispose()
    {
        _playbackTimer?.Dispose();
        StopAsync().Wait();
    }
}
