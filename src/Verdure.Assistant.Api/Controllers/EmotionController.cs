using Microsoft.AspNetCore.Mvc;
using Verdure.Assistant.Api.IoT.Interfaces;
using Verdure.Assistant.Api.IoT.Models;

namespace Verdure.Assistant.Api.Controllers;

/// <summary>
/// 机器人表情和动作控制API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EmotionController : ControllerBase
{
    private readonly IEmotionActionService _emotionActionService;
    private readonly IDisplayService _displayService;
    private readonly IRobotActionService _robotActionService;
    private readonly ILogger<EmotionController> _logger;

    public EmotionController(
        IEmotionActionService emotionActionService,
        IDisplayService displayService,
        IRobotActionService robotActionService,
        ILogger<EmotionController> logger)
    {
        _emotionActionService = emotionActionService ?? throw new ArgumentNullException(nameof(emotionActionService));
        _displayService = displayService ?? throw new ArgumentNullException(nameof(displayService));
        _robotActionService = robotActionService ?? throw new ArgumentNullException(nameof(robotActionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 播放指定表情和动作
    /// </summary>
    [HttpPost("play")]
    public async Task<IActionResult> PlayEmotion([FromBody] PlayRequest request)
    {
        try
        {
            if (request == null)
            {
                return BadRequest("请求参数不能为空");
            }

            _logger.LogInformation($"收到播放表情请求: {request.EmotionType}");
            
            var success = await _emotionActionService.PlayEmotionWithActionAsync(request);
            
            if (success)
            {
                return Ok(new { message = "表情播放完成", emotion = request.EmotionType });
            }
            else
            {
                return BadRequest(new { message = "表情播放失败", emotion = request.EmotionType });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "播放表情时发生错误");
            return StatusCode(500, new { message = "服务器内部错误", error = ex.Message });
        }
    }

    /// <summary>
    /// 仅播放表情动画
    /// </summary>
    [HttpPost("play-emotion/{emotionType}")]
    public async Task<IActionResult> PlayEmotionOnly(string emotionType, [FromQuery] int loops = 1, [FromQuery] int fps = 30)
    {
        try
        {
            _logger.LogInformation($"收到播放表情动画请求: {emotionType}");
            
            var success = await _emotionActionService.PlayEmotionOnlyAsync(emotionType, loops, fps);
            
            if (success)
            {
                return Ok(new { message = "表情动画播放完成", emotion = emotionType });
            }
            else
            {
                return BadRequest(new { message = "表情动画播放失败", emotion = emotionType });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "播放表情动画时发生错误");
            return StatusCode(500, new { message = "服务器内部错误", error = ex.Message });
        }
    }

    /// <summary>
    /// 仅播放动作
    /// </summary>
    [HttpPost("play-action/{emotionType}")]
    public async Task<IActionResult> PlayActionOnly(string emotionType)
    {
        try
        {
            _logger.LogInformation($"收到播放动作请求: {emotionType}");
            
            var success = await _emotionActionService.PlayActionOnlyAsync(emotionType);
            
            if (success)
            {
                return Ok(new { message = "动作播放完成", emotion = emotionType });
            }
            else
            {
                return BadRequest(new { message = "动作播放失败", emotion = emotionType });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "播放动作时发生错误");
            return StatusCode(500, new { message = "服务器内部错误", error = ex.Message });
        }
    }

    /// <summary>
    /// 播放随机表情和动作
    /// </summary>
    [HttpPost("play-random")]
    public async Task<IActionResult> PlayRandomEmotion([FromQuery] bool includeAction = true, [FromQuery] bool includeEmotion = true, [FromQuery] int loops = 1, [FromQuery] int fps = 30)
    {
        try
        {
            _logger.LogInformation("收到播放随机表情请求");
            
            var success = await _emotionActionService.PlayRandomEmotionAsync(includeAction, includeEmotion, loops, fps);
            
            if (success)
            {
                return Ok(new { message = "随机表情播放完成" });
            }
            else
            {
                return BadRequest(new { message = "随机表情播放失败" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "播放随机表情时发生错误");
            return StatusCode(500, new { message = "服务器内部错误", error = ex.Message });
        }
    }

    /// <summary>
    /// 停止当前播放
    /// </summary>
    [HttpPost("stop")]
    public async Task<IActionResult> StopPlayback([FromQuery] bool clearScreen = false)
    {
        try
        {
            _logger.LogInformation("收到停止播放请求");
            
            await _emotionActionService.StopCurrentPlaybackAsync(clearScreen);
            
            return Ok(new { message = "播放已停止" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止播放时发生错误");
            return StatusCode(500, new { message = "服务器内部错误", error = ex.Message });
        }
    }

    /// <summary>
    /// 清除表情屏幕
    /// </summary>
    [HttpPost("clear")]
    public async Task<IActionResult> ClearScreen()
    {
        try
        {
            _logger.LogInformation("收到清屏请求");
            
            await _emotionActionService.ClearEmotionScreenAsync();
            
            return Ok(new { message = "屏幕已清除" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清屏时发生错误");
            return StatusCode(500, new { message = "服务器内部错误", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取播放状态
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        try
        {
            var state = _emotionActionService.GetCurrentState();
            return Ok(state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取状态时发生错误");
            return StatusCode(500, new { message = "服务器内部错误", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取可用的表情类型
    /// </summary>
    [HttpGet("emotions")]
    public IActionResult GetAvailableEmotions()
    {
        try
        {
            var emotions = _displayService.GetAvailableEmotions();
            var emotionConfigs = _emotionActionService.GetAvailableEmotionConfigs();
            var supportedEmotions = EmotionMappingService.GetSupportedEmotions();
            
            return Ok(new 
            { 
                availableEmotions = emotions,
                emotionConfigs = emotionConfigs,
                supportedInputEmotions = supportedEmotions,
                emotionMappings = new
                {
                    neutral = new[] { "neutral", "relaxed", "sleepy" },
                    happy = new[] { "happy", "laughing", "funny", "loving", "confident", "winking", "cool", "delicious", "kissy", "silly" },
                    sad = new[] { "sad", "crying" },
                    angry = new[] { "angry" },
                    surprised = new[] { "surprised", "shocked" },
                    confused = new[] { "thinking", "confused", "embarrassed" }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取表情列表时发生错误");
            return StatusCode(500, new { message = "服务器内部错误", error = ex.Message });
        }
    }

    /// <summary>
    /// 初始化机器人位置
    /// </summary>
    [HttpPost("initialize")]
    public async Task<IActionResult> InitializeRobot()
    {
        try
        {
            _logger.LogInformation("收到初始化机器人请求");
            
            await _emotionActionService.InitializeRobotAsync();
            
            return Ok(new { message = "机器人初始化完成" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化机器人时发生错误");
            return StatusCode(500, new { message = "服务器内部错误", error = ex.Message });
        }
    }

    /// <summary>
    /// 运行演示程序
    /// </summary>
    [HttpPost("demo")]
    public async Task<IActionResult> RunDemo()
    {
        try
        {
            _logger.LogInformation("收到演示程序请求");
            
            // 在后台运行演示，避免阻塞API响应
            _ = Task.Run(async () =>
            {
                try
                {
                    await _emotionActionService.RunDemoAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "演示程序执行失败");
                }
            });
            
            return Ok(new { message = "演示程序已开始" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动演示程序时发生错误");
            return StatusCode(500, new { message = "服务器内部错误", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取机器人关节状态
    /// </summary>
    [HttpGet("joints")]
    public IActionResult GetJointStatuses()
    {
        try
        {
            var joints = _robotActionService.GetAllJointStatuses();
            return Ok(joints);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取关节状态时发生错误");
            return StatusCode(500, new { message = "服务器内部错误", error = ex.Message });
        }
    }

    /// <summary>
    /// 移动指定关节
    /// </summary>
    [HttpPost("joints/{channel}/move")]
    public async Task<IActionResult> MoveJoint(int channel, [FromQuery] float angle)
    {
        try
        {
            _logger.LogInformation($"收到移动关节请求: 通道{channel}, 角度{angle}");
            
            await _robotActionService.MoveJointAsync(channel, angle);
            
            return Ok(new { message = $"关节 {channel} 移动完成", angle = angle });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "移动关节时发生错误");
            return StatusCode(500, new { message = "服务器内部错误", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取显示器状态
    /// </summary>
    [HttpGet("displays")]
    public IActionResult GetDisplayStatuses()
    {
        try
        {
            var display24Status = _displayService.GetDisplayStatus(DisplayType.Display24Inch);
            var display147Status = _displayService.GetDisplayStatus(DisplayType.Display147Inch);
            
            return Ok(new 
            { 
                display24Inch = display24Status,
                display147Inch = display147Status
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取显示器状态时发生错误");
            return StatusCode(500, new { message = "服务器内部错误", error = ex.Message });
        }
    }
}