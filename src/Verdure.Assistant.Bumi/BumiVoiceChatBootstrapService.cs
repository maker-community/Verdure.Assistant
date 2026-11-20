using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Bumi;

/// <summary>
/// 进程起来后在后台连上服务端，这样机器人开机自启就能直接说话，
/// 不必等外部先调一次 <c>/api/voicechat/initialize</c>。
/// </summary>
internal sealed class BumiVoiceChatBootstrapService : BackgroundService
{
    private readonly IVoiceChatService _voiceChat;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BumiVoiceChatBootstrapService> _logger;

    public BumiVoiceChatBootstrapService(
        IVoiceChatService voiceChat,
        IConfiguration configuration,
        ILogger<BumiVoiceChatBootstrapService> logger)
    {
        _voiceChat = voiceChat;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("AutoStartVoiceChat", true))
        {
            _logger.LogInformation("AutoStartVoiceChat 已关闭，等待外部调用 /api/voicechat/initialize");
            return;
        }

        try
        {
            BumiVoiceChatDefaults.ApplyAutoConversationMode(_voiceChat, _configuration);
            await _voiceChat.InitializeAsync(BumiVoiceChatDefaults.CreateConfig(_configuration));
            _logger.LogInformation("语音聊天服务已自动初始化，可用 POST /api/voicechat/start 开始对话");
        }
        catch (Exception ex)
        {
            // 失败不影响 HTTP 服务本身：机器人没上电、网络没通时仍然能查看状态并重试。
            _logger.LogError(ex, "自动初始化语音聊天失败，可稍后通过 /api/voicechat/initialize 重试");
        }
    }
}
