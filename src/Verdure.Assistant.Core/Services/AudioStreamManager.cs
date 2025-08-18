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
    private bool _isCleaningUp = false; // 添加清理状态标志
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
        if (_isDisposed) return;

        lock (_streamLock)
        {
            // 如果正在录制且参数相同，直接返回
            if (_isRecording && _sampleRate == sampleRate && _channels == channels && _sharedInputStream != null)
            {
                _logger?.LogDebug("音频流已在运行，参数相同，跳过启动");
                return;
            }

            // 如果有不同参数的录制在进行，或者有残留的流对象，先清理
            if (_isRecording || _sharedInputStream != null)
            {
                _logger?.LogDebug("检测到现有音频流（参数不同或状态不一致），先进行清理");
                CleanupStreamInternal();
            }

            try
            {
                _sampleRate = sampleRate;
                _channels = channels;

                // 使用 PortAudioManager 确保正确初始化
                if (!PortAudioManager.Instance.AcquireReference())
                {
                    throw new InvalidOperationException("无法初始化 PortAudio");
                }

                _logger?.LogDebug("创建新的音频流，采样率: {SampleRate}Hz, 声道: {Channels}", sampleRate, channels);

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

    /// <summary>
    /// 内部清理流资源的方法（在锁内调用）
    /// 优化版本：增强异常处理、防止重复清理、状态检查
    /// </summary>
    private void CleanupStreamInternal()
    {
        // 防止重复清理
        if (_isCleaningUp)
        {
            _logger?.LogDebug("清理操作正在进行中，跳过重复调用");
            return;
        }

        try
        {
            _isCleaningUp = true; // 设置清理标志
            _isRecording = false;

            if (_sharedInputStream != null)
            {
                var streamToCleanup = _sharedInputStream;
                _sharedInputStream = null; // 立即置空以防止重复清理

                try
                {
                    _logger?.LogDebug("开始清理音频流...");
                    
                    // 检查流状态，只有在运行状态下才需要停止
                    bool needsStop = false;
                    try
                    {
                        // 使用 IsActive 属性检查流状态
                        needsStop = streamToCleanup.IsActive;
                    }
                    catch (Exception checkEx)
                    {
                        _logger?.LogDebug(checkEx, "检查流状态时出错，假设需要停止");
                        needsStop = true; // 保守处理，假设需要停止
                    }

                    if (needsStop)
                    {
                        // 使用更长超时和分步清理机制
                        var cleanupTask = Task.Run(() =>
                        {
                            try
                            {
                                // 分步清理：先停止，再关闭，最后释放
                                _logger?.LogDebug("正在停止音频流...");
                                streamToCleanup.Stop();
                                
                                _logger?.LogDebug("正在关闭音频流...");
                                streamToCleanup.Close();
                                
                                _logger?.LogDebug("正在释放音频流...");
                                streamToCleanup.Dispose();
                            }
                            catch (PortAudioException paEx)
                            {
                                _logger?.LogWarning(paEx, "PortAudio 流停止时出现已知异常（可能是正常的资源释放）");
                                // 对于 PortAudio 异常，仍然尝试释放资源
                                try
                                {
                                    streamToCleanup.Dispose();
                                }
                                catch (Exception disposeEx)
                                {
                                    _logger?.LogDebug(disposeEx, "强制释放流资源时出错");
                                }
                            }
                        });
                        
                        var completed = cleanupTask.Wait(5000); // 增加到5秒超时
                        
                        if (!completed)
                        {
                            _logger?.LogWarning("清理音频流超时，将进行强制清理");
                            // 超时情况下的强制清理
                            try
                            {
                                GC.SuppressFinalize(streamToCleanup);
                                _logger?.LogDebug("已抑制音频流终结器");
                            }
                            catch (Exception suppressEx)
                            {
                                _logger?.LogDebug(suppressEx, "抑制终结器时出错");
                            }
                        }
                        else
                        {
                            _logger?.LogDebug("音频流清理完成");
                        }
                    }
                    else
                    {
                        _logger?.LogDebug("音频流未处于活动状态，直接释放");
                        try
                        {
                            streamToCleanup.Dispose();
                        }
                        catch (Exception disposeEx)
                        {
                            _logger?.LogDebug(disposeEx, "释放非活动流时出错");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "清理音频流时出现警告，继续执行清理流程");
                    // 即使出错也要尝试强制释放
                    try
                    {
                        streamToCleanup.Dispose();
                    }
                    catch (Exception forceEx)
                    {
                        _logger?.LogDebug(forceEx, "强制释放流时出错");
                    }
                }

                // 延迟释放 PortAudio 引用，确保流完全释放
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(100); // 短暂延迟确保资源释放完毕
                        PortAudioManager.Instance.ReleaseReference();
                        _logger?.LogDebug("已释放 PortAudio 引用");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "释放 PortAudio 引用时出错");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "清理流资源时出现严重错误");
            // 确保状态重置
            _isRecording = false;
            _sharedInputStream = null;
        }
        finally
        {
            _isCleaningUp = false; // 清理完成，重置标志
        }
    }

    public async Task StopRecordingAsync()
    {
        if (!_isRecording) return;

        // 使用超时机制避免在树莓派等平台上卡死
        var stopTask = Task.Run(() => StopRecordingInternal());
        var timeoutTask = Task.Delay(5000); // 5秒超时

        var completedTask = await Task.WhenAny(stopTask, timeoutTask);
        
        if (completedTask == timeoutTask)
        {
            _logger?.LogWarning("停止音频录制超时，强制设置状态");
            // 超时情况下，强制清理状态
            lock (_streamLock)
            {
                _isRecording = false;
                _sharedInputStream = null;
            }
            
            // 尝试释放 PortAudio 引用（即使可能失败）
            try
            {
                PortAudioManager.Instance.ReleaseReference();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "超时情况下释放 PortAudio 引用失败");
            }
            
            // 仍然通知订阅者
            try
            {
                RecordingStopped?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "通知订阅者时出错");
            }
        }
        else
        {
            // 正常完成，等待任务结果
            await stopTask;
        }
    }

    private void StopRecordingInternal()
    {
        lock (_streamLock)
        {
            if (!_isRecording) return;

            try
            {
                _logger?.LogDebug("开始停止共享音频流...");
                CleanupStreamInternal();

                // 通知所有订阅者录制已停止
                RecordingStopped?.Invoke(this, EventArgs.Empty);
                _logger?.LogInformation("共享音频流已停止");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "停止共享音频流时出错");
            }
        }
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
