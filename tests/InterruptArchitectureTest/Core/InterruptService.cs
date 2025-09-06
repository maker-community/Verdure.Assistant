using Microsoft.Extensions.Logging;
using InterruptArchitectureTest.Sources;

namespace InterruptArchitectureTest.Core;

/// <summary>
/// 中断服务实现
/// </summary>
public class InterruptService : IInterruptService
{
    private readonly Dictionary<string, IInterruptSource> _interruptSources = new();
    private readonly ILogger<InterruptService>? _logger;
    private readonly object _lock = new object();
    private readonly TimeSpan _cooldownPeriod = TimeSpan.FromMilliseconds(300);
    private DateTime _lastInterruptTime = DateTime.MinValue;
    private bool _disposed;

    public event EventHandler<InterruptEventArgs>? InterruptOccurred;

    public InterruptService(ILogger<InterruptService>? logger = null)
    {
        _logger = logger;
    }

    public void RegisterInterruptSource(IInterruptSource source)
    {
        lock (_lock)
        {
            if (_interruptSources.ContainsKey(source.Name))
            {
                _logger?.LogWarning("Interrupt source already registered: {Name}", source.Name);
                return;
            }

            _interruptSources[source.Name] = source;
            source.InterruptTriggered += OnInterruptTriggered;
            
            _logger?.LogInformation("Registered interrupt source: {Name} ({Type})", source.Name, source.InterruptType);
        }
    }

    public void UnregisterInterruptSource(string sourceName)
    {
        lock (_lock)
        {
            if (_interruptSources.TryGetValue(sourceName, out var source))
            {
                source.InterruptTriggered -= OnInterruptTriggered;
                _ = source.StopAsync();
                _interruptSources.Remove(sourceName);
                
                _logger?.LogInformation("Unregistered interrupt source: {Name}", sourceName);
            }
        }
    }

    public IInterruptSource? GetInterruptSource(string sourceName)
    {
        lock (_lock)
        {
            return _interruptSources.TryGetValue(sourceName, out var source) ? source : null;
        }
    }

    public IEnumerable<IInterruptSource> GetAllInterruptSources()
    {
        lock (_lock)
        {
            return _interruptSources.Values.ToList();
        }
    }

    public async Task StartAllAsync()
    {
        var tasks = new List<Task>();
        
        lock (_lock)
        {
            foreach (var source in _interruptSources.Values)
            {
                tasks.Add(source.StartAsync());
            }
        }

        await Task.WhenAll(tasks);
        _logger?.LogInformation("All interrupt sources started");
    }

    public async Task StopAllAsync()
    {
        var tasks = new List<Task>();
        
        lock (_lock)
        {
            foreach (var source in _interruptSources.Values)
            {
                tasks.Add(source.StopAsync());
            }
        }

        await Task.WhenAll(tasks);
        _logger?.LogInformation("All interrupt sources stopped");
    }

    public async Task PauseSourceAsync(string sourceName)
    {
        var source = GetInterruptSource(sourceName);
        if (source != null)
        {
            await source.PauseAsync();
            _logger?.LogInformation("Paused interrupt source: {Name}", sourceName);
        }
    }

    public async Task ResumeSourceAsync(string sourceName)
    {
        var source = GetInterruptSource(sourceName);
        if (source != null)
        {
            await source.ResumeAsync();
            _logger?.LogInformation("Resumed interrupt source: {Name}", sourceName);
        }
    }

    public async Task TriggerManualInterruptAsync(string description, object? data = null)
    {
        var manualSource = GetInterruptSource("ManualTrigger") as ManualInterruptSource;
        if (manualSource != null)
        {
            manualSource.TriggerManualInterrupt(description, data);
        }
        else
        {
            // 直接触发事件
            var eventArgs = new InterruptEventArgs(InterruptTypes.Manual, "DirectTrigger", description, data, 9);
            OnInterruptTriggered(this, eventArgs);
        }
        
        await Task.CompletedTask;
    }

    private void OnInterruptTriggered(object? sender, InterruptEventArgs e)
    {
        // 实现冷却期
        var now = DateTime.UtcNow;
        if (now - _lastInterruptTime < _cooldownPeriod)
        {
            _logger?.LogDebug("Interrupt ignored due to cooldown: {Type} from {Source}", e.InterruptType, e.SourceName);
            return;
        }

        _lastInterruptTime = now;
        
        _logger?.LogInformation("Interrupt occurred: {Type} from {Source} - {Description}", 
            e.InterruptType, e.SourceName, e.Description);

        InterruptOccurred?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _ = StopAllAsync();
            
            lock (_lock)
            {
                foreach (var source in _interruptSources.Values)
                {
                    source.Dispose();
                }
                _interruptSources.Clear();
            }
            
            _disposed = true;
        }
    }
}
