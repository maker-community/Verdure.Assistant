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
    private readonly IVoiceChatService? _voiceChatService;
    private readonly Random _random = new();
    private readonly TimeSpan _averageInterval = TimeSpan.FromSeconds(30); // 平均30秒触发一次

    public VoiceActivityInterruptSource(ISharedAudioRecorder? audioRecorder = null, 
        IVoiceChatService? voiceChatService = null,
        ILogger<VoiceActivityInterruptSource>? logger = null)
        : base("VoiceActivity", InterruptTypes.VoiceActivity, logger)
    {
        _audioRecorder = audioRecorder;
        _voiceChatService = voiceChatService;
    }

    protected override async Task OnStartAsync()
    {
        _logger?.LogInformation("Voice activity interrupt source started");
        await base.OnStartAsync();
    }

    protected override async Task OnStopAsync()
    {
        _logger?.LogInformation("Voice activity interrupt source stopped");
        await base.OnStopAsync();
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

    protected override async Task MonitoringLoopAsync()
    {
        _logger?.LogInformation("Voice activity interrupt monitoring started");

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                if (!_isPaused && IsEnabled)
                {
                    // 模拟语音活动检测 - 随机触发
                    var waitTime = TimeSpan.FromSeconds(_averageInterval.TotalSeconds * (0.5 + _random.NextDouble()));
                    await Task.Delay(waitTime, _cancellationTokenSource.Token);

                    if (!_isPaused && IsEnabled && !_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        var probability = 0.6f + (float)(_random.NextDouble() * 0.4); // 0.6-1.0
                        
                        TriggerInterrupt(
                            "Simulated voice activity detected",
                            new { Probability = probability },
                            priority: 6
                        );
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
                _logger?.LogError(ex, "Error in voice activity interrupt monitoring loop");
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 清理资源
        }
        base.Dispose(disposing);
    }
}