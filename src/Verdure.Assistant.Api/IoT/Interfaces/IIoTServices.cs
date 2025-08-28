using Verdure.Assistant.Api.IoT.Models;

namespace Verdure.Assistant.Api.IoT.Interfaces;

/// <summary>
/// 显示服务接口
/// </summary>
public interface IDisplayService : IDisposable
{
    /// <summary>
    /// 播放表情动画
    /// </summary>
    Task PlayEmotionAsync(string emotionType, int loops = 1, int fps = 30, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 显示时间
    /// </summary>
    Task DisplayTimeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 清除屏幕
    /// </summary>
    void ClearScreen(bool is24Inch = true, ushort color = 0x0000);
    
    /// <summary>
    /// 渐变清屏
    /// </summary>
    Task FadeToBlackAsync(bool is24Inch = true, int durationMs = 500);
    
    /// <summary>
    /// 获取可用的表情类型
    /// </summary>
    IEnumerable<string> GetAvailableEmotions();
    
    /// <summary>
    /// 获取显示器状态
    /// </summary>
    DisplayStatus GetDisplayStatus(DisplayType type);
}

/// <summary>
/// 机器人动作服务接口
/// </summary>
public interface IRobotActionService : IDisposable
{
    /// <summary>
    /// 执行指定表情的动作
    /// </summary>
    Task PerformActionAsync(string emotionType, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 初始化机器人位置
    /// </summary>
    Task InitializePositionAsync();
    
    /// <summary>
    /// 移动单个关节
    /// </summary>
    Task MoveJointAsync(int channel, float angle);
    
    /// <summary>
    /// 获取关节状态
    /// </summary>
    JointStatus GetJointStatus(int channel);
    
    /// <summary>
    /// 获取所有关节状态
    /// </summary>
    IEnumerable<JointStatus> GetAllJointStatuses();
}

/// <summary>
/// 表情动作整合服务接口
/// </summary>
public interface IEmotionActionService : IDisposable
{
    /// <summary>
    /// 播放指定表情和动作
    /// </summary>
    Task<bool> PlayEmotionWithActionAsync(PlayRequest request);
    
    /// <summary>
    /// 仅播放表情动画
    /// </summary>
    Task<bool> PlayEmotionOnlyAsync(string emotionType, int loops = 1, int fps = 30, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 仅播放动作
    /// </summary>
    Task<bool> PlayActionOnlyAsync(string emotionType, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 播放随机表情和动作
    /// </summary>
    Task<bool> PlayRandomEmotionAsync(bool includeAction = true, bool includeEmotion = true, int loops = 1, int fps = 30);
    
    /// <summary>
    /// 停止当前播放
    /// </summary>
    Task StopCurrentPlaybackAsync(bool clearScreen = false);
    
    /// <summary>
    /// 清除表情屏幕
    /// </summary>
    Task ClearEmotionScreenAsync();
    
    /// <summary>
    /// 获取当前播放状态
    /// </summary>
    PlaybackState GetCurrentState();
    
    /// <summary>
    /// 初始化机器人
    /// </summary>
    Task InitializeRobotAsync();
    
    /// <summary>
    /// 获取可用的表情配置
    /// </summary>
    IEnumerable<EmotionConfig> GetAvailableEmotionConfigs();
}

/// <summary>
/// Lottie渲染器接口
/// </summary>
public interface ILottieRenderer : IDisposable
{
    /// <summary>
    /// 帧数
    /// </summary>
    uint FrameCount { get; }
    
    /// <summary>
    /// 渲染指定帧
    /// </summary>
    byte[] RenderFrame(int frameIndex, int width, int height);
    
    /// <summary>
    /// 重置播放
    /// </summary>
    void ResetPlayback();
}