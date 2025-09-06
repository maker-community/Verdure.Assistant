using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Verdure.Assistant.Core.Services.Interrupt;

/// <summary>
/// 打断服务接口
/// Interrupt service interface for managing multiple interrupt sources
/// </summary>
public interface IInterruptService : IDisposable
{
    /// <summary>
    /// 打断事件发生
    /// </summary>
    event EventHandler<InterruptEventArgs>? InterruptOccurred;
    
    /// <summary>
    /// 注册打断源
    /// </summary>
    void RegisterInterruptSource(IInterruptSource source);
    
    /// <summary>
    /// 注销打断源
    /// </summary>
    void UnregisterInterruptSource(string sourceName);
    
    /// <summary>
    /// 获取打断源
    /// </summary>
    IInterruptSource? GetInterruptSource(string sourceName);
    
    /// <summary>
    /// 获取所有打断源
    /// </summary>
    IEnumerable<IInterruptSource> GetAllInterruptSources();
    
    /// <summary>
    /// 启动所有打断源
    /// </summary>
    Task StartAllAsync();
    
    /// <summary>
    /// 停止所有打断源
    /// </summary>
    Task StopAllAsync();
    
    /// <summary>
    /// 暂停指定打断源
    /// </summary>
    Task PauseSourceAsync(string sourceName);
    
    /// <summary>
    /// 恢复指定打断源
    /// </summary>
    Task ResumeSourceAsync(string sourceName);
    
    /// <summary>
    /// 手动触发打断
    /// </summary>
    Task TriggerManualInterruptAsync(string description, object? data = null);
}