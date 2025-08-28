using System.Runtime.InteropServices;
using Verdure.Assistant.Api.IoT.Interfaces;
using Verdure.Assistant.Api.IoT.Models;

namespace Verdure.Assistant.Api.IoT.Services;

/// <summary>
/// 机器人动作服务实现 - 控制舵机执行表情相关的动作
/// </summary>
public class RobotActionService : IRobotActionService
{
    private readonly ILogger<RobotActionService> _logger;
    private readonly Dictionary<int, JointStatus> _jointStatuses;
    private readonly Dictionary<string, EmotionConfig> _emotionConfigs;
    private readonly object _jointLock = new();
    private bool _disposed = false;
    private bool _isInitialized = false;

    public RobotActionService(ILogger<RobotActionService> logger)
    {
        _logger = logger;
        _jointStatuses = new Dictionary<int, JointStatus>();
        _emotionConfigs = InitializeEmotionConfigs();
        
        InitializeJoints();
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            InitializeHardware();
        }
        else
        {
            _logger.LogWarning("非Linux平台，机器人硬件初始化跳过");
            _isInitialized = true; // 允许在非Linux平台上运行用于测试
        }
    }

    /// <summary>
    /// 初始化关节状态
    /// </summary>
    private void InitializeJoints()
    {
        foreach (var joint in RobotJoints.GetAllJoints())
        {
            _jointStatuses[joint] = new JointStatus
            {
                Channel = joint,
                CurrentAngle = 90, // 默认中位
                TargetAngle = 90,
                IsMoving = false,
                LastUpdate = DateTime.Now,
                Name = RobotJoints.GetJointName(joint)
            };
        }
        
        _logger.LogInformation($"初始化 {_jointStatuses.Count} 个关节状态");
    }

    /// <summary>
    /// 初始化硬件（在Linux平台上）
    /// </summary>
    private void InitializeHardware()
    {
        try
        {
            // 这里应该初始化PCA9685或其他舵机控制器
            // 由于我们没有具体的硬件库依赖，这里只是模拟
            _logger.LogInformation("机器人硬件初始化开始");
            
            // 模拟硬件初始化
            Thread.Sleep(100);
            
            _isInitialized = true;
            _logger.LogInformation("机器人硬件初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "机器人硬件初始化失败");
        }
    }

    /// <summary>
    /// 初始化表情配置
    /// </summary>
    private Dictionary<string, EmotionConfig> InitializeEmotionConfigs()
    {
        return new Dictionary<string, EmotionConfig>
        {
            [EmotionTypes.Neutral] = new EmotionConfig
            {
                Type = EmotionTypes.Neutral,
                Name = "平静",
                Duration = 3000,
                ActionAngles = new Dictionary<int, float>
                {
                    { RobotJoints.LeftEar, 95 },   // 左耳轻微动作
                    { RobotJoints.RightEar, 85 },  // 右耳轻微动作
                    { RobotJoints.Head, 90 },      // 脖子中正
                    { RobotJoints.LeftArm, 90 },   // 左臂自然
                    { RobotJoints.RightArm, 90 },  // 右臂自然
                }
            },
            [EmotionTypes.Happy] = new EmotionConfig
            {
                Type = EmotionTypes.Happy,
                Name = "快乐",
                Duration = 3500,
                ActionAngles = new Dictionary<int, float>
                {
                    { RobotJoints.LeftEar, 110 },  // 左耳活跃
                    { RobotJoints.RightEar, 70 },  // 右耳活跃
                    { RobotJoints.LeftArm, 45 },   // 左臂欢快摆动
                    { RobotJoints.RightArm, 135 }, // 右臂欢快摆动
                    { RobotJoints.Head, 110 },     // 脖子快乐摆动
                }
            },
            [EmotionTypes.Sad] = new EmotionConfig
            {
                Type = EmotionTypes.Sad,
                Name = "悲伤",
                Duration = 4000,
                ActionAngles = new Dictionary<int, float>
                {
                    { RobotJoints.LeftEar, 80 },   // 左耳下垂
                    { RobotJoints.RightEar, 100 }, // 右耳下垂
                    { RobotJoints.LeftArm, 120 },  // 左臂下垂
                    { RobotJoints.RightArm, 60 },  // 右臂下垂
                    { RobotJoints.Head, 75 },      // 脖子低头
                }
            },
            [EmotionTypes.Angry] = new EmotionConfig
            {
                Type = EmotionTypes.Angry,
                Name = "愤怒",
                Duration = 4000,
                ActionAngles = new Dictionary<int, float>
                {
                    { RobotJoints.LeftEar, 115 },  // 左耳竖起
                    { RobotJoints.RightEar, 65 },  // 右耳竖起
                    { RobotJoints.LeftArm, 25 },   // 左臂张开威胁
                    { RobotJoints.RightArm, 155 }, // 右臂张开威胁
                    { RobotJoints.Head, 60 },      // 脖子威胁姿态
                }
            },
            [EmotionTypes.Surprised] = new EmotionConfig
            {
                Type = EmotionTypes.Surprised,
                Name = "惊讶",
                Duration = 3000,
                ActionAngles = new Dictionary<int, float>
                {
                    { RobotJoints.LeftEar, 120 },  // 左耳惊讶竖起
                    { RobotJoints.RightEar, 60 },  // 右耳惊讶竖起
                    { RobotJoints.LeftArm, 60 },   // 左臂惊讶张开
                    { RobotJoints.RightArm, 120 }, // 右臂惊讶张开
                    { RobotJoints.Head, 115 },     // 脖子惊讶转动
                }
            },
            [EmotionTypes.Confused] = new EmotionConfig
            {
                Type = EmotionTypes.Confused,
                Name = "困惑",
                Duration = 3500,
                ActionAngles = new Dictionary<int, float>
                {
                    { RobotJoints.LeftEar, 100 },  // 左耳困惑摆动
                    { RobotJoints.RightEar, 80 },  // 右耳困惑摆动
                    { RobotJoints.LeftArm, 80 },   // 左臂困惑姿态
                    { RobotJoints.RightArm, 100 }, // 右臂困惑姿态
                    { RobotJoints.Head, 120 },     // 脖子困惑转动
                }
            }
        };
    }

    /// <summary>
    /// 执行指定表情的动作
    /// </summary>
    public async Task PerformActionAsync(string emotionType, CancellationToken cancellationToken = default)
    {
        // 映射表情类型
        var mappedEmotion = EmotionMappingService.MapEmotion(emotionType);
        
        if (!_emotionConfigs.ContainsKey(mappedEmotion))
        {
            _logger.LogWarning($"未找到表情 {emotionType} -> {mappedEmotion} 的动作配置");
            return;
        }

        if (!_isInitialized)
        {
            _logger.LogWarning("机器人硬件未初始化，跳过动作执行");
            return;
        }

        var config = _emotionConfigs[mappedEmotion];
        _logger.LogInformation($"开始执行动作: {emotionType} -> {mappedEmotion}");

        try
        {
            // 并行移动所有相关关节
            var moveTasks = config.ActionAngles.Select(kvp => 
                MoveJointAsync(kvp.Key, kvp.Value, cancellationToken)).ToArray();

            await Task.WhenAll(moveTasks);
            
            _logger.LogInformation($"动作 {mappedEmotion} 执行完成");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation($"动作 {mappedEmotion} 执行被取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"动作 {mappedEmotion} 执行失败");
            throw;
        }
    }

    /// <summary>
    /// 初始化机器人位置
    /// </summary>
    public async Task InitializePositionAsync()
    {
        _logger.LogInformation("初始化机器人位置到中位");

        try
        {
            // 所有关节移动到90度中位
            var initTasks = RobotJoints.GetAllJoints()
                .Select(joint => MoveJointAsync(joint, 90))
                .ToArray();

            await Task.WhenAll(initTasks);
            
            _logger.LogInformation("机器人位置初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "机器人位置初始化失败");
            throw;
        }
    }

    /// <summary>
    /// 移动单个关节
    /// </summary>
    public async Task MoveJointAsync(int channel, float angle)
    {
        await MoveJointAsync(channel, angle, CancellationToken.None);
    }

    /// <summary>
    /// 移动单个关节（内部实现）
    /// </summary>
    private async Task MoveJointAsync(int channel, float angle, CancellationToken cancellationToken = default)
    {
        if (!RobotJoints.IsValidJoint(channel))
        {
            _logger.LogWarning($"无效的关节通道: {channel}");
            return;
        }

        // 限制角度范围
        angle = Math.Clamp(angle, 0, 180);

        lock (_jointLock)
        {
            var status = _jointStatuses[channel];
            status.TargetAngle = angle;
            status.IsMoving = true;
            status.LastUpdate = DateTime.Now;
        }

        try
        {
            _logger.LogDebug($"移动关节 {RobotJoints.GetJointName(channel)} 到 {angle}°");

            // 模拟舵机移动时间（实际实现中会调用硬件API）
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // 这里应该调用PCA9685或其他舵机控制器的API
                // 目前只是模拟移动
                await SimulateServoMovement(channel, angle, cancellationToken);
            }
            else
            {
                // 非Linux平台上只是延时模拟
                await Task.Delay(500, cancellationToken);
            }

            lock (_jointLock)
            {
                var status = _jointStatuses[channel];
                status.CurrentAngle = angle;
                status.IsMoving = false;
                status.LastUpdate = DateTime.Now;
            }
        }
        catch (OperationCanceledException)
        {
            lock (_jointLock)
            {
                _jointStatuses[channel].IsMoving = false;
            }
            _logger.LogDebug($"关节 {channel} 移动被取消");
        }
        catch (Exception ex)
        {
            lock (_jointLock)
            {
                _jointStatuses[channel].IsMoving = false;
            }
            _logger.LogError(ex, $"关节 {channel} 移动失败");
        }
    }

    /// <summary>
    /// 模拟舵机移动（实际硬件实现中替换为真实的硬件调用）
    /// </summary>
    private async Task SimulateServoMovement(int channel, float angle, CancellationToken cancellationToken)
    {
        // 计算移动时间（根据角度差异）
        var currentAngle = _jointStatuses[channel].CurrentAngle;
        var angleDiff = Math.Abs(angle - currentAngle);
        var moveTime = (int)(angleDiff * 10); // 每度10ms

        await Task.Delay(Math.Min(moveTime, 2000), cancellationToken); // 最大2秒
    }

    /// <summary>
    /// 获取关节状态
    /// </summary>
    public JointStatus GetJointStatus(int channel)
    {
        lock (_jointLock)
        {
            return _jointStatuses.TryGetValue(channel, out var status) 
                ? new JointStatus
                {
                    Channel = status.Channel,
                    CurrentAngle = status.CurrentAngle,
                    TargetAngle = status.TargetAngle,
                    IsMoving = status.IsMoving,
                    LastUpdate = status.LastUpdate,
                    Name = status.Name
                }
                : new JointStatus { Channel = channel, Name = "未知关节" };
        }
    }

    /// <summary>
    /// 获取所有关节状态
    /// </summary>
    public IEnumerable<JointStatus> GetAllJointStatuses()
    {
        lock (_jointLock)
        {
            return _jointStatuses.Values.Select(status => new JointStatus
            {
                Channel = status.Channel,
                CurrentAngle = status.CurrentAngle,
                TargetAngle = status.TargetAngle,
                IsMoving = status.IsMoving,
                LastUpdate = status.LastUpdate,
                Name = status.Name
            }).ToList();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            try
            {
                // 回到中位
                var resetTasks = RobotJoints.GetAllJoints()
                    .Select(joint => MoveJointAsync(joint, 90))
                    .ToArray();

                Task.WaitAll(resetTasks, TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "机器人关闭时复位失败");
            }

            _logger.LogInformation("机器人动作服务已释放资源");
            _disposed = true;
        }
    }
}