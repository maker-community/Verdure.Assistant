using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services.Interrupt;

/// <summary>
/// 打断源抽象基类，提供通用功能
/// Abstract base class for interrupt sources providing common functionality
/// </summary>
public abstract class InterruptSourceBase : IInterruptSource
{
    protected readonly ILogger? _logger;
    protected readonly CancellationTokenSource _cancellationTokenSource = new();
    protected bool _isRunning;
    protected bool _isPaused;
    protected bool _disposed;

    /// <summary>
    /// 打断源名称
    /// </summary>
    public string Name { get; protected set; }
    
    /// <summary>
    /// 打断源类型
    /// </summary>
    public string InterruptType { get; protected set; }
    
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning => _isRunning && !_isPaused;
    
    /// <summary>
    /// 打断事件触发
    /// </summary>
    public event EventHandler<InterruptEventArgs>? InterruptTriggered;

    protected InterruptSourceBase(string name, string interruptType, ILogger? logger = null)
    {
        Name = name;
        InterruptType = interruptType;
        _logger = logger;
    }

    /// <summary>
    /// 启动打断源
    /// </summary>
    public virtual async Task StartAsync()
    {
        if (_isRunning) return;
        
        _logger?.LogInformation("Starting interrupt source: {Name} ({Type})", Name, InterruptType);
        
        try
        {
            await OnStartAsync();
            _isRunning = true;
            _isPaused = false;
            
            // 启动持续监听任务
            _ = Task.Run(MonitoringLoopAsync, _cancellationTokenSource.Token);
            
            _logger?.LogInformation("Interrupt source started: {Name}", Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start interrupt source: {Name}", Name);
            throw;
        }
    }

    /// <summary>
    /// 停止打断源
    /// </summary>
    public virtual async Task StopAsync()
    {
        if (!_isRunning) return;
        
        _logger?.LogInformation("Stopping interrupt source: {Name}", Name);
        
        try
        {
            _cancellationTokenSource.Cancel();
            await OnStopAsync();
            _isRunning = false;
            _isPaused = false;
            
            _logger?.LogInformation("Interrupt source stopped: {Name}", Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to stop interrupt source: {Name}", Name);
            throw;
        }
    }

    /// <summary>
    /// 暂停打断源
    /// </summary>
    public virtual Task PauseAsync()
    {
        if (!_isRunning || _isPaused) return Task.CompletedTask;
        
        _isPaused = true;
        _logger?.LogDebug("Interrupt source paused: {Name}", Name);
        return OnPauseAsync();
    }

    /// <summary>
    /// 恢复打断源
    /// </summary>
    public virtual Task ResumeAsync()
    {
        if (!_isRunning || !_isPaused) return Task.CompletedTask;
        
        _isPaused = false;
        _logger?.LogDebug("Interrupt source resumed: {Name}", Name);
        return OnResumeAsync();
    }

    /// <summary>
    /// 启动时的钩子方法
    /// </summary>
    protected virtual Task OnStartAsync() => Task.CompletedTask;
    
    /// <summary>
    /// 停止时的钩子方法
    /// </summary>
    protected virtual Task OnStopAsync() => Task.CompletedTask;
    
    /// <summary>
    /// 暂停时的钩子方法
    /// </summary>
    protected virtual Task OnPauseAsync() => Task.CompletedTask;
    
    /// <summary>
    /// 恢复时的钩子方法
    /// </summary>
    protected virtual Task OnResumeAsync() => Task.CompletedTask;

    /// <summary>
    /// 持续监听的核心方法，由子类实现
    /// </summary>
    protected abstract Task MonitoringLoopAsync();

    /// <summary>
    /// 触发打断事件
    /// </summary>
    protected virtual void TriggerInterrupt(string description, object? data = null, int priority = 0)
    {
        if (!IsEnabled || _isPaused) return;

        var eventArgs = new InterruptEventArgs(InterruptType, Name, description, 
            GetAbortReasonForInterruptType(InterruptType), data, priority);
        InterruptTriggered?.Invoke(this, eventArgs);
        
        _logger?.LogInformation("Interrupt triggered by {Name}: {Description}", Name, description);
    }

    /// <summary>
    /// 根据打断类型获取对应的中止原因
    /// </summary>
    private static Constants.AbortReason GetAbortReasonForInterruptType(string interruptType)
    {
        return interruptType switch
        {
            InterruptTypes.VoiceActivity => Constants.AbortReason.VoiceInterruption,
            InterruptTypes.Hotkey => Constants.AbortReason.KeyboardInterruption,
            InterruptTypes.Keyword => Constants.AbortReason.WakeWordDetected,
            InterruptTypes.Network => Constants.AbortReason.NetworkError,
            InterruptTypes.Api => Constants.AbortReason.UserInterruption,
            _ => Constants.AbortReason.UserInterruption
        };
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _ = StopAsync();
                _cancellationTokenSource?.Dispose();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}