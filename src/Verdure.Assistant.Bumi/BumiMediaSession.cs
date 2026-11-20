using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Noetix.Sdk.Bumi;

namespace Verdure.Assistant.Bumi;

/// <summary>
/// 持有 MediaController 单例：负责设置 DDS 配置、初始化、套用路线 A 的开关，并在释放时恢复原值。
/// </summary>
/// <remarks>
/// 路线 A（实测结论）：只关内置智能体的输出与提示音，<b>不动</b> 麦克风到智能体的输入。
/// 关掉那个输入会把唤醒检测和扬声器一起停掉，且无法用 TO_WAKEUP 恢复。
/// </remarks>
public sealed class BumiMediaSession : IDisposable
{
    private readonly ILogger<BumiMediaSession> _logger;
    private readonly BumiAudioOptions _options;
    private readonly object _gate = new();

    private bool _initialized;
    private bool _disposed;

    private bool _originalAgentToSpeaker = true;
    private bool _originalAudioCue = true;
    private int _originalVolume;
    private bool _switchesChanged;

    public BumiMediaSession(IOptions<BumiAudioOptions> options, ILogger<BumiMediaSession> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>初始化后的 MediaController。首次访问时完成 DDS 配置与 Init。</summary>
    public MediaController Media
    {
        get
        {
            EnsureInitialized();
            return MediaController.Instance;
        }
    }

    private void EnsureInitialized()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialized)
            {
                return;
            }

            // CYCLONEDDS_URI 必须在 Init 之前设置；Linux 上环境变量传不到原生代码，只能用 SetDdsConfig。
            NoetixSdk.SetDdsConfig(_options.DdsConfigPath);

            var media = MediaController.Instance;
            if (!media.Init())
            {
                throw new InvalidOperationException(
                    $"MediaController.Init() returned false. Check the DDS config '{_options.DdsConfigPath}' and that the robot is reachable.");
            }

            _originalVolume = media.GetVolume();
            _originalAgentToSpeaker = media.GetInternalAgentAudioDataToPlaybackEnable();
            _originalAudioCue = media.GetAudioCueEnable();

            if (_options.MuteBuiltInAgent && _originalAgentToSpeaker)
            {
                media.SetInternalAgentAudioDataToPlaybackEnable(false);
                _switchesChanged = true;
            }

            if (_options.MuteAudioCue && _originalAudioCue)
            {
                media.SetAudioCueEnable(false);
                _switchesChanged = true;
            }

            if (_options.Volume is > 0 and <= 200 && _options.Volume != _originalVolume)
            {
                media.SetVolume(_options.Volume);
                _switchesChanged = true;
            }

            // 外部音频到扬声器必须开着，否则自己的声音出不来。
            media.SetExternalCustomAudioDataToPlaybackEnable(true);

            _logger.LogInformation(
                "Bumi media ready: volume {Volume}, agent->speaker {Agent}, cue {Cue}, mic->agent {Mic} (left untouched)",
                media.GetVolume(),
                media.GetInternalAgentAudioDataToPlaybackEnable(),
                media.GetAudioCueEnable(),
                media.GetInternalCaptureAudioDataToAgentEnable());

            if (media.GetInternalCaptureAudioDataToAgentEnable())
            {
                _logger.LogWarning(
                    "Microphone audio still reaches the built-in agent. Disabling it would also stop the speaker, so it stays on; disclose this if the assistant is delivered to others.");
            }

            _initialized = true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (!_initialized || !_switchesChanged || !_options.RestoreOnExit)
            {
                return;
            }

            try
            {
                var media = MediaController.Instance;
                media.SetInternalAgentAudioDataToPlaybackEnable(_originalAgentToSpeaker);
                media.SetAudioCueEnable(_originalAudioCue);
                media.SetVolume(_originalVolume);
                _logger.LogInformation(
                    "Bumi media switches restored (agent->speaker {Agent}, cue {Cue}, volume {Volume})",
                    _originalAgentToSpeaker, _originalAudioCue, _originalVolume);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore the Bumi media switches; the robot may stay muted");
            }
        }
    }
}