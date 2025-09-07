namespace Verdure.Assistant.Core.Services.Interrupt;

/// <summary>
/// 中断类型辅助类 - 用于判断中断类型和分类
/// Helper class for interrupt type classification and handling
/// </summary>
public static class InterruptTypeHelper
{
    /// <summary>
    /// 检查是否为VAD（语音活动检测）中断
    /// </summary>
    /// <param name="interruptType">中断类型</param>
    /// <returns>是否为VAD中断</returns>
    public static bool IsVadInterrupt(string interruptType)
    {
        return interruptType == InterruptTypes.VoiceActivity;
    }

    /// <summary>
    /// 检查是否为手动中断（包括API、热键、手动触发）
    /// </summary>
    /// <param name="interruptType">中断类型</param>
    /// <returns>是否为手动中断</returns>
    public static bool IsManualInterrupt(string interruptType)
    {
        return interruptType == InterruptTypes.Manual || 
               interruptType == InterruptTypes.Api || 
               interruptType == InterruptTypes.Hotkey;
    }

    /// <summary>
    /// 获取中断类型的显示名称
    /// </summary>
    /// <param name="interruptType">中断类型</param>
    /// <returns>显示名称</returns>
    public static string GetDisplayName(string interruptType)
    {
        return interruptType switch
        {
            InterruptTypes.VoiceActivity => "语音活动中断",
            InterruptTypes.Manual => "手动中断",
            InterruptTypes.Api => "API中断",
            InterruptTypes.Hotkey => "热键中断",
            _ => "未知中断类型"
        };
    }

    /// <summary>
    /// 获取中断类型的优先级
    /// </summary>
    /// <param name="interruptType">中断类型</param>
    /// <returns>优先级（数字越大优先级越高）</returns>
    public static int GetPriority(string interruptType)
    {
        return interruptType switch
        {
            InterruptTypes.Manual => 9,
            InterruptTypes.Hotkey => 8,
            InterruptTypes.Api => 6,
            InterruptTypes.VoiceActivity => 4,
            _ => 1
        };
    }

    /// <summary>
    /// 获取打断类型的类别
    /// Get the category for an interrupt type
    /// </summary>
    /// <param name="interruptType">中断类型</param>
    /// <returns>中断类别</returns>
    public static string GetInterruptCategory(string interruptType)
    {
        return interruptType switch
        {
            InterruptTypes.Hotkey => InterruptCategories.Manual,
            InterruptTypes.Api => InterruptCategories.Manual,
            InterruptTypes.Manual => InterruptCategories.Manual,
            InterruptTypes.VoiceActivity => InterruptCategories.VoiceActivity,
            _ => InterruptCategories.Manual // Default to manual for other types
        };
    }
}
