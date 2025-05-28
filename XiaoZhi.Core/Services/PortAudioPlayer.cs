using PortAudioSharp;
using XiaoZhi.Core.Interfaces;

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

    public event EventHandler? PlaybackStopped;

    public bool IsPlaying => _isPlaying;    
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

            // 配置音频流参数
            var outputParameters = new StreamParameters
            {
                device = defaultOutputDevice,
                channelCount = channels,
                sampleFormat = SampleFormat.Int16,
                suggestedLatency = PortAudio.GetDeviceInfo(defaultOutputDevice).defaultLowOutputLatency
            };

            // 创建输出流
            _outputStream = new PortAudioSharp.Stream(
                null,
                outputParameters,
                sampleRate,
                1024, // 帧大小
                StreamFlags.NoFlag,
                OnAudioDataRequested,
                IntPtr.Zero);

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
            }

            if (!_isPlaying && _outputStream != null)
            {
                _outputStream.Start();
                _isPlaying = true;
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"播放音频时出错: {ex.Message}");
        }
    }    public async Task StopAsync()
    {
        if (!_isPlaying) return;

        try
        {
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
            PlaybackStopped?.Invoke(this, EventArgs.Empty);

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
                    }
                }

                if (audioData != null)
                {
                    // 计算要复制的数据大小
                    int bytesToCopy = Math.Min(audioData.Length, (int)(frameCount * _channels * 2));
                    
                    // 复制数据到输出缓冲区
                    System.Runtime.InteropServices.Marshal.Copy(audioData, 0, output, bytesToCopy);

                    // 如果数据不足，用静音填充
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
                    
                    // 如果队列为空，停止播放
                    Task.Run(async () => await StopAsync());
                    return StreamCallbackResult.Complete;
                }
            }

            return StreamCallbackResult.Continue;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"音频播放回调错误: {ex.Message}");
            return StreamCallbackResult.Abort;
        }
    }

    public void Dispose()
    {
        StopAsync().Wait();
    }
}
