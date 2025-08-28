namespace Verdure.Assistant.Api.IoT.Models;

/// <summary>
/// 关节状态信息
/// </summary>
public class JointStatus
{
    public int Channel { get; set; }
    public float CurrentAngle { get; set; }
    public float TargetAngle { get; set; }
    public bool IsMoving { get; set; }
    public DateTime LastUpdate { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 机器人关节常量
/// </summary>
public static class RobotJoints
{
    // 关节通道定义
    public const int LeftEar = 4;      // 左耳
    public const int RightEar = 8;     // 右耳
    public const int LeftArm = 6;      // 左臂
    public const int RightArm = 10;    // 右臂
    public const int Head = 12;        // 头部/脖子
    
    // 关节名称映射
    public static readonly Dictionary<int, string> JointNames = new()
    {
        { LeftEar, "左耳" },
        { RightEar, "右耳" },
        { LeftArm, "左臂" },
        { RightArm, "右臂" },
        { Head, "头部" }
    };
    
    /// <summary>
    /// 获取所有有效的关节通道
    /// </summary>
    public static int[] GetAllJoints()
    {
        return JointNames.Keys.ToArray();
    }
    
    /// <summary>
    /// 检查关节通道是否有效
    /// </summary>
    public static bool IsValidJoint(int channel)
    {
        return JointNames.ContainsKey(channel);
    }
    
    /// <summary>
    /// 获取关节名称
    /// </summary>
    public static string GetJointName(int channel)
    {
        return JointNames.TryGetValue(channel, out var name) ? name : $"未知关节({channel})";
    }
}

/// <summary>
/// 显示器类型
/// </summary>
public enum DisplayType
{
    Display24Inch,  // 2.4寸屏幕
    Display147Inch  // 1.47寸屏幕
}

/// <summary>
/// 显示器状态
/// </summary>
public class DisplayStatus
{
    public DisplayType Type { get; set; }
    public bool IsInitialized { get; set; }
    public bool IsDisplaying { get; set; }
    public string? CurrentContent { get; set; }
    public DateTime LastUpdate { get; set; }
}