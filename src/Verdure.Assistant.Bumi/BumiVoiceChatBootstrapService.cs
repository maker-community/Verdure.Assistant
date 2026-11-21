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

        BumiVoiceChatDefaults.ApplyAutoConversationMode(_voiceChat, _configuration);
        var config = BumiVoiceChatDefaults.CreateConfig(_configuration);

        // 开机自启时网络和 DDS 可能还没就绪，失败就退避重试，而不是让服务停在一个半死状态。
        for (var attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
        {
            if (_voiceChat.IsConnected)
            {
                return;
            }

            try
            {
                await _voiceChat.InitializeAsync(config);
                _logger.LogInformation("语音聊天服务已自动初始化（第 {Attempt} 次尝试），可用 POST /api/voicechat/start 开始对话", attempt);
                return;
            }
            catch (Exception ex)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(60, 5 * attempt));
                _logger.LogWarning(
                    ex, "第 {Attempt} 次自动初始化失败，{Delay} 秒后重试（服务本身仍在运行，可查状态）", attempt, delay.TotalSeconds);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
