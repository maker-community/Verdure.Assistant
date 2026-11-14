using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Services.DeepSeek;
using Verdure.Assistant.Core.Services.DeepSeek.Speech;

namespace Verdure.Assistant.ViewModels;

/// <summary>
/// 首页的"语音后端"部分：在小智与 DeepSeek 之间切换，连续对话/按住说话/打断/唤醒词都按所选后端执行。
/// </summary>
public partial class HomePageViewModel
{
    public IReadOnlyList<string> BackendChoices { get; } = new[] { "小智", "DeepSeek" };

    private VoiceBackendCoordinator? _backend;
    private DeepSeekVoiceChatService? _deepSeekVoice;
    private bool _backendAttached;
    private bool _suppressBackendSwitch;

    /// <summary>当前语音后端是 DeepSeek</summary>
    [ObservableProperty]
    private bool _isDeepSeekBackend;

    /// <summary>语音后端下拉框的选中项：0 小智，1 DeepSeek</summary>
    [ObservableProperty]
    private int _backendIndex;

    public bool IsDeepSeekAvailable => _backend != null && _deepSeekVoice != null;

    partial void OnBackendIndexChanged(int value)
    {
        if (_suppressBackendSwitch || _backend == null)
        {
            return;
        }

        _ = SwitchBackendAsync(value == 1 ? VoiceBackend.DeepSeek : VoiceBackend.XiaoZhi);
    }

    partial void OnIsDeepSeekBackendChanged(bool value)
    {
        OnPropertyChanged(nameof(PhaseText));
        UpdatePhase();
    }

    private async Task SwitchBackendAsync(VoiceBackend backend)
    {
        try
        {
            await _backend!.SetBackendAsync(backend);
            AddMessage(backend == VoiceBackend.DeepSeek
                ? "已切换到 DeepSeek 语音对话：唤醒词、连续对话和按住说话都会使用 DeepSeek 识别与回复"
                : "已切换到小智语音对话");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "切换语音后端失败");
            AddMessage($"切换语音后端失败: {ex.Message}", true);
        }
    }

    internal void AttachBackend(VoiceBackendCoordinator? backend, DeepSeekVoiceChatService? deepSeekVoice)
    {
        _backend = backend;
        _deepSeekVoice = deepSeekVoice;
        OnPropertyChanged(nameof(IsDeepSeekAvailable));
    }

    private async Task BindBackendAsync()
    {
        if (_backend == null || _deepSeekVoice == null || _backendAttached)
        {
            return;
        }

        _backendAttached = true;

        _backend.BackendChanged += (_, backend) => _ = _uiDispatcher.InvokeAsync(() =>
        {
            _suppressBackendSwitch = true;
            try
            {
                BackendIndex = backend == VoiceBackend.DeepSeek ? 1 : 0;
                IsDeepSeekBackend = backend == VoiceBackend.DeepSeek;
            }
            finally
            {
                _suppressBackendSwitch = false;
            }
        });

        _deepSeekVoice.PhaseChanged += (_, phase) => _ = _uiDispatcher.InvokeAsync(() => OnDeepSeekPhaseChanged(phase));
        _deepSeekVoice.ActiveChanged += (_, active) => _ = _uiDispatcher.InvokeAsync(() =>
        {
            IsAutoMode = active && _deepSeekVoice.IsContinuous;
            AutoButtonText = IsAutoMode ? "停止对话" : "开始对话";
            if (!active)
            {
                IsPushToTalkActive = false;
                RestoreManualButtonState();
                UpdatePhase();
            }
        });
        _deepSeekVoice.UserTranscribed += (_, text) => _ = _uiDispatcher.InvokeAsync(() => AddMessage($"用户: {text}", false));
        _deepSeekVoice.ChatEventReceived += (_, ev) =>
        {
            if (ev is DeepSeekChatEvent.Completed completed && !string.IsNullOrWhiteSpace(completed.Content))
            {
                _ = _uiDispatcher.InvokeAsync(() =>
                {
                    AddMessage($"DeepSeek: {completed.Content}", false);
                    TtsText = completed.Content;
                });
            }
        };
        _deepSeekVoice.ErrorOccurred += (_, error) => _ = _uiDispatcher.InvokeAsync(() => AddMessage($"❌ {error}", true));
        _deepSeekVoice.InfoOccurred += (_, info) => _ = _uiDispatcher.InvokeAsync(() => AddMessage(info, false));

        await _backend.InitializeAsync();
        await _uiDispatcher.InvokeAsync(() =>
        {
            _suppressBackendSwitch = true;
            try
            {
                BackendIndex = _backend.IsDeepSeek ? 1 : 0;
                IsDeepSeekBackend = _backend.IsDeepSeek;
            }
            finally
            {
                _suppressBackendSwitch = false;
            }
        });
    }

    private void OnDeepSeekPhaseChanged(DeepSeekVoicePhase phase)
    {
        OnPropertyChanged(nameof(PhaseText));
        UpdatePhase();

        switch (phase)
        {
            case DeepSeekVoicePhase.Listening:
                IsListening = true;
                StatusText = "正在聆听";
                KeywordWakeStatusText = "聆听中";
                KeywordWakePromptText = "请说话，说完停顿一下即可…";
                EmotionStatusText = "专注聆听";
                SetEmotion("listening");
                ShowMicrophoneVisualizer = true;
                break;

            case DeepSeekVoicePhase.Transcribing:
                IsListening = false;
                StatusText = "识别中";
                KeywordWakePromptText = "正在识别你说的话…";
                EmotionStatusText = "认真倾听";
                SetEmotion("thinking");
                ShowMicrophoneVisualizer = false;
                break;

            case DeepSeekVoicePhase.Thinking:
                IsListening = false;
                StatusText = "思考中";
                KeywordWakePromptText = "DeepSeek 正在思考…";
                EmotionStatusText = "思考中";
                SetEmotion("thinking");
                ShowMicrophoneVisualizer = false;
                break;

            case DeepSeekVoicePhase.Speaking:
                IsListening = false;
                StatusText = "正在播放";
                KeywordWakeStatusText = "播放中";
                KeywordWakePromptText = "正在朗读回复…";
                EmotionStatusText = "活跃交流";
                SetEmotion("speaking");
                ShowMicrophoneVisualizer = false;
                break;

            default:
                IsListening = false;
                StatusText = "待命";
                KeywordWakeStatusText = "待机中";
                KeywordWakePromptText = "请说 '小点' 来唤醒助手";
                EmotionStatusText = "平静待机";
                SetEmotion("neutral");
                ShowMicrophoneVisualizer = false;
                break;
        }
    }

    /// <summary>DeepSeek 阶段 → 界面阶段（识别/思考都显示为"处理中"）</summary>
    private string DeepSeekUiPhase() => _deepSeekVoice?.Phase switch
    {
        DeepSeekVoicePhase.Listening => "Listening",
        DeepSeekVoicePhase.Transcribing or DeepSeekVoicePhase.Thinking => "Connecting",
        DeepSeekVoicePhase.Speaking => "Speaking",
        _ => "Idle"
    };

    private string DeepSeekPhaseText() => _deepSeekVoice?.Phase switch
    {
        DeepSeekVoicePhase.Transcribing => "识别中",
        DeepSeekVoicePhase.Thinking => "思考中",
        _ => "连接中"
    };

    #region DeepSeek 后端下的命令

    private async Task<bool> TryToggleAutoDeepSeekAsync()
    {
        if (!IsDeepSeekBackend || _deepSeekVoice == null)
        {
            return false;
        }

        try
        {
            if (_deepSeekVoice.IsActive)
            {
                await _deepSeekVoice.StopAsync();
                AddMessage("DeepSeek 连续对话已停止");
            }
            else if (await _deepSeekVoice.StartAsync(continuous: true))
            {
                AddMessage("DeepSeek 连续对话已开始，请直接说话");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "切换 DeepSeek 连续对话失败");
            AddMessage($"切换 DeepSeek 连续对话失败: {ex.Message}", true);
        }

        return true;
    }

    private async Task<bool> TryStartPushToTalkDeepSeekAsync()
    {
        if (!IsDeepSeekBackend || _deepSeekVoice == null)
        {
            return false;
        }

        try
        {
            IsPushToTalkActive = true;
            if (await _deepSeekVoice.BeginPushToTalkAsync())
            {
                SetManualButtonRecordingState();
                AddMessage("🎤 正在录音... 松开按钮结束录音");
            }
            else
            {
                IsPushToTalkActive = false;
            }
        }
        catch (Exception ex)
        {
            IsPushToTalkActive = false;
            RestoreManualButtonState();
            _logger?.LogError(ex, "DeepSeek 按住说话启动失败");
            AddMessage($"开始录音失败: {ex.Message}", true);
        }

        return true;
    }

    private async Task<bool> TryStopPushToTalkDeepSeekAsync()
    {
        if (!IsDeepSeekBackend || _deepSeekVoice == null)
        {
            return false;
        }

        try
        {
            if (IsPushToTalkActive)
            {
                await _deepSeekVoice.EndPushToTalkAsync();
                IsPushToTalkActive = false;
                RestoreManualButtonState();
                AddMessage("录音结束，正在识别…");
            }
        }
        catch (Exception ex)
        {
            IsPushToTalkActive = false;
            RestoreManualButtonState();
            _logger?.LogError(ex, "DeepSeek 按住说话结束失败");
            AddMessage($"停止录音失败: {ex.Message}", true);
        }

        return true;
    }

    private async Task<bool> TryAbortDeepSeekAsync()
    {
        if (!IsDeepSeekBackend || _deepSeekVoice == null)
        {
            return false;
        }

        try
        {
            if (_deepSeekVoice.Phase is DeepSeekVoicePhase.Thinking or DeepSeekVoicePhase.Speaking or DeepSeekVoicePhase.Transcribing)
            {
                await _deepSeekVoice.InterruptAsync();
                AddMessage("已中断当前回复");
            }
            else if (_deepSeekVoice.IsActive)
            {
                await _deepSeekVoice.StopAsync();
                AddMessage("DeepSeek 语音对话已停止");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "中断 DeepSeek 对话失败");
            AddMessage($"中断操作失败: {ex.Message}", true);
        }

        return true;
    }

    #endregion
}
