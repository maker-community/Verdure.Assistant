namespace Verdure.Assistant.Api.Models;

/// <summary>
/// 机器人关节状态
/// </summary>
public class JointStatus
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public float ServoAngleMin { get; set; }
    public float ServoAngleMax { get; set; }
    public float ModelAngleMin { get; set; }
    public float ModelAngleMax { get; set; }
    public bool IsInverted { get; set; }
    public int I2cAddress { get; set; }

    /// <summary>
    /// 将模型角度转换为舵机角度
    /// </summary>
    public float ConvertModelAngleToServoAngle(float modelAngle)
    {
        // 确保模型角度在有效范围内
        modelAngle = Math.Max(ModelAngleMin, Math.Min(ModelAngleMax, modelAngle));
        
        // 计算模型角度在其范围内的比例
        float modelRange = ModelAngleMax - ModelAngleMin;
        float modelRatio = (modelAngle - ModelAngleMin) / modelRange;
        
        // 如果反向，翻转比例
        if (IsInverted)
            modelRatio = 1.0f - modelRatio;
        
        // 映射到舵机角度范围
        float servoRange = ServoAngleMax - ServoAngleMin;
        return ServoAngleMin + (servoRange * modelRatio);
    }

    /// <summary>
    /// 将舵机角度转换为模型角度
    /// </summary>
    public float ConvertServoAngleToModelAngle(float servoAngle)
    {
        // 确保舵机角度在有效范围内
        servoAngle = Math.Max(ServoAngleMin, Math.Min(ServoAngleMax, servoAngle));
        
        // 计算舵机角度在其范围内的比例
        float servoRange = ServoAngleMax - ServoAngleMin;
        float servoRatio = (servoAngle - ServoAngleMin) / servoRange;
        
        // 如果反向，翻转比例
        if (IsInverted)
            servoRatio = 1.0f - servoRatio;
        
        // 映射到模型角度范围
        float modelRange = ModelAngleMax - ModelAngleMin;
        return ModelAngleMin + (modelRange * servoRatio);
    }
}