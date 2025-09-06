using Verdure.Assistant.Core.Constants;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// Legacy interrupt event arguments for backward compatibility
/// 传统中断事件参数，用于向后兼容
/// </summary>
public class InterruptEventArgs : EventArgs
{
    /// <summary>
    /// 中断原因
    /// </summary>
    public AbortReason Reason { get; }
    
    /// <summary>
    /// 中断描述
    /// </summary>
    public string Description { get; }
    
    /// <summary>
    /// 构造函数
    /// </summary>
    public InterruptEventArgs(AbortReason reason, string description)
    {
        Reason = reason;
        Description = description;
    }
}
