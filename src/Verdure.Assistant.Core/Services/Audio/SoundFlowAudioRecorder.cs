using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Structs;
using Verdure.Assistant.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// SoundFlow实现的共享音频录制器
/// 基于SoundFlowRecordingCodecTest的最优配置实现
/// 提供与AudioStreamManager相同的接口和功能
/// </summary>
public class SoundFlowAudioRecorder : ISharedAudioRecorder, IDisposable
{
    private static SoundFlowAudioRecorder? _instance;
    private static readonly object _instanceLock = new();
    
    private AudioEngine? _engine;
    private AudioCaptureDevice? _captureDevice;
    private Recorder? _recorder;
    private readonly object _streamLock = new();
    private readonly List<EventHandler<byte[]>> _dataSubscribers = new();
    private bool _isRecording = false;
    private bool _isDisposed = false;
    private int _sampleRate = 16000;
    private int _channels = 1;
    private readonly ILogger<SoundFlowAudioRecorder>? _logger;

    // 设备配置 - 优化为低延迟录音
    private static readonly MiniAudioDeviceConfig DeviceConfig = new()
    {
        PeriodSizeInFrames = 960,   // 60ms @ 16kHz = 960 samples
        PeriodSizeInMilliseconds = 0,
        Periods = 3,
        NoPreSilencedOutputBuffer = true,
        NoClip = false,
        NoDisableDenormals = false,
        NoFixedSizedCallback = false,
        Capture = new DeviceSubConfig 
        { 
            ShareMode = ShareMode.Shared 
        },
        Wasapi = new WasapiSettings 
        { 
            Usage = WasapiUsage.ProAudio,
            NoAutoConvertSRC = false,     // 允许自动采样率转换
            NoDefaultQualitySRC = false,  // 允许高质量重采样
            NoAutoStreamRouting = false,
            NoHardwareOffloading = false
        }
    };

    // 参考 py-xiaozhi 的事件系统
    public event EventHandler<byte[]>? DataAvailable;
    public event EventHandler? RecordingStopped;

    public bool IsRecording => _isRecording;

    private SoundFlowAudioRecorder(ILogger<SoundFlowAudioRecorder>? logger = null)
    {
        _logger = logger;
        InitializeAudioEngine();
    }

    /// <summary>
    /// 在构造函数中初始化音频引擎和基础组件
    /// </summary>
    private void InitializeAudioEngine()
    {
        try
        {
            // 在构造时就初始化引擎
            _engine = new MiniAudioEngine();
            
            // 显示可用的录音设备（调试模式）
            if (_logger != null && _logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("SoundFlow录音引擎初始化完成");
                _logger.LogDebug("可用SoundFlow录音设备:");
                for (int i = 0; i < _engine.CaptureDevices.Length; i++)
                {
                    var device = _engine.CaptureDevices[i];
                    var marker = device.IsDefault ? " (默认)" : "";
                    _logger.LogDebug("  [{Index}] {Name}{Marker}", i, device.Name, marker);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "初始化SoundFlow录音引擎失败");
            throw;
        }
    }

    /// <summary>
    /// 获取单例实例（参考 AudioStreamManager 的单例模式）
    /// </summary>
    public static SoundFlowAudioRecorder GetInstance(ILogger<SoundFlowAudioRecorder>? logger = null)
    {
        if (_instance == null)
        {
            lock (_instanceLock)
            {
                if (_instance == null)
                {
                    _instance = new SoundFlowAudioRecorder(logger);
                }
            }
        }
        return _instance;
    }

    /// <summary>
    /// 订阅音频数据（参考 AudioStreamManager 的多组件共享模式）
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
            // 智能检查：如果正在录制且参数相同，直接返回
            if (_isRecording && _sampleRate == sampleRate && _channels == channels && _captureDevice != null)
            {
                _logger?.LogDebug("SoundFlow音频流已在运行，参数相同，跳过启动");
                return;
            }

            // 只有在参数不同或状态不一致时才清理设备
            if (_isRecording || _captureDevice != null)
            {
                _logger?.LogDebug("检测到现有SoundFlow音频流（参数不同或状态不一致），先进行清理");
                CleanupStreamInternal();
            }

            try
            {
                _sampleRate = sampleRate;
                _channels = channels;

                _logger?.LogDebug("创建新的SoundFlow音频设备，采样率: {SampleRate}Hz, 声道: {Channels}", sampleRate, channels);

                // 引擎已在构造函数中初始化，直接使用
                if (_engine == null)
                {
                    throw new InvalidOperationException("SoundFlow引擎未正确初始化");
                }

                // 使用指定格式初始化录音设备
                var format = new AudioFormat
                {
                    Format = SampleFormat.S16,
                    Channels = channels,
                    SampleRate = sampleRate
                };

                _captureDevice = _engine.InitializeCaptureDevice(null, format, DeviceConfig);

                _logger?.LogDebug("已选择SoundFlow设备: {DeviceName}", _captureDevice.Info?.Name ?? "默认设备");
                _logger?.LogDebug("设备格式: {Format}, {Channels}ch, {SampleRate}Hz", 
                    _captureDevice.Format.Format, _captureDevice.Format.Channels, _captureDevice.Format.SampleRate);

                // 创建录音器，使用回调方式处理音频数据
                _recorder = new Recorder(_captureDevice, ProcessAudioData);
                
                // 开始录制
                _recorder.StartRecording();
                _captureDevice.Start();
                _isRecording = true;

                _logger?.LogInformation("SoundFlow共享音频流启动成功: {SampleRate}Hz, {Channels}声道", 
                    sampleRate, channels);
            }
            catch (Exception ex)
            {
                _isRecording = false;
                _logger?.LogError(ex, "启动SoundFlow共享音频流失败");
                throw new Exception($"启动SoundFlow共享音频流失败: {ex.Message}", ex);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 音频数据处理回调 - 核心转码逻辑
    /// 基于SoundFlowRecordingCodecTest的实现，确保与现有系统兼容
    /// </summary>
    private void ProcessAudioData(Span<float> samples, Capability capability)
    {
        if (_isDisposed || samples.Length == 0) return;

        try
        {
            // 1. SoundFlow输出的是F32格式，需要转换为Int16
            var sampleCount = samples.Length;
            var int16Samples = new short[sampleCount];
            
            // F32 → Int16 转换 (核心转码逻辑)
            for (int i = 0; i < sampleCount; i++)
            {
                // 限制范围到 [-1.0, 1.0] 并转换为 Int16
                var clampedSample = Math.Max(-1.0f, Math.Min(1.0f, samples[i]));
                int16Samples[i] = (short)(clampedSample * short.MaxValue);
            }

            // 2. 转换为byte[]格式 (匹配AudioStreamManager.OnAudioDataReceived的输出)
            var audioDataBytes = new byte[sampleCount * 2]; // 2 bytes per Int16
            for (int i = 0; i < sampleCount; i++)
            {
                var bytes = BitConverter.GetBytes(int16Samples[i]);
                audioDataBytes[i * 2] = bytes[0];
                audioDataBytes[i * 2 + 1] = bytes[1];
            }

            // 验证音频数据有效性
            if (IsValidAudioData(audioDataBytes))
            {
                // 分发给所有订阅者（参考 AudioStreamManager 的共享模式）
                lock (_streamLock)
                {
                    // 触发主要的 DataAvailable 事件
                    DataAvailable?.Invoke(this, audioDataBytes);

                    // 通知所有额外的订阅者
                    foreach (var subscriber in _dataSubscribers.ToList())
                    {
                        try
                        {
                            subscriber?.Invoke(this, audioDataBytes);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "SoundFlow音频数据订阅者处理时出错");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SoundFlow处理音频数据时出错");
        }
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

    /// <summary>
    /// 内部清理流资源的方法（在锁内调用）
    /// </summary>
    private void CleanupStreamInternal()
    {
        try
        {
            _isRecording = false;

            if (_recorder != null)
            {
                try
                {
                    _recorder.StopRecording();
                    _recorder = null;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "停止SoundFlow录音器时出错");
                }
            }

            if (_captureDevice != null)
            {
                try
                {
                    _captureDevice.Stop();
                    _captureDevice = null;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "停止SoundFlow捕获设备时出错");
                }
            }

            if (_engine != null)
            {
                try
                {
                    _engine.Dispose();
                    _engine = null;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "释放SoundFlow引擎时出错");
                }
            }

            _logger?.LogDebug("SoundFlow音频流清理完成");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SoundFlow清理流资源时出现严重错误");
            // 确保状态重置
            _isRecording = false;
            _captureDevice = null;
            _recorder = null;
            _engine = null;
        }
    }

    public async Task StopRecordingAsync()
    {
        if (!_isRecording) return;

        var timeout = Environment.ProcessorCount <= 4 ? 3000 : 5000;
        var stopTask = Task.Run(() => StopRecordingInternal());
        var timeoutTask = Task.Delay(timeout);

        var completedTask = await Task.WhenAny(stopTask, timeoutTask);
        
        if (completedTask == timeoutTask)
        {
            _logger?.LogWarning("停止SoundFlow音频录制超时，强制设置状态");
            // 超时情况下，强制清理状态
            lock (_streamLock)
            {
                _isRecording = false;
                _captureDevice = null;
                _recorder = null;
                _engine = null;
            }
            
            // 仍然通知订阅者
            try
            {
                RecordingStopped?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "通知SoundFlow订阅者时出错");
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
                _logger?.LogDebug("开始停止SoundFlow共享音频流...");
                CleanupStreamInternal();

                // 通知所有订阅者录制已停止
                RecordingStopped?.Invoke(this, EventArgs.Empty);
                _logger?.LogInformation("SoundFlow共享音频流已停止");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "停止SoundFlow共享音频流时出错");
            }
        }
    }

    /// <summary>
    /// 强制清理音频系统（用于全局异常恢复）
    /// </summary>
    public void ForceCleanup()
    {
        try
        {
            _logger?.LogWarning("执行SoundFlow强制音频系统清理...");
            
            lock (_streamLock)
            {
                // 强制重置所有状态
                _isRecording = false;
                
                // 清理订阅者
                var subscriberCount = _dataSubscribers.Count;
                _dataSubscribers.Clear();
                
                // 强制清理流
                if (_recorder != null || _captureDevice != null || _engine != null)
                {
                    try
                    {
                        // 尝试快速清理
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                _recorder?.StopRecording();
                                _captureDevice?.Stop();
                                _engine?.Dispose();
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogDebug(ex, "SoundFlow强制清理时的预期异常");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "SoundFlow强制清理时的预期异常");
                    }
                    finally
                    {
                        _recorder = null;
                        _captureDevice = null;
                        _engine = null;
                    }
                }
                
                _logger?.LogWarning("SoundFlow强制清理完成，已清理 {Count} 个订阅者", subscriberCount);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SoundFlow强制清理过程中出现错误");
        }
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
                _logger?.LogWarning(ex, "SoundFlow Dispose 时停止录制出错");
            }

            _dataSubscribers.Clear();
            
            // 在最后一个组件释放时清理引擎
            if (_instance == this)
            {
                try
                {
                    _engine?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "释放SoundFlow引擎时出错");
                }
            }
            
            _logger?.LogInformation("SoundFlowAudioRecorder 已释放");
        }
    }
}
