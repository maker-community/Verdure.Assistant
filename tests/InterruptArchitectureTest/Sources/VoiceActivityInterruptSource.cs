using Microsoft.Extensions.Logging;

namespace InterruptArchitectureTest.Sources;

/// <summary>
/// 简化的语音活动检测源 - 用于测试
/// </summary>
public class SimpleVoiceActivityInterruptSource : Core.InterruptSourceBase
{
    private readonly Random _random = new();
    private readonly TimeSpan _averageInterval = TimeSpan.FromSeconds(30); // 平均30秒触发一次

    public SimpleVoiceActivityInterruptSource(ILogger<SimpleVoiceActivityInterruptSource>? logger = null)
        : base("SimpleVAD", Core.InterruptTypes.VoiceActivity, logger)
    {
    }

    protected override async Task MonitoringLoopAsync()
    {
        _logger?.LogInformation("Simple voice activity monitoring started");

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
                _logger?.LogError(ex, "Error in simple voice activity monitoring loop");
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
        }
    }
}
