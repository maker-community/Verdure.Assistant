namespace InterruptArchitectureTest.Core;

/// <summary>
/// 打断事件参数
/// </summary>
public class InterruptEventArgs : EventArgs
{
    public string InterruptType { get; }
    public string SourceName { get; }
    public string Description { get; }
    public object? Data { get; }
    public DateTime Timestamp { get; }
    public int Priority { get; set; } = 0;

    public InterruptEventArgs(string interruptType, string sourceName, string description, object? data = null, int priority = 0)
    {
        InterruptType = interruptType;
        SourceName = sourceName;
        Description = description;
        Data = data;
        Timestamp = DateTime.UtcNow;
        Priority = priority;
    }
}

/// <summary>
/// 预定义的打断类型常量
/// </summary>
public static class InterruptTypes
{
    public const string Keyword = "keyword";
    public const string Hotkey = "hotkey";
    public const string VoiceActivity = "voice_activity";
    public const string Manual = "manual";
    public const string Network = "network";
    public const string Timer = "timer";
    public const string Custom = "custom";
}
