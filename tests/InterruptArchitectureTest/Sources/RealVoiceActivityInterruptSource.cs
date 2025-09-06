using Microsoft.Extensions.Logging;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Extensions.WebRtc.Apm;
using SoundFlow.Providers;
using SoundFlow.Structs;
using System.Collections.Concurrent;

namespace InterruptArchitectureTest.Sources;

/// <summary>
/// 基于SoundFlow的真实语音活动检测源
/// 参考SoundFlow.Samples.VoiceInterruption的VAD实现和SoundFlowRecordingCodecTest的录音编码
/// </summary>
public class RealVoiceActivityInterruptSource : Core.InterruptSourceBase
{
    private readonly AudioEngine _engine;
    private AudioCaptureDevice? _captureDevice;
    private Recorder? _recorder;
    private VoiceActivityDetector? _vad;
    private readonly object _vadLock = new();
    
    // VAD配置参数
    private readonly VadConfiguration _vadConfig;
    
    // 音频统计
    private int _audioFrameCount = 0;
    private int _vadTriggerCount = 0;
    private DateTime _lastAudioFrameTime = DateTime.Now;
    private DateTime _lastVadTriggerTime = DateTime.Now;
    private bool _lastVadState = false;
    
    // 最优化的音频格式配置 (参考SoundFlowRecordingCodecTest)
    private static readonly AudioFormat OptimalFormat = new()
    {
        Format = SampleFormat.S16,  // 使用S16接近Int16目标格式
        Channels = 1,               // 单声道，匹配目标
        SampleRate = 16000          // 16kHz，匹配目标采样率
    };
    
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

    public class VadConfiguration
    {
        public float EnergyThreshold { get; set; } = 0.001f;      // 能量阈值
        public float ActivationTimeMs { get; set; } = 100f;       // 激活确认时间
        public float HangoverTimeMs { get; set; } = 500f;         // 保持时间
        public int SpeechLowBand { get; set; } = 200;             // 人声频带下限
        public int SpeechHighBand { get; set; } = 4000;           // 人声频带上限
        public int FftSize { get; set; } = 1024;                  // FFT大小
        public bool DebugOutput { get; set; } = false;            // 调试输出
    }

    public RealVoiceActivityInterruptSource(
        VadConfiguration? vadConfig = null, 
        ILogger<RealVoiceActivityInterruptSource>? logger = null)
        : base("RealVAD", Core.InterruptTypes.VoiceActivity, logger)
    {
        _engine = new MiniAudioEngine();
        _vadConfig = vadConfig ?? new VadConfiguration();
    }

    /// <summary>
    /// 获取VAD配置，允许外部调整参数
    /// </summary>
    public VadConfiguration Configuration => _vadConfig;

    /// <summary>
    /// 获取音频统计信息
    /// </summary>
    public AudioStatistics GetStatistics()
    {
        return new AudioStatistics
        {
            AudioFrameCount = _audioFrameCount,
            VadTriggerCount = _vadTriggerCount,
            LastAudioFrameTime = _lastAudioFrameTime,
            LastVadTriggerTime = _lastVadTriggerTime,
            CurrentVadState = _lastVadState,
            IsRecording = _recorder?.State == PlaybackState.Playing
        };
    }

    public class AudioStatistics
    {
        public int AudioFrameCount { get; set; }
        public int VadTriggerCount { get; set; }
        public DateTime LastAudioFrameTime { get; set; }
        public DateTime LastVadTriggerTime { get; set; }
        public bool CurrentVadState { get; set; }
        public bool IsRecording { get; set; }
    }

    protected override async Task OnStartAsync()
    {
        _logger?.LogInformation("Initializing real VAD interrupt source...");
        
        try
        {
            // 初始化音频系统
            await InitializeAudioSystem();
            
            // 设置VAD处理
            SetupVoiceActivityDetection();
            
            // 开始录音
            StartRecording();
            
            _logger?.LogInformation("Real VAD interrupt source initialized successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize real VAD interrupt source");
            await CleanupResources();
            throw;
        }
    }

    protected override async Task OnStopAsync()
    {
        _logger?.LogInformation("Stopping real VAD interrupt source...");
        await CleanupResources();
    }

    protected override Task MonitoringLoopAsync()
    {
        _logger?.LogInformation("Real VAD monitoring started");

        // 启动音频监控任务
        _ = Task.Run(async () =>
        {
            int monitorCount = 0;
            while (!_cancellationTokenSource.Token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    await Task.Delay(1000, _cancellationTokenSource.Token);
                    monitorCount++;
                    
                    // 每10秒输出一次统计信息
                    if (_vadConfig.DebugOutput && monitorCount % 10 == 0)
                    {
                        var stats = GetStatistics();
                        _logger?.LogDebug("VAD Statistics - Running: {RunningTime}s, Frames: {Frames}, VAD Triggers: {Triggers}",
                            monitorCount, stats.AudioFrameCount, stats.VadTriggerCount);
                        
                        // 检查是否长时间没有音频活动
                        var timeSinceLastAudio = DateTime.Now - stats.LastAudioFrameTime;
                        if (timeSinceLastAudio.TotalSeconds > 10)
                        {
                            _logger?.LogWarning("No audio activity detected for {Seconds} seconds", timeSinceLastAudio.TotalSeconds);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in VAD monitoring loop");
                }
            }
        }, _cancellationTokenSource.Token);

        return Task.CompletedTask;
    }

    private async Task InitializeAudioSystem()
    {
        try
        {
            _engine.UpdateDevicesInfo();
            
            if (_vadConfig.DebugOutput)
            {
                _logger?.LogDebug("Available capture devices:");
                for (int i = 0; i < _engine.CaptureDevices.Length; i++)
                {
                    var device = _engine.CaptureDevices[i];
                    var marker = device.IsDefault ? " (Default)" : "";
                    _logger?.LogDebug("  [{Index}] {Name}{Marker}", i, device.Name, marker);
                }
            }
            
            // 使用默认录音设备，配置为最优格式
            _captureDevice = _engine.InitializeCaptureDevice(null, OptimalFormat, DeviceConfig);
            
            _logger?.LogInformation("Audio device initialized: {Name}", _captureDevice.Info?.Name ?? "Default Device");
            _logger?.LogInformation("Audio format: {Format}, {Channels}ch, {SampleRate}Hz", 
                _captureDevice.Format.Format, _captureDevice.Format.Channels, _captureDevice.Format.SampleRate);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize audio system");
            throw;
        }
    }

    private void SetupVoiceActivityDetection()
    {
        try
        {
            if (_captureDevice == null)
            {
                throw new InvalidOperationException("Capture device not initialized");
            }

            // 创建VAD检测器 (参考SoundFlow.Samples.VoiceInterruption)
            _vad = new VoiceActivityDetector(
                format: _captureDevice.Format,
                fftSize: _vadConfig.FftSize,
                energyThreshold: _vadConfig.EnergyThreshold
            );

            // 配置VAD参数
            _vad.ActivationTimeMs = _vadConfig.ActivationTimeMs;
            _vad.HangoverTimeMs = _vadConfig.HangoverTimeMs;
            _vad.SpeechLowBand = _vadConfig.SpeechLowBand;
            _vad.SpeechHighBand = _vadConfig.SpeechHighBand;

            if (_vadConfig.DebugOutput)
            {
                _logger?.LogDebug("VAD Configuration:");
                _logger?.LogDebug("  Energy Threshold: {Threshold}", _vad.EnergyThreshold);
                _logger?.LogDebug("  Activation Time: {ActivationTime}ms", _vad.ActivationTimeMs);
                _logger?.LogDebug("  Hangover Time: {HangoverTime}ms", _vad.HangoverTimeMs);
                _logger?.LogDebug("  Speech Band: {LowBand}Hz - {HighBand}Hz", _vad.SpeechLowBand, _vad.SpeechHighBand);
                _logger?.LogDebug("  FFT Size: {FftSize}", _vadConfig.FftSize);
            }

            // 绑定VAD事件 (参考SoundFlow.Samples.VoiceInterruption的事件处理)
            _vad.SpeechDetected += OnVoiceActivityDetected;

            _logger?.LogInformation("VAD detector configured successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to setup voice activity detection");
            throw;
        }
    }

    private void StartRecording()
    {
        try
        {
            if (_captureDevice == null)
            {
                throw new InvalidOperationException("Capture device not initialized");
            }

            // 创建录音器，使用内存流进行实时处理 (参考SoundFlow.Samples.VoiceInterruption)
            var memoryStream = new MemoryStream();
            _recorder = new Recorder(_captureDevice, memoryStream);

            // 添加VAD分析器
            if (_vad != null)
            {
                _recorder.AddAnalyzer(_vad);
            }

            // 开始录音
            _recorder.StartRecording();
            _captureDevice.Start();

            _logger?.LogInformation("Recording started successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start recording");
            throw;
        }
    }

    /// <summary>
    /// VAD事件处理 (参考SoundFlow.Samples.VoiceInterruption的OnVoiceActivityDetected)
    /// </summary>
    private void OnVoiceActivityDetected(bool isVoiceActive)
    {
        lock (_vadLock)
        {
            _vadTriggerCount++;
            _lastVadTriggerTime = DateTime.Now;
            _lastVadState = isVoiceActive;
            
            if (_vadConfig.DebugOutput)
            {
                _logger?.LogDebug("VAD Event #{Count} - Voice Active: {IsActive} (Time: {Time:HH:mm:ss.fff})",
                    _vadTriggerCount, isVoiceActive, DateTime.Now);
            }
            
            // 只在检测到人声活动时触发打断事件
            if (isVoiceActive && !_isPaused && IsEnabled)
            {
                var confidence = CalculateVoiceConfidence();
                
                TriggerInterrupt(
                    $"Voice activity detected with confidence {confidence:P1}",
                    new 
                    { 
                        IsVoiceActive = isVoiceActive,
                        Confidence = confidence,
                        TriggerCount = _vadTriggerCount,
                        AudioFrameCount = _audioFrameCount,
                        VadConfig = new
                        {
                            _vadConfig.EnergyThreshold,
                            _vadConfig.ActivationTimeMs,
                            _vadConfig.HangoverTimeMs,
                            _vadConfig.SpeechLowBand,
                            _vadConfig.SpeechHighBand
                        }
                    },
                    priority: 7 // 高优先级，因为是实时语音检测
                );
            }
        }
    }

    /// <summary>
    /// 计算语音置信度 (简单实现)
    /// </summary>
    private float CalculateVoiceConfidence()
    {
        // 基于触发频率和配置参数计算置信度
        var baseConfidence = 0.7f;
        
        // 根据能量阈值调整置信度
        if (_vadConfig.EnergyThreshold <= 0.001f)
            baseConfidence += 0.1f;
        else if (_vadConfig.EnergyThreshold >= 0.01f)
            baseConfidence -= 0.1f;
            
        // 根据激活时间调整置信度
        if (_vadConfig.ActivationTimeMs <= 100f)
            baseConfidence += 0.1f;
        else if (_vadConfig.ActivationTimeMs >= 300f)
            baseConfidence -= 0.1f;
            
        return Math.Max(0.5f, Math.Min(1.0f, baseConfidence));
    }

    /// <summary>
    /// 动态调整VAD参数
    /// </summary>
    public void UpdateVadConfiguration(Action<VadConfiguration> configUpdater)
    {
        if (_vad == null) return;
        
        lock (_vadLock)
        {
            configUpdater(_vadConfig);
            
            // 更新VAD参数
            _vad.EnergyThreshold = _vadConfig.EnergyThreshold;
            _vad.ActivationTimeMs = _vadConfig.ActivationTimeMs;
            _vad.HangoverTimeMs = _vadConfig.HangoverTimeMs;
            _vad.SpeechLowBand = _vadConfig.SpeechLowBand;
            _vad.SpeechHighBand = _vadConfig.SpeechHighBand;
            
            _logger?.LogInformation("VAD configuration updated");
        }
    }

    private Task CleanupResources()
    {
        try
        {
            // 停止录音
            if (_recorder != null)
            {
                _recorder.StopRecording();
                _recorder.Dispose();
                _recorder = null;
            }

            // 停止设备
            if (_captureDevice != null)
            {
                _captureDevice.Stop();
                _captureDevice.Dispose();
                _captureDevice = null;
            }

            // 清理VAD
            if (_vad != null)
            {
                _vad.SpeechDetected -= OnVoiceActivityDetected;
                _vad = null;
            }

            // 清理引擎
            _engine?.Dispose();

            _logger?.LogInformation("Real VAD resources cleaned up successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during resource cleanup");
        }
        
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            CleanupResources().Wait();
        }
        base.Dispose(disposing);
    }
}
