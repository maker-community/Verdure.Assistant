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

    // 提示音要比播放器的 300 ms 前导缓冲长，否则短音会卡在缓冲里不响。
    private const int CueToneMs = 250;
    private const int CueFadeMs = 10;
    private const int CueSampleRate = 16000;

    // 播放器还会再乘一次 OutputGainDb（默认 -6 dB），所以这里按 0.6 幅度给足余量。
    private const double CueAmplitude = 0.6;

    private readonly BumiMediaSession _media;
    private readonly IVoiceChatService _voiceChat;
    private readonly IAudioPlayer _audioPlayer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BumiWakeWordBridge> _logger;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly byte[] _cuePcm;

    private long _lastWakeAt;
    private bool _subscribed;

    public BumiWakeWordBridge(
        BumiMediaSession media,
        IVoiceChatService voiceChat,
        IAudioPlayer audioPlayer,
        IConfiguration configuration,
        ILogger<BumiWakeWordBridge> logger)
    {
        _media = media;
        _voiceChat = voiceChat;
        _audioPlayer = audioPlayer;
        _configuration = configuration;
        _logger = logger;
        _cuePcm = CreateChime();
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

            await PlayCueAsync().ConfigureAwait(false);

            // DisconnectAsync 会把手动模式恢复成默认值，这里重新套用一次，
            // 否则断开之后的唤醒会退化成 Manual，服务端永远等不到 listen stop。
            BumiVoiceChatDefaults.ApplyAutoConversationMode(_voiceChat, _configuration);

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

    /// <summary>
    /// 播放一声提示音，让用户知道唤醒被听到了。等它放完再开始上行，
    /// 否则提示音会被机器人自己的麦克风收进去当成用户说的话。
    /// </summary>
    private async Task PlayCueAsync()
    {
        if (!_configuration.GetValue("WakeCueEnabled", true))
        {
            return;
        }

        try
        {
            await _audioPlayer.PlayAsync(_cuePcm, CueSampleRate, 1).ConfigureAwait(false);
            await Task.Delay(CueToneMs * 2 + 120).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "播放唤醒提示音失败，直接开始对话");
        }
    }

    /// <summary>两段正弦（A5 → D6），前后各做一次淡入淡出，避免咔哒声。</summary>
    private static byte[] CreateChime()
    {
        var frequencies = new[] { 880.0, 1174.66 };
        var toneSamples = CueSampleRate * CueToneMs / 1000;
        var fadeSamples = CueSampleRate * CueFadeMs / 1000;
        var samples = new short[toneSamples * frequencies.Length];

        var offset = 0;
        foreach (var frequency in frequencies)
        {
            for (var i = 0; i < toneSamples; i++)
            {
                var envelope = 1.0;
                if (i < fadeSamples)
                {
                    envelope = (double)i / fadeSamples;
                }
                else if (i >= toneSamples - fadeSamples)
                {
                    envelope = (double)(toneSamples - i) / fadeSamples;
                }

                var value = Math.Sin(2 * Math.PI * frequency * i / CueSampleRate) * envelope * CueAmplitude;
                samples[offset + i] = (short)(value * short.MaxValue);
            }

            offset += toneSamples;
        }

        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
