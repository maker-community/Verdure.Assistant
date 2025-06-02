using PortAudioSharp;
using Verdure.Assistant.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// 音频流管理器 - 参考 py-xiaozhi 的 AudioCodec 共享流模式
/// 提供共享的音频输入流，供关键词检测和语音录制使用
/// </summary>
public class AudioStreamManager : IAudioRecorder, IDisposable
{
    private static AudioStreamManager? _instance;
    private static readonly object _instanceLock = new();
    
    private PortAudioSharp.Stream? _sharedInputStream;
    private readonly object _streamLock = new();
    private readonly List<EventHandler<byte[]>> _dataSubscribers = new();
    private bool _isRecording = false;
    private bool _isDisposed = false;
    private int _sampleRate = 16000;
    private int _channels = 1;
    private readonly ILogger<AudioStreamManager>? _logger;

    // 参考 py-xiaozhi 的事件系统
    public event EventHandler<byte[]>? DataAvailable;
    public event EventHandler? RecordingStopped;

    public bool IsRecording => _isRecording;

    private AudioStreamManager(ILogger<AudioStreamManager>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取单例实例（参考 py-xiaozhi 的 AudioCodec 单例模式）
    /// </summary>
    public static AudioStreamManager GetInstance(ILogger<AudioStreamManager>? logger = null)
    {
        if (_instance == null)
        {
            lock (_instanceLock)
            {
                if (_instance == null)
                {
                    _instance = new AudioStreamManager(logger);
                }
            }
        }
        return _instance;
    }

    /// <summary>
    /// 获取共享输入流（对应 py-xiaozhi 的 audio_codec.input_stream）
    /// </summary>
    public PortAudioSharp.Stream? GetSharedInputStream()
    {
        lock (_streamLock)
        {
            return _sharedInputStream;
        }
    }

    /// <summary>
    /// 订阅音频数据（参考 py-xiaozhi 的多组件共享模式）
    /// </summary>
    public void SubscribeToAudioData(EventHandler<byte[]> handler)
    {
        lock (_streamLock)
        {
            if (!_dataSubscribers.Contains(handler))
            {
                _dataSubscribers.Add(handler);
                _logger?.LogInformation("新的音频数据订阅者已添加，当前订阅者数量: {Count}", _dataSubscribers.Count);
            }
        }
    }

    /// <summary>
    /// 取消订阅音频数据
    /// </summary>
    public void UnsubscribeFromAudioData(EventHandler<byte[]> handler)
    {
        lock (_streamLock)
        {
            _dataSubscribers.Remove(handler);
            _logger?.LogInformation("音频数据订阅者已移除，当前订阅者数量: {Count}", _dataSubscribers.Count);
        }
    }

    public async Task StartRecordingAsync(int sampleRate = 16000, int channels = 1)
    {
        if (_isRecording || _isDisposed) return;

        lock (_streamLock)
        {
            if (_isRecording) return;

            try
            {
                _sampleRate = sampleRate;
                _channels = channels;

                // 使用 PortAudioManager 确保正确初始化
                if (!PortAudioManager.Instance.AcquireReference())
                {
                    throw new InvalidOperationException("无法初始化 PortAudio");
                }

                // 获取默认输入设备
                var defaultInputDevice = PortAudio.DefaultInputDevice;
                if (defaultInputDevice == -1)
                    throw new InvalidOperationException("未找到音频输入设备");

                // 计算帧大小 (60ms帧，匹配Python配置)
                uint frameSize = (uint)(sampleRate * 60 / 1000);

                // 配置音频流参数
                var inputParameters = new StreamParameters
                {
                    device = defaultInputDevice,
                    channelCount = channels,
                    sampleFormat = SampleFormat.Int16,
                    suggestedLatency = PortAudio.GetDeviceInfo(defaultInputDevice).defaultLowInputLatency
                };

                // 创建共享输入流
                _sharedInputStream = new PortAudioSharp.Stream(
                    inputParameters,
                    null,
                    sampleRate,
                    frameSize,
                    StreamFlags.ClipOff,
                    OnAudioDataReceived,
                    IntPtr.Zero);

                // 开始录制
                _sharedInputStream.Start();
                _isRecording = true;

                _logger?.LogInformation("共享音频流启动成功: {SampleRate}Hz, {Channels}声道, 帧大小: {FrameSize}", 
                    sampleRate, channels, frameSize);
            }
            catch (Exception ex)
            {
                _isRecording = false;
                PortAudioManager.Instance.ReleaseReference();
                _logger?.LogError(ex, "启动共享音频流失败");
                throw new Exception($"启动共享音频流失败: {ex.Message}", ex);
            }
        }

        await Task.CompletedTask;
    }

    public async Task StopRecordingAsync()
    {
        if (!_isRecording) return;

        lock (_streamLock)
        {
            if (!_isRecording) return;

            try
            {
                _isRecording = false;

                // 停止并释放共享输入流
                if (_sharedInputStream != null)
                {
                    try
                    {
                        _sharedInputStream.Stop();
                        _sharedInputStream.Close();
                        _sharedInputStream.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "停止共享音频流时出现警告");
                    }
                    finally
                    {
                        _sharedInputStream = null;
                    }
                }

                // 释放 PortAudio 引用
                PortAudioManager.Instance.ReleaseReference();

                // 通知所有订阅者录制已停止
                RecordingStopped?.Invoke(this, EventArgs.Empty);
                _logger?.LogInformation("共享音频流已停止");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "停止共享音频流时出错");
            }
        }

        await Task.CompletedTask;
    }

    private StreamCallbackResult OnAudioDataReceived(
        IntPtr input,
        IntPtr output,
        uint frameCount,
        ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags,
        IntPtr userData)
    {
        if (_isDisposed || input == IntPtr.Zero) return StreamCallbackResult.Continue;

        try
        {
            // 计算音频数据大小
            int dataSize = (int)(frameCount * _channels * sizeof(short));
            byte[] audioData = new byte[dataSize];

            // 从非托管内存复制音频数据
            System.Runtime.InteropServices.Marshal.Copy(input, audioData, 0, dataSize);

            // 验证音频数据有效性
            if (IsValidAudioData(audioData))
            {
                // 分发给所有订阅者（参考 py-xiaozhi 的共享模式）
                lock (_streamLock)
                {
                    // 触发主要的 DataAvailable 事件
                    DataAvailable?.Invoke(this, audioData);

                    // 通知所有额外的订阅者
                    foreach (var subscriber in _dataSubscribers.ToList())
                    {
                        try
                        {
                            subscriber?.Invoke(this, audioData);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "音频数据订阅者处理时出错");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理音频数据时出错");
        }

        return StreamCallbackResult.Continue;
    }

    private bool IsValidAudioData(byte[] audioData)
    {
        if (audioData == null || audioData.Length == 0)
            return false;

        // 简单的音频数据验证
        bool hasNonZero = false;
        for (int i = 0; i < Math.Min(audioData.Length, 100); i++)
        {
            if (audioData[i] != 0)
            {
                hasNonZero = true;
                break;
            }
        }

        return hasNonZero;
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        lock (_streamLock)
        {
            if (_isDisposed) return;

            _isDisposed = true;

            try
            {
                StopRecordingAsync().Wait(3000);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Dispose 时停止录制出错");
            }

            _dataSubscribers.Clear();
            _logger?.LogInformation("AudioStreamManager 已释放");
        }
    }
}
