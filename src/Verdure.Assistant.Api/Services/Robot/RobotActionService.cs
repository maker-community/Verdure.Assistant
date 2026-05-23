using System.Device.I2c;
using Verdure.Assistant.Api.Models;

namespace Verdure.Assistant.Api.Services.Robot;

/// <summary>
/// 机器人动作控制服务
/// </summary>
public class RobotActionService : IDisposable
{
    private readonly Dictionary<int, I2cDevice> _i2cDevices;
    private readonly Dictionary<int, JointStatus> _joints;
    private readonly byte[] _i2cTxData = new byte[5];
    private readonly byte[] _i2cRxData = new byte[5];
    private readonly ILogger<RobotActionService> _logger;

    public RobotActionService(ILogger<RobotActionService> logger)
    {
        _logger = logger;
        _i2cDevices = new Dictionary<int, I2cDevice>();
        _joints = new Dictionary<int, JointStatus>();
        
        InitializeJoints();
        InitializeI2cDevices();
    }

    /// <summary>
    /// 初始化关节配置
    /// </summary>
    private void InitializeJoints()
    {
        _joints[2] = new JointStatus
        {
            Id = 2,
            Name = "头部上下",
            ServoAngleMin = 60,
            ServoAngleMax = 105,
            ModelAngleMin = -20,
            ModelAngleMax = 20,
            IsInverted = true,
            I2cAddress = 0x01
        };

        _joints[6] = new JointStatus
        {
            Id = 6,
            Name = "左臂旋转",
            ServoAngleMin = 0,
            ServoAngleMax = 180,
            ModelAngleMin = 0,
            ModelAngleMax = 180,
            IsInverted = false,
            I2cAddress = 0x03
        };

        _joints[10] = new JointStatus
        {
            Id = 10,
            Name = "右臂旋转",
            ServoAngleMin = 0,
            ServoAngleMax = 180,
            ModelAngleMin = 0,
            ModelAngleMax = 180,
            IsInverted = true,
            I2cAddress = 0x05
        };

        _joints[12] = new JointStatus
        {
            Id = 12,
            Name = "脖子旋转",
            ServoAngleMin = 0,
            ServoAngleMax = 180,
            ModelAngleMin = -90,
            ModelAngleMax = 90,
            IsInverted = false,
            I2cAddress = 0x06
        };
    }

    /// <summary>
    /// 初始化I2C设备
    /// </summary>
    private void InitializeI2cDevices()
    {
        var uniqueAddresses = _joints.Values.Select(j => j.I2cAddress).Distinct().OrderBy(x => x).ToList();
        
        foreach (var address in uniqueAddresses)
        {
            try
            {
                var i2cDevice = I2cDevice.Create(new I2cConnectionSettings(7, address));
                _i2cDevices[address] = i2cDevice;
                _logger.LogInformation($"初始化I2C设备地址 0x{address:X2} 成功");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"初始化I2C设备地址 0x{address:X2} 失败: {ex.Message}");
            }
        }

        // 显示关节与I2C设备的映射
        foreach (var joint in _joints.Values.OrderBy(j => j.Id))
        {
            var deviceStatus = _i2cDevices.ContainsKey(joint.I2cAddress) ? "可用" : "不可用";
            _logger.LogInformation($"关节 {joint.Name} (ID: {joint.Id}) → I2C地址 0x{joint.I2cAddress:X2} ({deviceStatus})");
        }
    }

    /// <summary>
    /// 启用或禁用指定关节
    /// </summary>
    /// <param name="jointId">关节ID</param>
    /// <param name="enable">是否启用</param>
    public async Task<bool> SetJointEnableAsync(int jointId, bool enable, CancellationToken cancellationToken = default)
    {
        if (!_joints.ContainsKey(jointId))
        {
            _logger.LogWarning($"关节 ID {jointId} 不存在");
            return false;
        }

        var joint = _joints[jointId];
        if (!_i2cDevices.ContainsKey(joint.I2cAddress))
        {
            _logger.LogWarning($"关节 {joint.Name} 对应的I2C设备地址 0x{joint.I2cAddress:X2} 不可用");
            return false;
        }

        var device = _i2cDevices[joint.I2cAddress];

        _i2cTxData[0] = 0xff;
        _i2cTxData[1] = enable ? (byte)1 : (byte)0;
        _i2cTxData[2] = 0x00;
        _i2cTxData[3] = 0x00;
        _i2cTxData[4] = 0x00;

        return await Task.Run(() => TransmitAndReceiveI2cPacket(device, joint), cancellationToken);
    }

    /// <summary>
    /// 设置关节的模型角度（自动转换为舵机角度）
    /// </summary>
    /// <param name="jointId">关节ID</param>
    /// <param name="modelAngle">模型角度</param>
    public async Task<bool> SetJointModelAngleAsync(int jointId, float modelAngle, CancellationToken cancellationToken = default)
    {
        if (!_joints.ContainsKey(jointId))
        {
            _logger.LogWarning($"关节 ID {jointId} 不存在");
            return false;
        }

        var joint = _joints[jointId];
        if (!_i2cDevices.ContainsKey(joint.I2cAddress))
        {
            _logger.LogWarning($"关节 {joint.Name} 对应的I2C设备地址 0x{joint.I2cAddress:X2} 不可用");
            return false;
        }

        var device = _i2cDevices[joint.I2cAddress];

        // 转换模型角度为舵机角度
        float servoAngle = joint.ConvertModelAngleToServoAngle(modelAngle);

        _logger.LogDebug($"关节 {joint.Name}: 模型角度 {modelAngle}° → 舵机角度 {servoAngle:F2}°");

        // 发送舵机角度
        byte[] angleBytes = BitConverter.GetBytes(servoAngle);
        _i2cTxData[0] = 0x01;
        Array.Copy(angleBytes, 0, _i2cTxData, 1, angleBytes.Length);

        return await Task.Run(() => TransmitAndReceiveI2cPacket(device, joint), cancellationToken);
    }

    /// <summary>
    /// 同时设置多个关节的模型角度
    /// </summary>
    /// <param name="jointAngles">关节ID和对应的模型角度字典</param>
    public async Task SetMultipleJointAnglesAsync(Dictionary<int, float> jointAngles, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug($"开始设置 {jointAngles.Count} 个关节角度...");
        
        foreach (var kvp in jointAngles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SetJointModelAngleAsync(kvp.Key, kvp.Value, cancellationToken);
            await Task.Delay(10, cancellationToken); // 小延时，避免I2C总线冲突
        }
        
        _logger.LogDebug("多关节角度设置完成");
    }

    /// <summary>
    /// 执行预定义的动作序列
    /// </summary>
    public async Task PerformActionAsync(string emotionType, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"执行动作: {emotionType}");

        if (!EmotionTypes.IsValid(emotionType))
        {
            _logger.LogWarning($"无效的动作类型: {emotionType}");
            return;
        }

        switch (emotionType.ToLower())
        {
            case EmotionTypes.Neutral:
                await PerformNeutralActionAsync(cancellationToken);
                break;
                
            case EmotionTypes.Happy:
                await PerformHappyActionAsync(cancellationToken);
                break;
                
            case EmotionTypes.Sad:
                await PerformSadActionAsync(cancellationToken);
                break;

            case EmotionTypes.Angry:
                await PerformAngryActionAsync(cancellationToken);
                break;
                
            case EmotionTypes.Surprised:
                await PerformSurprisedActionAsync(cancellationToken);
                break;
                
            case EmotionTypes.Confused:
                await PerformConfusedActionAsync(cancellationToken);
                break;

            default:
                _logger.LogWarning($"未知动作类型: {emotionType}");
                break;
        }
    }

    /// <summary>
    /// 执行中性/放松动作 - 轻柔的自然动作
    /// </summary>
    public async Task PerformRelaxActionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("开始执行放松动作序列");
        
        // 轻柔的头部与脖子活动
        for (int i = 0; i < 2; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 2, 5 },     // 头部轻微上仰
                { 12, -15 }   // 脖子轻柔向左
            }, cancellationToken);
            await Task.Delay(800, cancellationToken);

            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 2, -5 },    // 头部轻微下低
                { 12, 15 }    // 脖子轻柔向右
            }, cancellationToken);
            await Task.Delay(800, cancellationToken);
        }

        // 缓慢的手臂舒展
        await SetMultipleJointAnglesAsync(new Dictionary<int, float>
        {
            { 6, 60 },    // 左臂轻微张开
            { 10, 120 },  // 右臂轻微张开
            { 12, 0 }     // 脖子回正
        }, cancellationToken);
        await Task.Delay(1000, cancellationToken);

        // 回到放松状态
        await SetMultipleJointAnglesAsync(new Dictionary<int, float>
        {
            { 2, 0 },    // 头部中位
            { 6, 90 },   // 左臂中位
            { 10, 90 },  // 右臂中位
            { 12, 0 }    // 脖子居中
        }, cancellationToken);
        
        _logger.LogDebug("放松动作序列执行完成");
    }

    /// <summary>
    /// 执行愤怒动作
    /// </summary>
    private async Task PerformAngryActionAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("开始执行愤怒动作序列");
        
        // 第一阶段：威胁姿态 - 双臂张开，头部前倾，脖子向前
        await SetMultipleJointAnglesAsync(new Dictionary<int, float>
        {
            { 2, 18 },    // 头部前倾（威胁）
            { 6, 30 },    // 左臂向前威胁
            { 10, 150 },  // 右臂向前威胁
            { 12, -30 }   // 脖子稍微向左转
        }, cancellationToken);

        await Task.Delay(500, cancellationToken);

        // 第二阶段：激烈的脖子旋转 + 头部晃动 + 手臂挥舞
        for (int i = 0; i < 4; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // 脖子快速左右旋转 + 手臂挥舞
            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 12, -60 },  // 脖子向左转
                { 6, 0 },     // 左臂快速挥动
                { 10, 180 }   // 右臂快速挥动
            }, cancellationToken);
            await Task.Delay(150, cancellationToken);

            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 12, 60 },   // 脖子向右转
                { 6, 60 },    // 左臂挥动
                { 10, 120 }   // 右臂挥动
            }, cancellationToken);
            await Task.Delay(150, cancellationToken);
        }

        // 第三阶段：最后的威胁 - 脖子居中，手臂交叉威胁
        await SetMultipleJointAnglesAsync(new Dictionary<int, float>
        {
            { 2, 18 },    // 头部保持前倾
            { 12, 0 },    // 脖子正面对准
            { 6, 45 },    // 左臂交叉
            { 10, 135 }   // 右臂交叉
        }, cancellationToken);
        
        await Task.Delay(800, cancellationToken);

        // 回到中位
        await SetMultipleJointAnglesAsync(new Dictionary<int, float>
        {
            { 2, 0 },    // 头部中位
            { 6, 90 },   // 左臂中位
            { 10, 90 },  // 右臂中位
            { 12, 0 }    // 脖子居中
        }, cancellationToken);
        
        _logger.LogDebug("愤怒动作序列执行完成");
    }

    /// <summary>
    /// 执行快乐动作
    /// </summary>
    private async Task PerformHappyActionAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("开始执行快乐动作序列");
        
        // 第一阶段：欢迎姿态 - 双臂张开欢迎，头部上扬
        await SetMultipleJointAnglesAsync(new Dictionary<int, float>
        {
            { 2, 18 },    // 头部上扬（欢乐）
            { 6, 45 },    // 左臂张开
            { 10, 135 },  // 右臂张开
            { 12, 0 }     // 脖子正面
        }, cancellationToken);

        await Task.Delay(400, cancellationToken);

        // 第二阶段：左右挥手 + 脖子轻柔左右摆动 + 耳朵活动
        for (int i = 0; i < 3; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // 向左挥手 + 脖子配合转动
            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 6, 0 },     // 左臂向左挥
                { 10, 180 },  // 右臂向右挥
                { 12, -25 },  // 脖子轻柔向左转
                { 2, 12 }      // 头部轻微上仰
            }, cancellationToken);
            await Task.Delay(400, cancellationToken);

            // 向右挥手 + 脖子配合转动
            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 6, 90 },    // 左臂向中间
                { 10, 90 },   // 右臂向中间
                { 12, 25 },   // 脖子轻柔向右转
                { 2, -12 }     // 头部轻微低头
            }, cancellationToken);
            await Task.Delay(400, cancellationToken);
        }

        // 第三阶段：点头表示友好
        for (int i = 0; i < 3; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // 头部向下点头
            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 2, -18 },   // 头部向下点头
                { 12, -10 }   // 脖子稍微配合
            }, cancellationToken);
            await Task.Delay(250, cancellationToken);
            
            // 头部向上
            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 2, 15 },     // 头部向上
                { 12, 10 }    // 脖子稍微配合
            }, cancellationToken);
            await Task.Delay(250, cancellationToken);
        }

        // 第四阶段：最后的庆祝 - 双臂高举，快乐摆动
        await SetMultipleJointAnglesAsync(new Dictionary<int, float>
        {
            { 2, 18 },    // 头部上扬（庆祝）
            { 6, 15 },    // 左臂高举
            { 10, 165 }   // 右臂高举
        }, cancellationToken);

        // 快乐的脖子摆动
        for (int i = 0; i < 2; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SetJointModelAngleAsync(12, -40, cancellationToken);
            await Task.Delay(200, cancellationToken);
            await SetJointModelAngleAsync(12, 40, cancellationToken);
            await Task.Delay(200, cancellationToken);
        }

        // 回到中位
        await SetMultipleJointAnglesAsync(new Dictionary<int, float>
        {
            { 2, 0 },    // 头部中位
            { 6, 90 },   // 左臂中位
            { 10, 90 },  // 右臂中位
            { 12, 0 }    // 脖子居中
        }, cancellationToken);
        
        _logger.LogDebug("快乐动作序列执行完成");
    }

    /// <summary>
    /// 初始化动作 - 所有关节回到初始位置
    /// </summary>
    public async Task InitializePositionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("执行初始化动作");

        // 启用所有关节
        foreach (var jointId in _joints.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SetJointEnableAsync(jointId, true, cancellationToken);
            await Task.Delay(100, cancellationToken);
        }

        // 设置初始位置
        await SetMultipleJointAnglesAsync(new Dictionary<int, float>
        {
            { 2, 0 },    // 头部中位（上下）
            { 6, 90 },   // 左臂中位
            { 10, 90 },  // 右臂中位
            { 12, 0 }    // 脖子居中
        }, cancellationToken);
    }

    /// <summary>
    /// I2C通信底层实现
    /// </summary>
    private bool TransmitAndReceiveI2cPacket(I2cDevice device, JointStatus joint)
    {
        int retryCount = 3;
        
        while (retryCount > 0)
        {
            try
            {
                device.WriteRead(_i2cTxData, _i2cRxData);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"关节 {joint.Name} I2C通信失败: {ex.Message}");
                retryCount--;
                Thread.Sleep(10);
            }
        }

        _logger.LogError($"关节 {joint.Name} I2C通信最终失败");
        return false;
    }

    /// <summary>
    /// 获取所有关节信息
    /// </summary>
    /// <returns>关节信息字典</returns>
    public Dictionary<int, JointStatus> GetAllJoints()
    {
        return new Dictionary<int, JointStatus>(_joints);
    }

    /// <summary>
    /// 执行中性/平静动作 - 轻柔的自然动作
    /// </summary>
    private async Task PerformNeutralActionAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("开始执行平静动作序列");
        
        // 轻柔的头部与脖子活动
        for (int i = 0; i < 2; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 2, -14 },    // 头部轻微下低
                { 12, -10 }   // 脖子轻柔向左
            }, cancellationToken);
            await Task.Delay(1000, cancellationToken);

            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 2, 14 },     // 头部轻微上仰
                { 12, 10 }    // 脖子轻柔向右
            }, cancellationToken);
            await Task.Delay(1000, cancellationToken);
        }

        // 缓慢回到中性位置
        await InitializePositionAsync(cancellationToken);
        await Task.Delay(500, cancellationToken);
    }

    /// <summary>
    /// 执行悲伤动作 - 低沉、缓慢的动作，模拟沮丧状态
    /// </summary>
    private async Task PerformSadActionAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("开始执行悲伤动作序列");
        
        // 第一阶段：头部低垂，脖子低垂
        await SetMultipleJointAnglesAsync(new Dictionary<int, float>
        {
            { 2, -20 },   // 头部下低（悲伤）
            { 12, -20 },  // 脖子低垂
            { 6, 120 },   // 左臂下垂
            { 10, 60 }    // 右臂下垂
        }, cancellationToken);
        await Task.Delay(2000, cancellationToken);

        // 第二阶段：缓慢的摇摆，表现沮丧
        for (int i = 0; i < 3; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 12, -30 },  // 脖子左倾
                { 6, 135 },   // 左臂更加下垂
            }, cancellationToken);
            await Task.Delay(1500, cancellationToken);

            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 12, -10 },  // 脖子回到中心
                { 6, 120 },   // 左臂回到下垂位置
            }, cancellationToken);
            await Task.Delay(1500, cancellationToken);
        }

        // 缓慢回到中性位置
        await InitializePositionAsync(cancellationToken);
    }

    /// <summary>
    /// 执行惊讶动作 - 突然的、快速的动作，表现震惊
    /// </summary>
    private async Task PerformSurprisedActionAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("开始执行惊讶动作序列");
        
        // 第一阶段：突然的震惊姿态
        await SetMultipleJointAnglesAsync(new Dictionary<int, float>
        {
            { 2, 20 },    // 头部上仰（惊讶）
            { 6, 60 },    // 左臂张开
            { 10, 120 },  // 右臂张开
            { 12, 25 }    // 脖子惊讶转动
        }, cancellationToken);
        await Task.Delay(800, cancellationToken);

        // 第二阶段：快速的左右张望
        for (int i = 0; i < 4; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 12, -35 },  // 脖子快速左转
                { 2, 14 }      // 头部保持上仰
            }, cancellationToken);
            await Task.Delay(300, cancellationToken);

            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 12, 35 },   // 脖子快速右转
            }, cancellationToken);
            await Task.Delay(300, cancellationToken);
        }

        // 回到惊讶的中心姿态
        await SetMultipleJointAnglesAsync(new Dictionary<int, float>
        {
            { 12, 0 },    // 脖子回中心
            { 2, 18 }     // 头部保持上仰
        }, cancellationToken);
        await Task.Delay(1000, cancellationToken);

        // 缓慢回到中性位置
        await InitializePositionAsync(cancellationToken);
    }

    /// <summary>
    /// 执行困惑动作 - 迟疑、思考性的动作
    /// </summary>
    private async Task PerformConfusedActionAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("开始执行困惑动作序列");
        
        // 第一阶段：困惑的头部倾斜
        await SetMultipleJointAnglesAsync(new Dictionary<int, float>
        {
            { 2, -16 },    // 头部轻微低头（困惑）
            { 12, 30 },   // 脖子困惑地向右倾斜
            { 6, 80 },    // 左臂困惑姿态
            { 10, 100 }   // 右臂困惑姿态
        }, cancellationToken);
        await Task.Delay(1500, cancellationToken);

        // 第二阶段：思考性的左右摇摆
        for (int i = 0; i < 3; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 12, -25 },  // 脖子向左思考
                { 2, -12 }     // 头部低头
            }, cancellationToken);
            await Task.Delay(1200, cancellationToken);

            await SetMultipleJointAnglesAsync(new Dictionary<int, float>
            {
                { 12, 25 },   // 脖子向右思考
                { 2, -16 }     // 头部略微低头
            }, cancellationToken);
            await Task.Delay(1200, cancellationToken);
        }

        // 第三阶段：最终的困惑摇头
        await SetMultipleJointAnglesAsync(new Dictionary<int, float>
        {
            { 12, 0 },    // 脖子回中心
            { 2, -12 }     // 头部轻微低头
        }, cancellationToken);
        await Task.Delay(1000, cancellationToken);

        // 缓慢回到中性位置
        await InitializePositionAsync(cancellationToken);
    }

    public void Dispose()
    {
        foreach (var device in _i2cDevices.Values)
        {
            device?.Dispose();
        }
        _i2cDevices.Clear();
        _logger.LogInformation("机器人动作控制服务已释放资源");
    }
}