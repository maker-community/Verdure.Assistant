using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Core.Services.Interrupt.Sources;

/// <summary>
/// 语音活动打断源 - 基于VAD检测的打断
/// Voice Activity Interrupt Source based on VAD detection
/// </summary>
public class VoiceActivityInterruptSource : InterruptSourceBase
{
    private readonly ISharedAudioRecorder? _audioRecorder;
    private VADDetectorService? _vadDetector;
    private bool _vadDetectionActive = false;

    public VoiceActivityInterruptSource(ISharedAudioRecorder? audioRecorder = null, 
        ILogger<VoiceActivityInterruptSource>? logger = null)
        : base("VoiceActivity", InterruptTypes.VoiceActivity, logger)
    {
        _audioRecorder = audioRecorder;
    }

    /// <summary>
    /// 设置语音聊天服务用于VAD检测
    /// </summary>
    public void SetVoiceChatService(IVoiceChatService voiceChatService)
    {
        _vadDetector = new VADDetectorService(voiceChatService, _audioRecorder);
        _vadDetector.VoiceInterruptDetected += OnVoiceActivityDetected;
    }

    protected override async Task OnStartAsync()
    {
        if (_vadDetector != null && !_vadDetectionActive)
        {
            _vadDetector.Start();
            _vadDetectionActive = true;
            _logger?.LogInformation("VAD detection started for voice activity interrupt");
        }
        await base.OnStartAsync();
    }

    protected override async Task OnStopAsync()
    {
        if (_vadDetector != null && _vadDetectionActive)
        {
            _vadDetector.Stop();
            _vadDetectionActive = false;
            _logger?.LogInformation("VAD detection stopped for voice activity interrupt");
        }
        await base.OnStopAsync();
    }

    protected override async Task OnPauseAsync()
    {
        if (_vadDetector != null && _vadDetectionActive)
        {
            _vadDetector.Pause();
            _logger?.LogDebug("VAD detection paused");
        }
        await base.OnPauseAsync();
    }

    protected override async Task OnResumeAsync()
    {
        if (_vadDetector != null && _vadDetectionActive && _vadDetector.IsPaused)
        {
            _vadDetector.Resume();
            _logger?.LogDebug("VAD detection resumed");
        }
        await base.OnResumeAsync();
    }

    protected override async Task MonitoringLoopAsync()
    {
        _logger?.LogInformation("Voice activity interrupt monitoring started");

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                // VAD检测在OnVoiceActivityDetected中处理，这里只需要保持监听循环
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in voice activity interrupt monitoring loop");
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
        }
    }

    private void OnVoiceActivityDetected(object? sender, bool detected)
    {
        if (detected && !_isPaused && IsEnabled)
        {
            TriggerInterrupt("Voice activity detected during assistant response", null, priority: 8);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_vadDetector != null)
            {
                _vadDetector.VoiceInterruptDetected -= OnVoiceActivityDetected;
                _vadDetector.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}