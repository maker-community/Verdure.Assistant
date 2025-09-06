using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using System.Collections.Generic;
using System.Linq;

namespace Verdure.Assistant.Core.Services.Interrupt.Sources;

/// <summary>
/// 语音活动打断源 - 基于VAD检测的打断
/// Voice Activity Interrupt Source based on VAD detection
/// </summary>
public class VoiceActivityInterruptSource : InterruptSourceBase
{
    private readonly ISharedAudioRecorder? _audioRecorder;
    private readonly IVoiceChatService? _voiceChatService;
    
    // VAD配置参数
    private readonly VadConfiguration _vadConfig;
    
    // 音频数据缓冲和分析
    private readonly List<byte> _audioBuffer = new();
    private readonly object _bufferLock = new();
    private readonly int _frameSize = 1024; // 分析帧大小
    private readonly int _sampleRate = 16000; // 目标采样率
    private readonly int _channels = 1; // 单声道
    
    // VAD状态
    private bool _isVoiceActive = false;
    private DateTime _lastVoiceActivityTime = DateTime.MinValue;
    private DateTime _voiceStartTime = DateTime.MinValue;
    private int _consecutiveVoiceFrames = 0;
    private int _consecutiveSilenceFrames = 0;
    
    // 统计信息
    private int _audioFrameCount = 0;
    private int _vadTriggerCount = 0;

    public class VadConfiguration
    {
        public float EnergyThreshold { get; set; } = 0.001f;        // 能量阈值
        public int MinVoiceFrames { get; set; } = 3;               // 最少连续语音帧数
        public int MinSilenceFrames { get; set; } = 10;            // 最少连续静音帧数
        public float MinVoiceDurationMs { get; set; } = 100f;      // 最短语音持续时间
        public float MaxSilenceDurationMs { get; set; } = 500f;    // 最大静音持续时间
        public bool DebugOutput { get; set; } = false;            // 调试输出
    }

    public VoiceActivityInterruptSource(ISharedAudioRecorder? audioRecorder = null, 
        IVoiceChatService? voiceChatService = null,
        VadConfiguration? vadConfig = null,
        ILogger<VoiceActivityInterruptSource>? logger = null)
        : base("VoiceActivity", InterruptTypes.VoiceActivity, logger)
    {
        _audioRecorder = audioRecorder;
        _voiceChatService = voiceChatService;
        _vadConfig = vadConfig ?? new VadConfiguration();
    }

    /// <summary>
    /// 获取VAD配置，允许外部调整参数
    /// </summary>
    public VadConfiguration Configuration => _vadConfig;

    /// <summary>
    /// 获取VAD统计信息
    /// </summary>
    public VadStatistics GetStatistics()
    {
        return new VadStatistics
        {
            AudioFrameCount = _audioFrameCount,
            VadTriggerCount = _vadTriggerCount,
            IsVoiceActive = _isVoiceActive,
            LastVoiceActivityTime = _lastVoiceActivityTime,
            ConsecutiveVoiceFrames = _consecutiveVoiceFrames,
            ConsecutiveSilenceFrames = _consecutiveSilenceFrames
        };
    }

    public class VadStatistics
    {
        public int AudioFrameCount { get; set; }
        public int VadTriggerCount { get; set; }
        public bool IsVoiceActive { get; set; }
        public DateTime LastVoiceActivityTime { get; set; }
        public int ConsecutiveVoiceFrames { get; set; }
        public int ConsecutiveSilenceFrames { get; set; }
    }

    protected override async Task OnStartAsync()
    {
        _logger?.LogInformation("Voice activity interrupt source starting...");
        
        if (_audioRecorder != null)
        {
            // 订阅音频数据流
            _audioRecorder.SubscribeToAudioData(OnAudioDataReceived);
            _logger?.LogInformation("Subscribed to audio data stream for VAD analysis");
        }
        else
        {
            _logger?.LogWarning("No audio recorder available, VAD will use fallback simulation mode");
        }
        
        await base.OnStartAsync();
        _logger?.LogInformation("Voice activity interrupt source started");
    }

    protected override async Task OnStopAsync()
    {
        _logger?.LogInformation("Voice activity interrupt source stopping...");
        
        if (_audioRecorder != null)
        {
            // 取消订阅音频数据流
            _audioRecorder.UnsubscribeFromAudioData(OnAudioDataReceived);
            _logger?.LogInformation("Unsubscribed from audio data stream");
        }
        
        // 清理缓冲区
        lock (_bufferLock)
        {
            _audioBuffer.Clear();
        }
        
        await base.OnStopAsync();
        _logger?.LogInformation("Voice activity interrupt source stopped");
    }

    protected override async Task OnPauseAsync()
    {
        _logger?.LogDebug("Voice activity detection paused");
        await base.OnPauseAsync();
    }

    protected override async Task OnResumeAsync()
    {
        _logger?.LogDebug("Voice activity detection resumed");
        await base.OnResumeAsync();
    }

    /// <summary>
    /// 音频数据接收处理
    /// </summary>
    private void OnAudioDataReceived(object? sender, byte[] audioData)
    {
        if (_isPaused || !IsEnabled || audioData == null || audioData.Length == 0)
            return;

        try
        {
            lock (_bufferLock)
            {
                _audioBuffer.AddRange(audioData);
                _audioFrameCount++;
                
                // 当缓冲区有足够数据时进行VAD分析
                while (_audioBuffer.Count >= _frameSize * 2) // 16-bit samples
                {
                    ProcessAudioFrame();
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing audio data for VAD");
        }
    }

    /// <summary>
    /// 处理音频帧进行VAD分析
    /// </summary>
    private void ProcessAudioFrame()
    {
        // 提取一帧数据
        var frameData = _audioBuffer.Take(_frameSize * 2).ToArray();
        _audioBuffer.RemoveRange(0, _frameSize * 2);
        
        // 计算音频能量
        var energy = CalculateAudioEnergy(frameData);
        
        // VAD判断
        var hasVoice = energy > _vadConfig.EnergyThreshold;
        
        if (_vadConfig.DebugOutput && _audioFrameCount % 100 == 0) // 每100帧输出一次调试信息
        {
            _logger?.LogDebug("VAD Frame #{Frame} - Energy: {Energy:F6}, Threshold: {Threshold:F6}, Voice: {HasVoice}",
                _audioFrameCount, energy, _vadConfig.EnergyThreshold, hasVoice);
        }
        
        UpdateVadState(hasVoice);
    }

    /// <summary>
    /// 计算音频能量（RMS）
    /// </summary>
    private float CalculateAudioEnergy(byte[] audioData)
    {
        if (audioData.Length < 2) return 0f;
        
        long sum = 0;
        int sampleCount = audioData.Length / 2;
        
        for (int i = 0; i < audioData.Length - 1; i += 2)
        {
            // 转换为16-bit signed integer
            short sample = (short)(audioData[i] | (audioData[i + 1] << 8));
            sum += sample * sample;
        }
        
        return (float)Math.Sqrt((double)sum / sampleCount) / short.MaxValue;
    }

    /// <summary>
    /// 更新VAD状态并触发相应事件
    /// </summary>
    private void UpdateVadState(bool hasVoice)
    {
        if (hasVoice)
        {
            _consecutiveVoiceFrames++;
            _consecutiveSilenceFrames = 0;
            
            // 检测语音开始
            if (!_isVoiceActive && _consecutiveVoiceFrames >= _vadConfig.MinVoiceFrames)
            {
                _isVoiceActive = true;
                _voiceStartTime = DateTime.Now;
                _lastVoiceActivityTime = DateTime.Now;
                
                if (_vadConfig.DebugOutput)
                {
                    _logger?.LogDebug("Voice activity started (frames: {Frames})", _consecutiveVoiceFrames);
                }
            }
            else if (_isVoiceActive)
            {
                _lastVoiceActivityTime = DateTime.Now;
            }
        }
        else
        {
            _consecutiveSilenceFrames++;
            _consecutiveVoiceFrames = 0;
            
            // 检测语音结束
            if (_isVoiceActive && _consecutiveSilenceFrames >= _vadConfig.MinSilenceFrames)
            {
                var voiceDuration = (DateTime.Now - _voiceStartTime).TotalMilliseconds;
                
                // 只有语音持续时间足够长才触发中断
                if (voiceDuration >= _vadConfig.MinVoiceDurationMs)
                {
                    TriggerVoiceInterrupt(voiceDuration);
                }
                
                _isVoiceActive = false;
                
                if (_vadConfig.DebugOutput)
                {
                    _logger?.LogDebug("Voice activity ended (duration: {Duration}ms, silence frames: {Frames})", 
                        voiceDuration, _consecutiveSilenceFrames);
                }
            }
        }
    }

    /// <summary>
    /// 触发语音中断事件
    /// </summary>
    private void TriggerVoiceInterrupt(double voiceDurationMs)
    {
        _vadTriggerCount++;
        var confidence = CalculateVoiceConfidence(voiceDurationMs);
        
        TriggerInterrupt(
            $"Voice activity detected (duration: {voiceDurationMs:F0}ms, confidence: {confidence:P1})",
            new 
            { 
                VoiceDurationMs = voiceDurationMs,
                Confidence = confidence,
                TriggerCount = _vadTriggerCount,
                AudioFrameCount = _audioFrameCount,
                ConsecutiveVoiceFrames = _consecutiveVoiceFrames,
                VadConfig = new
                {
                    _vadConfig.EnergyThreshold,
                    _vadConfig.MinVoiceFrames,
                    _vadConfig.MinSilenceFrames,
                    _vadConfig.MinVoiceDurationMs,
                    _vadConfig.MaxSilenceDurationMs
                }
            },
            priority: 7 // 高优先级，因为是实时语音检测
        );
        
        _logger?.LogInformation("Voice interrupt triggered - Duration: {Duration}ms, Confidence: {Confidence:P1}, Count: #{Count}",
            voiceDurationMs, confidence, _vadTriggerCount);
    }

    /// <summary>
    /// 计算语音置信度
    /// </summary>
    private float CalculateVoiceConfidence(double voiceDurationMs)
    {
        var baseConfidence = 0.7f;
        
        // 根据语音持续时间调整置信度
        if (voiceDurationMs >= 500) // 长语音更可靠
            baseConfidence += 0.2f;
        else if (voiceDurationMs >= 200)
            baseConfidence += 0.1f;
        else if (voiceDurationMs < 100) // 短语音可能是噪音
            baseConfidence -= 0.2f;
        
        // 根据触发频率调整置信度（避免过于频繁触发）
        var timeSinceLastTrigger = (DateTime.Now - _lastVoiceActivityTime).TotalSeconds;
        if (timeSinceLastTrigger < 1.0) // 1秒内重复触发
            baseConfidence -= 0.1f;
        
        return Math.Max(0.3f, Math.Min(1.0f, baseConfidence));
    }

    /// <summary>
    /// 动态更新VAD配置
    /// </summary>
    public void UpdateVadConfiguration(Action<VadConfiguration> configUpdater)
    {
        configUpdater(_vadConfig);
        _logger?.LogInformation("VAD configuration updated");
    }

    protected override async Task MonitoringLoopAsync()
    {
        _logger?.LogInformation("Voice activity interrupt monitoring started");

        // 如果没有音频录制器，使用模拟模式
        if (_audioRecorder == null)
        {
            await RunSimulationModeAsync();
            return;
        }

        // 启动监控任务 - 主要用于统计和检查超时
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, _cancellationTokenSource.Token);
                
                // 每10秒输出一次统计信息（调试模式）
                if (_vadConfig.DebugOutput && _audioFrameCount % 1000 == 0 && _audioFrameCount > 0)
                {
                    var stats = GetStatistics();
                    _logger?.LogDebug("VAD Statistics - Frames: {Frames}, VAD Triggers: {Triggers}, Voice Active: {IsActive}",
                        stats.AudioFrameCount, stats.VadTriggerCount, stats.IsVoiceActive);
                }
                
                // 检查语音活动超时（防止卡在语音状态）
                if (_isVoiceActive)
                {
                    var voiceDuration = (DateTime.Now - _voiceStartTime).TotalMilliseconds;
                    if (voiceDuration > _vadConfig.MaxSilenceDurationMs * 10) // 超时阈值
                    {
                        _logger?.LogWarning("Voice activity timeout detected, resetting VAD state");
                        _isVoiceActive = false;
                        _consecutiveVoiceFrames = 0;
                        _consecutiveSilenceFrames = 0;
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
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
        }
    }

    /// <summary>
    /// 模拟模式 - 当没有真实音频录制器时使用
    /// </summary>
    private async Task RunSimulationModeAsync()
    {
        _logger?.LogWarning("Running VAD in simulation mode (no audio recorder available)");
        var random = new Random();
        var averageInterval = TimeSpan.FromSeconds(45); // 45秒间隔，比原来更长一些

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                if (!_isPaused && IsEnabled)
                {
                    // 模拟语音活动检测 - 随机触发，但频率更低
                    var waitTime = TimeSpan.FromSeconds(averageInterval.TotalSeconds * (0.7 + random.NextDouble() * 0.6));
                    await Task.Delay(waitTime, _cancellationTokenSource.Token);

                    if (!_isPaused && IsEnabled && !_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        var simulatedDuration = 200 + random.NextDouble() * 800; // 200-1000ms
                        var probability = 0.7f + (float)(random.NextDouble() * 0.3); // 0.7-1.0
                        
                        TriggerInterrupt(
                            $"Simulated voice activity (duration: {simulatedDuration:F0}ms, confidence: {probability:P1})",
                            new 
                            { 
                                IsSimulated = true,
                                VoiceDurationMs = simulatedDuration,
                                Confidence = probability,
                                TriggerCount = ++_vadTriggerCount
                            },
                            priority: 6 // 稍低优先级，因为是模拟
                        );
                        
                        _logger?.LogInformation("Simulated voice interrupt triggered - Duration: {Duration}ms, Confidence: {Confidence:P1}",
                            simulatedDuration, probability);
                    }
                }
                else
                {
                    await Task.Delay(1000, _cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in VAD simulation mode");
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            try
            {
                // 取消订阅音频数据流
                if (_audioRecorder != null)
                {
                    _audioRecorder.UnsubscribeFromAudioData(OnAudioDataReceived);
                }
                
                // 清理缓冲区
                lock (_bufferLock)
                {
                    _audioBuffer.Clear();
                }
                
                _logger?.LogDebug("VoiceActivityInterruptSource disposed successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during VoiceActivityInterruptSource disposal");
            }
        }
        base.Dispose(disposing);
    }
}