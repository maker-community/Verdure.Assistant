using System;
using Verdure.Assistant.Core.Constants;

namespace Verdure.Assistant.Core.Services.Interrupt;

/// <summary>
/// 打断事件参数
/// Event arguments for interrupt events
/// </summary>
public class InterruptEventArgs : EventArgs
{
    /// <summary>
    /// 打断类型
    /// </summary>
    public string InterruptType { get; }
    
    /// <summary>
    /// 打断源名称
    /// </summary>
    public string SourceName { get; }
    
    /// <summary>
    /// 打断描述
    /// </summary>
    public string Description { get; }
    
    /// <summary>
    /// 打断数据
    /// </summary>
    public object? Data { get; }
    
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; }
    
    /// <summary>
    /// 优先级（数字越大优先级越高）
    /// </summary>
    public int Priority { get; set; } = 0;
    
    /// <summary>
    /// 对应的中止原因
    /// </summary>
    public AbortReason AbortReason { get; }

    public InterruptEventArgs(string interruptType, string sourceName, string description, 
        AbortReason abortReason = AbortReason.UserInterruption, object? data = null, int priority = 0)
    {
        InterruptType = interruptType;
        SourceName = sourceName;
        Description = description;
        AbortReason = abortReason;
        Data = data;
        Timestamp = DateTime.UtcNow;
        Priority = priority;
    }
}

/// <summary>
/// 预定义的打断类型常量
/// Predefined interrupt type constants
/// </summary>
public static class InterruptTypes
{
    public const string Keyword = "keyword";
    public const string Hotkey = "hotkey";
    public const string VoiceActivity = "voice_activity";
    public const string Manual = "manual";
    public const string Network = "network";
    public const string Timer = "timer";
    public const string Api = "api";
    public const string Custom = "custom";
}