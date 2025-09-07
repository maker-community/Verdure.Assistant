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

/// <summary>
/// 打断类别 - 用于区分不同类型的打断处理方式
/// Interrupt categories for different interrupt handling approaches
/// </summary>
public static class InterruptCategories
{
    /// <summary>
    /// 手动打断类别 - 包括按键、API、手动打断
    /// Manual interrupt category - includes hotkey, API, and manual interrupts
    /// </summary>
    public const string Manual = "manual_category";
    
    /// <summary>
    /// VAD打断类别 - 语音活动检测打断，更敏感，仅在音乐播放时激活
    /// VAD interrupt category - voice activity detection, more sensitive, only active during music playback
    /// </summary>
    public const string VoiceActivity = "vad_category";
}

///// <summary>
///// 打断类型分类帮助器
///// Interrupt type categorization helper
///// </summary>
//public static class InterruptTypeHelper
//{
//    /// <summary>
//    /// 获取打断类型的类别
//    /// Get the category for an interrupt type
//    /// </summary>
//    public static string GetInterruptCategory(string interruptType)
//    {
//        return interruptType switch
//        {
//            InterruptTypes.Hotkey => InterruptCategories.Manual,
//            InterruptTypes.Api => InterruptCategories.Manual,
//            InterruptTypes.Manual => InterruptCategories.Manual,
//            InterruptTypes.VoiceActivity => InterruptCategories.VoiceActivity,
//            _ => InterruptCategories.Manual // Default to manual for other types
//        };
//    }
    
//    /// <summary>
//    /// 检查是否为手动打断类型
//    /// Check if interrupt type is manual category
//    /// </summary>
//    public static bool IsManualInterrupt(string interruptType)
//    {
//        return GetInterruptCategory(interruptType) == InterruptCategories.Manual;
//    }
    
//    /// <summary>
//    /// 检查是否为VAD打断类型
//    /// Check if interrupt type is VAD category
//    /// </summary>
//    public static bool IsVadInterrupt(string interruptType)
//    {
//        return GetInterruptCategory(interruptType) == InterruptCategories.VoiceActivity;
//    }
//}