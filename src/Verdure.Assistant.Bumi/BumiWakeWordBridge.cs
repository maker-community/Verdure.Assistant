using System.Text;
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

    private const int CueSampleRate = 16000;
    private const string EmbeddedCueName = "Verdure.Assistant.Bumi.Assets.wake-cue.wav";

    private readonly BumiMediaSession _media;
    private readonly IVoiceChatService _voiceChat;
    private readonly IAudioPlayer _audioPlayer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BumiWakeWordBridge> _logger;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly byte[] _cuePcm;

    private long _lastWakeAt;
    private bool _subscribed;
    private string _lastStatus = string.Empty;

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
        _cuePcm = LoadCue(configuration);
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
        var signature = $"{status.Value}/{status.Reason}";
        if (signature != _lastStatus)
        {
            _lastStatus = signature;
            _logger.LogInformation("语音模块状态: {Value}（{Reason}）", status.Value, status.Reason);
        }

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
        if (!_configuration.GetValue("WakeCueEnabled", true) || _cuePcm.Length == 0)
        {
            return;
        }

        var cueMs = _cuePcm.Length * 1000 / (2 * CueSampleRate);
        try
        {
            await _audioPlayer.PlayAsync(_cuePcm, CueSampleRate, 1).ConfigureAwait(false);
            await Task.Delay(cueMs + 120).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "播放唤醒提示音失败，直接开始对话");
        }
    }

    /// <summary>
    /// 优先用 <c>WakeCueFile</c> 指定的 WAV，否则用内置的那段（由 bing.mp3 转成 16 kHz 单声道）。
    /// 读不到就返回空数组，只是不出声，不该影响唤醒本身。
    /// </summary>
    private byte[] LoadCue(IConfiguration configuration)
    {
        var overridePath = configuration["WakeCueFile"];
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            try
            {
                if (File.Exists(overridePath))
                {
                    _logger.LogInformation("使用自定义唤醒提示音: {Path}", overridePath);
                    return ReadWav(File.ReadAllBytes(overridePath));
                }

                _logger.LogWarning("WakeCueFile 指向的文件不存在: {Path}，改用内置提示音", overridePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取 WakeCueFile 失败，改用内置提示音");
            }
        }

        try
        {
            using var stream = typeof(BumiWakeWordBridge).Assembly.GetManifestResourceStream(EmbeddedCueName);
            if (stream is null)
            {
                _logger.LogError("内置提示音资源 {Name} 缺失，唤醒将没有提示音", EmbeddedCueName);
                return Array.Empty<byte>();
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return ReadWav(buffer.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载内置提示音失败，唤醒将没有提示音");
            return Array.Empty<byte>();
        }
    }

    /// <summary>取出 WAV 的 PCM 数据；机器人扬声器只认 16 kHz 单声道 16-bit。</summary>
    private static byte[] ReadWav(byte[] wav)
    {
        if (wav.Length < 44 ||
            Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" ||
            Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
        {
            throw new InvalidDataException("提示音不是合法的 WAV 文件");
        }

        var format = 0;
        var channels = 0;
        var sampleRate = 0;
        var bits = 0;
        var offset = 12;

        while (offset + 8 <= wav.Length)
        {
            var id = Encoding.ASCII.GetString(wav, offset, 4);
            var size = BitConverter.ToInt32(wav, offset + 4);
            if (size < 0)
            {
                break;
            }

            var body = offset + 8;
            if (id == "fmt " && body + 16 <= wav.Length)
            {
                format = BitConverter.ToInt16(wav, body);
                channels = BitConverter.ToInt16(wav, body + 2);
                sampleRate = BitConverter.ToInt32(wav, body + 4);
                bits = BitConverter.ToInt16(wav, body + 14);
            }
            else if (id == "data")
            {
                if (format != 1 || channels != 1 || sampleRate != CueSampleRate || bits != 16)
                {
                    throw new InvalidDataException(
                        $"提示音必须是 {CueSampleRate} Hz 单声道 16-bit PCM（当前 {sampleRate} Hz / {channels} 声道 / {bits} bit）");
                }

                var length = Math.Min(size, wav.Length - body);
                var pcm = new byte[length];
                Array.Copy(wav, body, pcm, 0, length);
                return pcm;
            }

            offset = body + size + (size % 2);
        }

        throw new InvalidDataException("WAV 里找不到 data 块");
    }
}
