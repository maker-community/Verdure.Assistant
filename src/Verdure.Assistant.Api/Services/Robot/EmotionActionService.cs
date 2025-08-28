using Verdure.Assistant.Api.Models;

namespace Verdure.Assistant.Api.Services.Robot;

/// <summary>
/// 情感和动作整合服务 - 统一管理表情播放和动作控制
/// </summary>
public class EmotionActionService : IDisposable
{
    private readonly DisplayService _displayService;
    private readonly RobotActionService _robotActionService;
    private readonly ILogger<EmotionActionService> _logger;
    
    private readonly Dictionary<string, EmotionConfig> _emotionConfigs;
    private readonly Random _random = new();
    
    // 播放状态管理
    private CancellationTokenSource? _currentPlaybackCts;
    private PlaybackState _currentState = new() { Status = PlaybackStatus.Stopped };
    private readonly object _stateLock = new();

    public EmotionActionService(
        DisplayService displayService, 
        RobotActionService robotActionService,
        ILogger<EmotionActionService> logger)
    {
        _displayService = displayService;
        _robotActionService = robotActionService;
        _logger = logger;
        _emotionConfigs = InitializeEmotionConfigs();
        
        _logger.LogInformation("情感动作服务初始化完成");
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
                LottieFile = "neutral.mp4.lottie.json",
                Duration = 3000,
                ActionAngles = new Dictionary<int, float>
                {
                    { 4, 5 },     // 左耳轻微动作
                    { 8, 5 },     // 右耳轻微动作
                    { 12, 10 },   // 脖子轻柔摆动
                }
            },
            [EmotionTypes.Happy] = new EmotionConfig
            {
                Type = EmotionTypes.Happy,
                Name = "快乐",
                LottieFile = "happy.mp4.lottie.json",
                Duration = 3500,
                ActionAngles = new Dictionary<int, float>
                {
                    { 4, 15 },    // 左耳活跃
                    { 8, 15 },    // 右耳活跃
                    { 6, 45 },    // 左臂欢快摆动
                    { 10, 135 },  // 右臂欢快摆动
                    { 12, 20 },   // 脖子快乐摆动
                }
            },
            [EmotionTypes.Sad] = new EmotionConfig
            {
                Type = EmotionTypes.Sad,
                Name = "悲伤",
                LottieFile = "sad.mp4.lottie.json",
                Duration = 4000,
                ActionAngles = new Dictionary<int, float>
                {
                    { 4, -10 },   // 左耳下垂
                    { 8, -10 },   // 右耳下垂
                    { 6, 120 },   // 左臂下垂
                    { 10, 60 },   // 右臂下垂
                    { 12, -15 },  // 脖子低头
                }
            },
            [EmotionTypes.Angry] = new EmotionConfig
            {
                Type = EmotionTypes.Angry,
                Name = "愤怒",
                LottieFile = "angry.mp4.lottie.json",
                Duration = 4000,
                ActionAngles = new Dictionary<int, float>
                {
                    { 4, 25 },    // 左耳竖起
                    { 8, 25 },    // 右耳竖起
                    { 6, 25 },    // 左臂张开威胁
                    { 10, 155 },  // 右臂张开威胁
                    { 12, -30 },  // 脖子威胁姿态
                }
            },
            [EmotionTypes.Surprised] = new EmotionConfig
            {
                Type = EmotionTypes.Surprised,
                Name = "惊讶",
                LottieFile = "surprised.mp4.lottie.json",
                Duration = 3000,
                ActionAngles = new Dictionary<int, float>
                {
                    { 4, 30 },    // 左耳惊讶竖起
                    { 8, 30 },    // 右耳惊讶竖起
                    { 6, 60 },    // 左臂惊讶张开
                    { 10, 120 },  // 右臂惊讶张开
                    { 12, 25 },   // 脖子惊讶转动
                }
            },
            [EmotionTypes.Confused] = new EmotionConfig
            {
                Type = EmotionTypes.Confused,
                Name = "困惑",
                LottieFile = "confused.mp4.lottie.json",
                Duration = 3500,
                ActionAngles = new Dictionary<int, float>
                {
                    { 4, 10 },    // 左耳困惑摆动
                    { 8, -10 },   // 右耳困惑摆动
                    { 6, 80 },    // 左臂困惑姿态
                    { 10, 100 },  // 右臂困惑姿态
                    { 12, 30 },   // 脖子困惑转动
                }
            }
        };
    }

    /// <summary>
    /// 播放指定表情和动作 (同步执行)
    /// </summary>
    public async Task<bool> PlayEmotionWithActionAsync(PlayRequest request)
    {
        // 停止当前播放，但不清屏（避免黑屏闪烁）
        await StopCurrentPlaybackAsync(clearScreen: false);

        var emotionType = !string.IsNullOrWhiteSpace(request.EmotionType) ? request.EmotionType : GetRandomEmotion();
        
        // 验证表情类型
        if (!EmotionTypes.IsValid(emotionType))
        {
            _logger.LogError($"无效的表情类型: {emotionType}");
            return false;
        }
        
        lock (_stateLock)
        {
            _currentPlaybackCts = new CancellationTokenSource();
            _currentState = new PlaybackState
            {
                Status = PlaybackStatus.Playing,
                CurrentEmotion = emotionType,
                StartTime = DateTime.Now,
                CurrentLoop = 0
            };
        }

        _logger.LogInformation($"开始播放情感 {emotionType}，包含动作: {request.IncludeAction}，包含表情: {request.IncludeEmotion}");

        try
        {
            var cancellationToken = _currentPlaybackCts.Token;
            
            // 并行执行表情和动作
            var tasks = new List<Task>();
            
            if (request.IncludeEmotion)
            {
                tasks.Add(PlayEmotionOnlyAsync(emotionType, request.Loops, request.Fps, cancellationToken));
            }
            
            if (request.IncludeAction)
            {
                tasks.Add(PlayActionOnlyAsync(emotionType, cancellationToken));
            }
            
            // 等待所有任务完成
            await Task.WhenAll(tasks);
            
            lock (_stateLock)
            {
                _currentState.Status = PlaybackStatus.Stopped;
            }
            
            _logger.LogInformation($"情感 {emotionType} 播放完成");
            return true;
        }
        catch (OperationCanceledException)
        {
            lock (_stateLock)
            {
                _currentState.Status = PlaybackStatus.Cancelled;
                _currentState.ErrorMessage = "播放被取消";
            }
            _logger.LogInformation($"情感 {emotionType} 播放被取消");
            return false;
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _currentState.Status = PlaybackStatus.Stopped;
                _currentState.ErrorMessage = ex.Message;
            }
            _logger.LogError(ex, $"情感 {emotionType} 播放发生错误");
            return false;
        }
    }

    /// <summary>
    /// 仅播放表情动画
    /// </summary>
    public async Task<bool> PlayEmotionOnlyAsync(string emotionType, int loops = 1, int fps = 30, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"开始播放表情动画: {emotionType}");
        
        if (!EmotionTypes.IsValid(emotionType))
        {
            _logger.LogError($"无效的表情类型: {emotionType}");
            return false;
        }
        
        try
        {
            await _displayService.PlayEmotionAsync(emotionType, loops, fps, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation($"表情动画 {emotionType} 播放被取消");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"表情动画 {emotionType} 播放失败");
            return false;
        }
    }

    /// <summary>
    /// 仅播放动作
    /// </summary>
    public async Task<bool> PlayActionOnlyAsync(string emotionType, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"开始播放动作: {emotionType}");
        
        if (!EmotionTypes.IsValid(emotionType))
        {
            _logger.LogError($"无效的表情类型: {emotionType}");
            return false;
        }
        
        try
        {
            await _robotActionService.PerformActionAsync(emotionType, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation($"动作 {emotionType} 播放被取消");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"动作 {emotionType} 播放失败");
            return false;
        }
    }

    /// <summary>
    /// 播放随机表情和动作
    /// </summary>
    public async Task<bool> PlayRandomEmotionAsync(bool includeAction = true, bool includeEmotion = true, int loops = 1, int fps = 30)
    {
        var randomEmotion = GetRandomEmotion();
        var request = new PlayRequest
        {
            EmotionType = randomEmotion,
            IncludeAction = includeAction,
            IncludeEmotion = includeEmotion,
            Loops = loops,
            Fps = fps
        };
        
        return await PlayEmotionWithActionAsync(request);
    }

    /// <summary>
    /// 停止当前播放
    /// </summary>
    /// <param name="clearScreen">是否清除屏幕</param>
    public async Task StopCurrentPlaybackAsync(bool clearScreen = false)
    {
        CancellationTokenSource? ctsToCancel = null;
        
        lock (_stateLock)
        {
            if (_currentPlaybackCts != null && !_currentPlaybackCts.IsCancellationRequested)
            {
                ctsToCancel = _currentPlaybackCts;
                _currentState.Status = PlaybackStatus.Cancelled;
            }
        }
        
        if (ctsToCancel != null)
        {
            _logger.LogInformation($"停止当前播放 (清屏: {clearScreen})");
            ctsToCancel.Cancel();
            
            // 等待一小段时间让任务响应取消
            await Task.Delay(100);
            
            // 仅在明确要求时才清屏
            if (clearScreen)
            {
                _displayService.ClearScreen(true);
            }
        }
    }

    /// <summary>
    /// 清除表情屏幕
    /// </summary>
    public async Task ClearEmotionScreenAsync()
    {
        _logger.LogInformation("清除表情屏幕");
        await StopCurrentPlaybackAsync(clearScreen: true);
    }

    /// <summary>
    /// 获取当前播放状态
    /// </summary>
    public PlaybackState GetCurrentState()
    {
        lock (_stateLock)
        {
            return new PlaybackState
            {
                Status = _currentState.Status,
                CurrentEmotion = _currentState.CurrentEmotion,
                StartTime = _currentState.StartTime,
                CurrentLoop = _currentState.CurrentLoop,
                ErrorMessage = _currentState.ErrorMessage
            };
        }
    }

    /// <summary>
    /// 获取随机表情类型
    /// </summary>
    private string GetRandomEmotion()
    {
        var availableEmotions = _displayService.GetAvailableEmotions().ToArray();
        if (availableEmotions.Length == 0)
        {
            _logger.LogWarning("没有可用的表情，返回默认表情");
            return EmotionTypes.Happy;
        }
        
        return availableEmotions[_random.Next(availableEmotions.Length)];
    }

    /// <summary>
    /// 获取可用的表情配置
    /// </summary>
    public IEnumerable<EmotionConfig> GetAvailableEmotionConfigs()
    {
        var availableEmotions = _displayService.GetAvailableEmotions();
        return availableEmotions.Select(e => _emotionConfigs[e]).ToList();
    }

    /// <summary>
    /// 初始化机器人位置
    /// </summary>
    public async Task InitializeRobotAsync()
    {
        _logger.LogInformation("初始化机器人位置");
        await _robotActionService.InitializePositionAsync();
    }

    /// <summary>
    /// 演示所有功能
    /// </summary>
    public async Task RunDemoAsync()
    {
        _logger.LogInformation("=== 开始演示程序 ===");
        
        try
        {
            // 1. 初始化机器人
            _logger.LogInformation("1. 初始化机器人位置");
            await InitializeRobotAsync();
            await Task.Delay(2000);

            // 2. 演示单独的表情播放
            _logger.LogInformation("2. 演示单独的表情播放");
            await PlayEmotionOnlyAsync(EmotionTypes.Happy, 1, 30);
            await Task.Delay(1000);

            // 3. 演示单独的动作播放
            _logger.LogInformation("3. 演示单独的动作播放");
            await PlayActionOnlyAsync(EmotionTypes.Angry);
            await Task.Delay(2000);

            // 4. 演示表情和动作同步播放
            _logger.LogInformation("4. 演示表情和动作同步播放 - 快乐");
            await PlayEmotionWithActionAsync(new PlayRequest 
            { 
                EmotionType = EmotionTypes.Happy, 
                IncludeAction = true, 
                IncludeEmotion = true,
                Loops = 1,
                Fps = 30
            });
            await Task.Delay(2000);

            _logger.LogInformation("5. 演示表情和动作同步播放 - 愤怒");
            await PlayEmotionWithActionAsync(new PlayRequest 
            { 
                EmotionType = EmotionTypes.Angry, 
                IncludeAction = true, 
                IncludeEmotion = true,
                Loops = 1,
                Fps = 30
            });
            await Task.Delay(2000);

            // 6. 演示随机播放
            _logger.LogInformation("6. 演示随机情感播放");
            await PlayRandomEmotionAsync(true, true, 1, 30);
            await Task.Delay(2000);

            // 7. 回到初始位置
            _logger.LogInformation("7. 回到初始位置");
            await InitializeRobotAsync();

            _logger.LogInformation("=== 演示程序完成 ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "演示程序执行失败");
        }
    }

    public void Dispose()
    {
        _currentPlaybackCts?.Cancel();
        _currentPlaybackCts?.Dispose();
        _logger.LogInformation("情感动作服务已释放资源");
    }
}