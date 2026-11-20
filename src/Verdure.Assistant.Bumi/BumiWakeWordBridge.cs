using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Noetix.Sdk.Bumi;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Bumi;

/// <summary>
/// 复用机器人自带的唤醒词：厂商智能体被语音唤醒时会发出一条
/// <see cref="StatusChangeReason.AudioWakeuped"/> 状态，这时我们同步开始自己的一轮对话。
/// </summary>
/// <remarks>
/// 这样就不需要额外的关键词模型文件。关键是路线 A 只静音了 agent→speaker，
/// 麦克风到智能体的输入仍然开着，所以唤醒检测照常工作，只是它自己不出声。
/// 回调运行在原生 SDK 线程上，必须立刻返回，真正的启动放到线程池里做。
/// </remarks>
internal sealed class BumiWakeWordBridge : IHostedService
{
    private const int WakeDebounceMs = 2000;

    private readonly BumiMediaSession _media;
    private readonly IVoiceChatService _voiceChat;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BumiWakeWordBridge> _logger;
    private readonly SemaphoreSlim _startGate = new(1, 1);

    private long _lastWakeAt;
    private bool _subscribed;

    public BumiWakeWordBridge(
        BumiMediaSession media,
        IVoiceChatService voiceChat,
        IConfiguration configuration,
        ILogger<BumiWakeWordBridge> logger)
    {
        _media = media;
        _voiceChat = voiceChat;
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue("WakeWordStart", true))
        {
            _logger.LogInformation("WakeWordStart 已关闭，唤醒词不会启动对话");
            return Task.CompletedTask;
        }

        try
        {
            _media.Media.SubscribeSystemStatus(OnSystemStatus);
            _subscribed = true;
            _logger.LogInformation("已订阅机器人状态，等待唤醒词（AudioWakeuped）触发对话");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "订阅机器人状态失败，唤醒词启动不可用");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_subscribed)
        {
            return Task.CompletedTask;
        }

        try
        {
            _media.Media.SubscribeSystemStatus(null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "取消订阅机器人状态失败");
        }

        _subscribed = false;
        return Task.CompletedTask;
    }

    private void OnSystemStatus(SystemStatus status)
    {
        if (status.Value != WorkStatus.Wakeuped || status.Reason != StatusChangeReason.AudioWakeuped)
        {
            return;
        }

        _ = Task.Run(StartConversationAsync);
    }

    private async Task StartConversationAsync()
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastWakeAt) < WakeDebounceMs)
        {
            return;
        }

        Interlocked.Exchange(ref _lastWakeAt, now);

        if (_voiceChat.IsVoiceChatActive)
        {
            _logger.LogDebug("唤醒词触发时对话已在进行中，忽略");
            return;
        }

        await _startGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_voiceChat.IsVoiceChatActive)
            {
                return;
            }

            await _voiceChat.StartVoiceChatAsync().ConfigureAwait(false);
            _logger.LogInformation("唤醒词触发，已开始一轮对话");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "唤醒词触发后启动对话失败");
        }
        finally
        {
            _startGate.Release();
        }
    }
}
