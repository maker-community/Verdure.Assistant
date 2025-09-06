using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace Verdure.Assistant.Core.Services.Interrupt.Sources;

/// <summary>
/// 手动打断源 - 支持程序化触发打断
/// Manual interrupt source for programmatic interrupt triggering
/// </summary>
public class ManualInterruptSource : InterruptSourceBase
{
    private readonly Dictionary<string, object?> _pendingInterrupts = new();
    private readonly object _lock = new object();

    public ManualInterruptSource(ILogger<ManualInterruptSource>? logger = null)
        : base("Manual", InterruptTypes.Manual, logger)
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

    /// <summary>
    /// 触发手动打断
    /// </summary>
    public void TriggerManualInterrupt(string description, object? data = null)
    {
        lock (_lock)
        {
            _pendingInterrupts[description] = data;
            Monitor.Pulse(_lock); // 通知监听线程
        }
        
        _logger?.LogInformation("Manual interrupt queued: {Description}", description);
    }

    /// <summary>
    /// 立即触发打断（不走队列）
    /// </summary>
    public void TriggerImmediateInterrupt(string description, object? data = null)
    {
        if (!_isPaused && IsEnabled)
        {
            TriggerInterrupt(description, data, priority: 9);
        }
    }
}