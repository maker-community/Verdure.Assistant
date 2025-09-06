using System;
using System.Threading.Tasks;

namespace Verdure.Assistant.Core.Services.Interrupt;

/// <summary>
/// 打断源接口
/// Interface for interrupt sources in the conversation system
/// </summary>
public interface IInterruptSource : IDisposable
{
    /// <summary>
    /// 打断源名称
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 打断源类型
    /// </summary>
    string InterruptType { get; }
    
    /// <summary>
    /// 是否启用
    /// </summary>
    bool IsEnabled { get; set; }
    
    /// <summary>
    /// 是否正在运行
    /// </summary>
    bool IsRunning { get; }
    
    /// <summary>
    /// 打断事件触发
    /// </summary>
    event EventHandler<InterruptEventArgs>? InterruptTriggered;
    
    /// <summary>
    /// 启动打断源
    /// </summary>
    Task StartAsync();
    
    /// <summary>
    /// 停止打断源
    /// </summary>
    Task StopAsync();
    
    /// <summary>
    /// 暂停打断源
    /// </summary>
    Task PauseAsync();
    
    /// <summary>
    /// 恢复打断源
    /// </summary>
    Task ResumeAsync();
}