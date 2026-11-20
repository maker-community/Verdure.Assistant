using Microsoft.AspNetCore.Mvc;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Bumi.Controllers;

/// <summary>
/// 机器人的语音对话接口，供外部程序（桌面端、Web 页面、剧本脚本）触发。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class VoiceChatController : ControllerBase
{
    private readonly IVoiceChatService _voiceChat;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VoiceChatController> _logger;

    public VoiceChatController(
        IVoiceChatService voiceChat,
        IConfiguration configuration,
        ILogger<VoiceChatController> logger)
    {
        _voiceChat = voiceChat;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            IsConnected = _voiceChat.IsConnected,
            IsVoiceChatActive = _voiceChat.IsVoiceChatActive,
            CurrentState = _voiceChat.CurrentState.ToString(),
            CurrentListeningMode = _voiceChat.CurrentListeningMode.ToString(),
            KeepListening = _voiceChat.KeepListening
        });
    }

    [HttpPost("initialize")]
    public async Task<IActionResult> Initialize()
    {
        try
        {
            if (_voiceChat.IsConnected)
            {
                return BadRequest("已经连接，无需重复初始化");
            }

            BumiVoiceChatDefaults.ApplyAutoConversationMode(_voiceChat, _configuration);
            await _voiceChat.InitializeAsync(BumiVoiceChatDefaults.CreateConfig(_configuration));
            return Ok(new { Success = true, Message = "语音聊天服务初始化成功" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化语音聊天服务失败");
            return StatusCode(500, $"初始化失败: {ex.Message}");
        }
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start()
    {
        try
        {
            if (_voiceChat.IsVoiceChatActive)
            {
                return BadRequest("语音对话已经在进行中");
            }

            await _voiceChat.StartVoiceChatAsync();
            return Ok(new { Success = true, Message = "语音对话已开始" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "开始语音对话失败");
            return StatusCode(500, $"开始语音对话失败: {ex.Message}");
        }
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop()
    {
        try
        {
            if (!_voiceChat.IsVoiceChatActive)
            {
                return BadRequest("语音对话未在进行中");
            }

            await _voiceChat.StopVoiceChatAsync();
            return Ok(new { Success = true, Message = "语音对话已停止" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止语音对话失败");
            return StatusCode(500, $"停止语音对话失败: {ex.Message}");
        }
    }

    [HttpPost("toggle")]
    public async Task<IActionResult> Toggle()
    {
        try
        {
            if (!_voiceChat.IsConnected)
            {
                return BadRequest("服务未连接，请先初始化");
            }

            await _voiceChat.ToggleChatStateAsync();
            return Ok(new
            {
                Success = true,
                Message = "对话状态已切换",
                CurrentState = _voiceChat.CurrentState.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换对话状态失败");
            return StatusCode(500, $"切换对话状态失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 让助手直接把一段文本说出来，不需要动用麦克风。
    /// </summary>
    [HttpPost("send-text")]
    public async Task<IActionResult> SendText([FromBody] SendTextRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest("消息内容不能为空");
            }

            if (!_voiceChat.IsConnected)
            {
                return BadRequest("服务未连接，请先初始化");
            }

            await _voiceChat.SendTextMessageAsync(request.Message);
            return Ok(new { Success = true, Message = "消息已发送" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送文本消息失败");
            return StatusCode(500, $"发送消息失败: {ex.Message}");
        }
    }

    [HttpPost("interrupt")]
    public async Task<IActionResult> Interrupt([FromBody] InterruptRequest? request = null)
    {
        try
        {
            var reason = request?.Reason ?? "external interrupt";
            _voiceChat.TriggerApiInterrupt("/api/voicechat/interrupt", new { Reason = reason });
            await _voiceChat.InterruptAsync(AbortReason.UserInterruption);
            return Ok(new { Success = true, Message = "对话已中断", Reason = reason });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "中断对话失败");
            return StatusCode(500, $"中断对话失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 自动对话模式：开启后由服务端判断一句话说完，机器人会一直听下去。
    /// </summary>
    [HttpPost("auto-mode")]
    public IActionResult SetAutoMode([FromBody] SetAutoModeRequest request)
    {
        try
        {
            _voiceChat.KeepListening = request.Enabled;
            var message = request.Enabled ? "自动对话模式已启用" : "自动对话模式已禁用（下次初始化生效）";
            return Ok(new { Success = true, Message = message, KeepListening = _voiceChat.KeepListening });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设置自动对话模式失败");
            return StatusCode(500, $"设置自动模式失败: {ex.Message}");
        }
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect()
    {
        try
        {
            // 单例服务不能 Dispose，之后还要能重新初始化。
            await _voiceChat.DisconnectAsync();
            return Ok(new { Success = true, Message = "连接已断开" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "断开连接失败");
            return StatusCode(500, $"断开连接失败: {ex.Message}");
        }
    }
}

public class SendTextRequest
{
    public string Message { get; set; } = string.Empty;
}

public class InterruptRequest
{
    public string? Reason { get; set; }
}

public class SetAutoModeRequest
{
    public bool Enabled { get; set; }
}
