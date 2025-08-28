using Microsoft.AspNetCore.Mvc;
using Verdure.Assistant.Api.Models;
using Verdure.Assistant.Api.Services.Robot;

namespace Verdure.Assistant.Api.Controllers;

/// <summary>
/// 情感和动作控制API控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EmotionController : ControllerBase
{
    private readonly EmotionActionService _emotionActionService;
    private readonly ILogger<EmotionController> _logger;

    public EmotionController(EmotionActionService emotionActionService, ILogger<EmotionController> logger)
    {
        _emotionActionService = emotionActionService;
        _logger = logger;
    }

    /// <summary>
    /// 测试API连接
    /// </summary>
    /// <returns>测试结果</returns>
    [HttpGet("test")]
    public IActionResult Test()
    {
        _logger.LogInformation("收到测试请求");
        return Ok(new { 
            success = true, 
            message = "API连接正常", 
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            server = Environment.MachineName
        });
    }

    /// <summary>
    /// 播放指定的表情和动作
    /// </summary>
    /// <param name="request">播放请求</param>
    /// <returns>播放结果</returns>
    [HttpPost("play")]
    public async Task<IActionResult> PlayEmotion([FromBody] PlayRequest request)
    {
        try
        {
            _logger.LogInformation($"收到播放请求: EmotionType={request.EmotionType}, IncludeAction={request.IncludeAction}, IncludeEmotion={request.IncludeEmotion}, Loops={request.Loops}, Fps={request.Fps}");
            
            if (string.IsNullOrWhiteSpace(request.EmotionType))
            {
                return BadRequest(new { success = false, message = "表情类型不能为空" });
            }
            
            if (!EmotionTypes.IsValid(request.EmotionType))
            {
                return BadRequest(new { success = false, message = $"无效的表情类型: {request.EmotionType}" });
            }
            
            var result = await _emotionActionService.PlayEmotionWithActionAsync(request);
            
            if (result)
            {
                return Ok(new { success = true, message = "播放完成" });
            }
            else
            {
                return BadRequest(new { success = false, message = "播放失败" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "播放请求处理失败");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 仅播放表情动画
    /// </summary>
    /// <param name="emotionType">表情类型</param>
    /// <param name="loops">循环次数</param>
    /// <param name="fps">帧率</param>
    /// <returns>播放结果</returns>
    [HttpPost("play-emotion/{emotionType}")]
    public async Task<IActionResult> PlayEmotionOnly(string emotionType, [FromQuery] int loops = 1, [FromQuery] int fps = 30)
    {
        try
        {
            _logger.LogInformation($"收到仅播放表情请求: {emotionType}");
            
            if (string.IsNullOrWhiteSpace(emotionType))
            {
                return BadRequest(new { success = false, message = "表情类型不能为空" });
            }
            
            if (!EmotionTypes.IsValid(emotionType))
            {
                return BadRequest(new { success = false, message = $"无效的表情类型: {emotionType}" });
            }
            
            var result = await _emotionActionService.PlayEmotionOnlyAsync(emotionType, loops, fps);
            
            if (result)
            {
                return Ok(new { success = true, message = $"表情 {emotionType} 播放完成" });
            }
            else
            {
                return BadRequest(new { success = false, message = "表情播放失败" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "表情播放请求处理失败");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 仅播放动作
    /// </summary>
    /// <param name="emotionType">情感类型</param>
    /// <returns>播放结果</returns>
    [HttpPost("play-action/{emotionType}")]
    public async Task<IActionResult> PlayActionOnly(string emotionType)
    {
        try
        {
            _logger.LogInformation($"收到仅播放动作请求: {emotionType}");
            
            if (string.IsNullOrWhiteSpace(emotionType))
            {
                return BadRequest(new { success = false, message = "表情类型不能为空" });
            }
            
            if (!EmotionTypes.IsValid(emotionType))
            {
                return BadRequest(new { success = false, message = $"无效的表情类型: {emotionType}" });
            }
            
            var result = await _emotionActionService.PlayActionOnlyAsync(emotionType);
            
            if (result)
            {
                return Ok(new { success = true, message = $"动作 {emotionType} 播放完成" });
            }
            else
            {
                return BadRequest(new { success = false, message = "动作播放失败" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "动作播放请求处理失败");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 播放随机表情和动作
    /// </summary>
    /// <param name="includeAction">是否包含动作</param>
    /// <param name="includeEmotion">是否包含表情</param>
    /// <param name="loops">循环次数</param>
    /// <param name="fps">帧率</param>
    /// <returns>播放结果</returns>
    [HttpPost("play-random")]
    public async Task<IActionResult> PlayRandom(
        [FromQuery] bool includeAction = true, 
        [FromQuery] bool includeEmotion = true,
        [FromQuery] int loops = 1,
        [FromQuery] int fps = 30)
    {
        try
        {
            _logger.LogInformation("收到随机播放请求");
            
            var result = await _emotionActionService.PlayRandomEmotionAsync(includeAction, includeEmotion, loops, fps);
            
            if (result)
            {
                return Ok(new { success = true, message = "随机播放完成" });
            }
            else
            {
                return BadRequest(new { success = false, message = "随机播放失败" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "随机播放请求处理失败");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 停止当前播放
    /// </summary>
    /// <param name="clearScreen">是否清除屏幕</param>
    /// <returns>停止结果</returns>
    [HttpPost("stop")]
    public async Task<IActionResult> Stop([FromQuery] bool clearScreen = false)
    {
        try
        {
            _logger.LogInformation($"收到停止播放请求 (清屏: {clearScreen})");
            
            await _emotionActionService.StopCurrentPlaybackAsync(clearScreen);
            
            return Ok(new { success = true, message = "播放已停止" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止播放请求处理失败");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 清除表情屏幕
    /// </summary>
    /// <returns>清屏结果</returns>
    [HttpPost("clear-screen")]
    public async Task<IActionResult> ClearScreen()
    {
        try
        {
            _logger.LogInformation("收到清屏请求");
            
            await _emotionActionService.ClearEmotionScreenAsync();
            
            return Ok(new { success = true, message = "屏幕已清除" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清屏请求处理失败");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 获取当前播放状态
    /// </summary>
    /// <returns>当前状态</returns>
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
            _logger.LogError(ex, "获取状态失败");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 获取可用的表情配置
    /// </summary>
    /// <returns>表情配置列表</returns>
    [HttpGet("emotions")]
    public IActionResult GetAvailableEmotions()
    {
        try
        {
            var emotions = _emotionActionService.GetAvailableEmotionConfigs();
            return Ok(emotions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取可用表情失败");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 初始化机器人位置
    /// </summary>
    /// <returns>初始化结果</returns>
    [HttpPost("initialize")]
    public async Task<IActionResult> Initialize()
    {
        try
        {
            _logger.LogInformation("收到初始化请求");
            
            await _emotionActionService.InitializeRobotAsync();
            
            return Ok(new { success = true, message = "机器人已初始化" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化请求处理失败");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 运行演示程序
    /// </summary>
    /// <returns>演示结果</returns>
    [HttpPost("demo")]
    public async Task<IActionResult> RunDemo()
    {
        try
        {
            _logger.LogInformation("收到演示程序请求");
            
            // 在后台运行演示，不阻塞API响应
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
            
            return Ok(new { success = true, message = "演示程序已启动" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "演示程序请求处理失败");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }
}