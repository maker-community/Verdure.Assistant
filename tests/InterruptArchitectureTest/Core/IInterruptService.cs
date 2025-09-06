namespace InterruptArchitectureTest.Core;

/// <summary>
/// 中断服务接口
/// </summary>
public interface IInterruptService : IDisposable
{
    event EventHandler<InterruptEventArgs>? InterruptOccurred;
    
    void RegisterInterruptSource(IInterruptSource source);
    void UnregisterInterruptSource(string sourceName);
    IInterruptSource? GetInterruptSource(string sourceName);
    IEnumerable<IInterruptSource> GetAllInterruptSources();
    
    Task StartAllAsync();
    Task StopAllAsync();
    Task PauseSourceAsync(string sourceName);
    Task ResumeSourceAsync(string sourceName);
    
    Task TriggerManualInterruptAsync(string description, object? data = null);
}
