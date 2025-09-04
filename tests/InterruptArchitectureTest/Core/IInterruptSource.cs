namespace InterruptArchitectureTest.Core;

/// <summary>
/// 打断源接口
/// </summary>
public interface IInterruptSource : IDisposable
{
    string Name { get; }
    string InterruptType { get; }
    bool IsEnabled { get; set; }
    bool IsRunning { get; }
    
    event EventHandler<InterruptEventArgs>? InterruptTriggered;
    
    Task StartAsync();
    Task StopAsync();
    Task PauseAsync();
    Task ResumeAsync();
}
