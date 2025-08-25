using Microsoft.AspNetCore.Mvc;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Api.Controllers
{
    /// <summary>
    /// 语音聊天控制器
    /// 提供语音聊天相关的API接口
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class VoiceChatController : ControllerBase
    {
        private readonly ILogger<VoiceChatController> _logger;
        private readonly IVoiceChatService _voiceChatService;

        public VoiceChatController(ILogger<VoiceChatController> logger, IVoiceChatService voiceChatService)
        {
            _logger = logger;
            _voiceChatService = voiceChatService;
        }

        /// <summary>
        /// 获取连接状态
        /// </summary>
        /// <returns>连接状态信息</returns>
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            try
            {
                var status = new
                {
                    IsConnected = _voiceChatService.IsConnected,
                    IsVoiceChatActive = _voiceChatService.IsVoiceChatActive,
                    CurrentState = _voiceChatService.CurrentState.ToString(),
                    CurrentListeningMode = _voiceChatService.CurrentListeningMode.ToString(),
                    KeepListening = _voiceChatService.KeepListening
                };

                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取语音聊天状态失败");
                return StatusCode(500, $"获取状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 初始化语音聊天服务
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpPost("initialize")]
        public async Task<IActionResult> Initialize([FromBody] InitializeRequest? request = null)
        {
            try
            {
                _logger.LogInformation("API初始化语音聊天服务");
                
                // 这里可以使用请求中的配置，或者使用默认配置
                var config = CreateDefaultConfig();
                
                await _voiceChatService.InitializeAsync(config);
                return Ok(new { Success = true, Message = "语音聊天服务初始化成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化语音聊天服务失败");
                return StatusCode(500, $"初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 开始语音对话
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpPost("start")]
        public async Task<IActionResult> StartVoiceChat()
        {
            try
            {
                if (!_voiceChatService.IsConnected)
                {
                    return BadRequest("服务未连接，请先初始化");
                }

                if (_voiceChatService.IsVoiceChatActive)
                {
                    return BadRequest("语音对话已经在进行中");
                }

                _logger.LogInformation("API开始语音对话");
                await _voiceChatService.StartVoiceChatAsync();
                return Ok(new { Success = true, Message = "语音对话已开始" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "开始语音对话失败");
                return StatusCode(500, $"开始语音对话失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止语音对话
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpPost("stop")]
        public async Task<IActionResult> StopVoiceChat()
        {
            try
            {
                if (!_voiceChatService.IsVoiceChatActive)
                {
                    return BadRequest("语音对话未在进行中");
                }

                _logger.LogInformation("API停止语音对话");
                await _voiceChatService.StopVoiceChatAsync();
                return Ok(new { Success = true, Message = "语音对话已停止" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止语音对话失败");
                return StatusCode(500, $"停止语音对话失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 切换对话状态
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpPost("toggle")]
        public async Task<IActionResult> ToggleChatState()
        {
            try
            {
                if (!_voiceChatService.IsConnected)
                {
                    return BadRequest("服务未连接，请先初始化");
                }

                _logger.LogInformation("API切换对话状态");
                await _voiceChatService.ToggleChatStateAsync();
                
                return Ok(new 
                { 
                    Success = true, 
                    Message = "对话状态已切换", 
                    CurrentState = _voiceChatService.CurrentState.ToString() 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切换对话状态失败");
                return StatusCode(500, $"切换对话状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送文本消息
        /// </summary>
        /// <param name="request">文本消息请求</param>
        /// <returns>操作结果</returns>
        [HttpPost("send-text")]
        public async Task<IActionResult> SendTextMessage([FromBody] SendTextRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    return BadRequest("消息内容不能为空");
                }

                if (!_voiceChatService.IsConnected)
                {
                    return BadRequest("服务未连接，请先初始化");
                }

                _logger.LogInformation("API发送文本消息: {Message}", request.Message);
                await _voiceChatService.SendTextMessageAsync(request.Message);
                return Ok(new { Success = true, Message = "消息已发送" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送文本消息失败");
                return StatusCode(500, $"发送消息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置自动对话模式
        /// </summary>
        /// <param name="request">自动模式设置请求</param>
        /// <returns>操作结果</returns>
        [HttpPost("auto-mode")]
        public IActionResult SetAutoMode([FromBody] SetAutoModeRequest request)
        {
            try
            {
                _logger.LogInformation("API设置自动对话模式: {Enabled}", request.Enabled);
                _voiceChatService.KeepListening = request.Enabled;
                
                var message = request.Enabled ? "自动对话模式已启用" : "自动对话模式已禁用";
                return Ok(new { Success = true, Message = message, KeepListening = _voiceChatService.KeepListening });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置自动对话模式失败");
                return StatusCode(500, $"设置自动模式失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpPost("disconnect")]
        public IActionResult Disconnect()
        {
            try
            {
                _logger.LogInformation("API断开语音聊天连接");
                _voiceChatService.Dispose();
                return Ok(new { Success = true, Message = "连接已断开" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "断开连接失败");
                return StatusCode(500, $"断开连接失败: {ex.Message}");
            }
        }

        #region 私有方法

        private VerdureConfig CreateDefaultConfig()
        {
            return new VerdureConfig
            {
                ServerUrl = "wss://api.tenclass.net/xiaozhi/v1/",
                MqttBroker = "localhost",
                MqttPort = 1883,
                MqttClientId = "xiaozhi_api_client",
                MqttTopic = "xiaozhi/chat",
                UseWebSocket = true,
                EnableVoice = true,
                AudioSampleRate = 16000,
                AudioChannels = 1,
                AudioFormat = "opus",
                EnableTemperatureSensor = false,
                KeywordModels = new KeywordModelConfig
                {
                    ModelsPath = "ModelFiles",
                    CurrentModel = "keyword_xiaodian.table"
                }
            };
        }

        #endregion
    }

    // 请求模型
    public class InitializeRequest
    {
        public string? ServerUrl { get; set; }
        public bool UseWebSocket { get; set; } = true;
        public bool EnableVoice { get; set; } = true;
    }

    public class SendTextRequest
    {
        public string Message { get; set; } = string.Empty;
    }

    public class SetAutoModeRequest
    {
        public bool Enabled { get; set; }
    }
}
