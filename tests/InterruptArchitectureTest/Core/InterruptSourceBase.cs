using Microsoft.Extensions.Logging;

namespace InterruptArchitectureTest.Core;

/// <summary>
/// 打断源抽象基类，提供通用功能
/// </summary>
public abstract class InterruptSourceBase : IInterruptSource
{
    protected readonly ILogger? _logger;
    protected readonly CancellationTokenSource _cancellationTokenSource = new();
    protected bool _isRunning;
    protected bool _isPaused;
    protected bool _disposed;

    public string Name { get; protected set; }
    public string InterruptType { get; protected set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsRunning => _isRunning && !_isPaused;
    
    public event EventHandler<InterruptEventArgs>? InterruptTriggered;

    protected InterruptSourceBase(string name, string interruptType, ILogger? logger = null)
    {
        Name = name;
        InterruptType = interruptType;
        _logger = logger;
    }

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

    public virtual Task PauseAsync()
    {
        if (!_isRunning || _isPaused) return Task.CompletedTask;
        
        _isPaused = true;
        _logger?.LogDebug("Interrupt source paused: {Name}", Name);
        return OnPauseAsync();
    }

    public virtual Task ResumeAsync()
    {
        if (!_isRunning || !_isPaused) return Task.CompletedTask;
        
        _isPaused = false;
        _logger?.LogDebug("Interrupt source resumed: {Name}", Name);
        return OnResumeAsync();
    }

    protected virtual Task OnStartAsync() => Task.CompletedTask;
    protected virtual Task OnStopAsync() => Task.CompletedTask;
    protected virtual Task OnPauseAsync() => Task.CompletedTask;
    protected virtual Task OnResumeAsync() => Task.CompletedTask;

    // 持续监听的核心方法
    protected abstract Task MonitoringLoopAsync();

    protected virtual void TriggerInterrupt(string description, object? data = null, int priority = 0)
    {
        if (!IsEnabled || _isPaused) return;

        var eventArgs = new InterruptEventArgs(InterruptType, Name, description, data, priority);
        InterruptTriggered?.Invoke(this, eventArgs);
        
        _logger?.LogInformation("Interrupt triggered by {Name}: {Description}", Name, description);
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
