using Microsoft.Extensions.Logging;

namespace InterruptArchitectureTest.Sources;

/// <summary>
/// 手动打断源 - 支持程序化触发打断
/// </summary>
public class ManualInterruptSource : Core.InterruptSourceBase
{
    private readonly Dictionary<string, object?> _pendingInterrupts = new();
    private readonly object _lock = new object();

    public ManualInterruptSource(ILogger<ManualInterruptSource>? logger = null)
        : base("ManualTrigger", Core.InterruptTypes.Manual, logger)
    {
    }

    protected override Task MonitoringLoopAsync()
    {
        return Task.Run(async () =>
        {
            _logger?.LogInformation("Manual interrupt monitoring started");

            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    Dictionary<string, object?> interruptsToProcess;
                    
                    lock (_lock)
                    {
                        if (_pendingInterrupts.Count == 0)
                        {
                            Monitor.Wait(_lock, 1000); // 等待1秒或直到有新的打断
                            continue;
                        }
                        
                        interruptsToProcess = new Dictionary<string, object?>(_pendingInterrupts);
                        _pendingInterrupts.Clear();
                    }

                    foreach (var interrupt in interruptsToProcess)
                    {
                        if (!_isPaused && IsEnabled)
                        {
                            TriggerInterrupt(interrupt.Key, interrupt.Value, priority: 9);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in manual interrupt monitoring loop");
                    await Task.Delay(1000, _cancellationTokenSource.Token);
                }
            }
        }, _cancellationTokenSource.Token);
    }

    public void TriggerManualInterrupt(string description, object? data = null)
    {
        lock (_lock)
        {
            _pendingInterrupts[description] = data;
            Monitor.Pulse(_lock); // 通知监听线程
        }
        
        _logger?.LogInformation("Manual interrupt queued: {Description}", description);
    }

    public void TriggerImmediateInterrupt(string description, object? data = null)
    {
        if (!_isPaused && IsEnabled)
        {
            TriggerInterrupt(description, data, priority: 9);
        }
    }
}

/// <summary>
/// 定时器打断源 - 按设定间隔触发打断
/// </summary>
public class TimerInterruptSource : Core.InterruptSourceBase
{
    private readonly TimeSpan _interval;
    private readonly string _message;
    private DateTime _lastTriggerTime = DateTime.MinValue;

    public TimerInterruptSource(TimeSpan interval, string message = "Timer interrupt", 
        ILogger<TimerInterruptSource>? logger = null)
        : base("TimerTrigger", Core.InterruptTypes.Timer, logger)
    {
        _interval = interval;
        _message = message;
    }

    protected override async Task MonitoringLoopAsync()
    {
        _logger?.LogInformation("Timer interrupt monitoring started with interval: {Interval}", _interval);

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                if (!_isPaused && IsEnabled)
                {
                    var now = DateTime.UtcNow;
                    if (now - _lastTriggerTime >= _interval)
                    {
                        _lastTriggerTime = now;
                        TriggerInterrupt(
                            _message,
                            new { Interval = _interval, TriggerTime = now },
                            priority: 3
                        );
                    }
                }

                // 使用较短的检查间隔以保证精度
                var checkInterval = TimeSpan.FromMilliseconds(Math.Min(_interval.TotalMilliseconds / 10, 1000));
                await Task.Delay(checkInterval, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in timer interrupt monitoring loop");
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
        }
    }
}
