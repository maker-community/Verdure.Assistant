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
/// 退出时恢复成"可用"状态（而不是回到启动时的值）：静音只在服务运行期间有效，
/// 否则一次崩溃就会让机器人自己的助手永久哑掉。
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

            if (_options.MuteBuiltInAgent)
            {
                if (_originalAgentToSpeaker)
                {
                    media.SetInternalAgentAudioDataToPlaybackEnable(false);
                }
                else
                {
                    // 上一次运行没能恢复开关（例如被 SIGKILL）：记下来，退出时重新打开。
                    _logger.LogWarning(
                        "内置智能体播放在服务启动前就已被关闭，说明上一次运行没走恢复路径；退出时会重新打开");
                }

                _switchesChanged = true;
            }

            if (_options.MuteAudioCue)
            {
                if (_originalAudioCue)
                {
                    media.SetAudioCueEnable(false);
                }
                else
                {
                    _logger.LogWarning("提示音在服务启动前就已被关闭；退出时会重新打开");
                }

                _switchesChanged = true;
            }

            if (_options.Volume is > 0 and <= 200 && _options.Volume != _originalVolume)
            {
                media.SetVolume(_options.Volume);
                _switchesChanged = true;
            }

            // 外部音频到扬声器必须开着，否则自己的声音出不来。
            media.SetExternalCustomAudioDataToPlaybackEnable(true);

            // 读回值会滞后一拍：刚 set 完再 get 往往还是旧值，所以这里把"请求值"和"读回值"分开写，
            // 免得下次看日志的人误以为静音没生效。
            _logger.LogInformation(
                "Bumi media ready: volume {Volume} (requested {WantedVolume}), agent->speaker requested {WantedAgent} (readback {Agent}), cue requested {WantedCue} (readback {Cue}), mic->agent {Mic} (left untouched)",
                media.GetVolume(),
                _options.Volume,
                _options.MuteBuiltInAgent ? "false" : "unchanged",
                media.GetInternalAgentAudioDataToPlaybackEnable(),
                _options.MuteAudioCue ? "false" : "unchanged",
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

                // 我们的静音只在服务运行期间有效：绝不能让机器人自己的助手在服务退出后还是哑的，
                // 哪怕启动时它就已经是关的（上一次崩溃留下的状态）。
                media.SetInternalAgentAudioDataToPlaybackEnable(true);
                media.SetAudioCueEnable(true);
                media.SetVolume(_originalVolume);
                _logger.LogInformation(
                    "Bumi media switches restored (agent->speaker true, cue true, volume {Volume}; 原来的 agent->speaker {WasAgent}, cue {WasCue})",
                    _originalVolume, _originalAgentToSpeaker, _originalAudioCue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore the Bumi media switches; the robot may stay muted");
            }
        }
    }
}