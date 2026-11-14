using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services.DeepSeek;
using Verdure.Assistant.Core.Services.DeepSeek.Speech;

namespace Verdure.Assistant.Core.Services;

public enum VoiceBackend
{
    /// <summary>小智服务器（WebSocket + Opus）</summary>
    XiaoZhi,

    /// <summary>DeepSeek：语音识别 + 对话 + 语音合成</summary>
    DeepSeek
}

/// <summary>
/// 语音对话后端协调：决定唤醒词、连续对话、按住说话走小智还是 DeepSeek，保证两者不会同时占用麦克风和扬声器。
/// </summary>
public sealed class VoiceBackendCoordinator : IDisposable
{
    private readonly IVoiceChatService _xiaozhi;
    private readonly DeepSeekVoiceChatService _deepSeek;
    private readonly ISettingsService<AppSettings>? _settings;
    private readonly DeepSeekOptions? _options;
    private readonly ISecretStore? _secrets;
    private readonly DeepSeekChatSession? _session;
    private readonly SwitchableToolExecutor? _tools;
    private readonly ILogger<VoiceBackendCoordinator>? _logger;
    private readonly SemaphoreSlim _switchGate = new(1, 1);
    private bool _initialized;

    public VoiceBackendCoordinator(
        IVoiceChatService xiaozhi,
        DeepSeekVoiceChatService deepSeek,
        ISettingsService<AppSettings>? settings = null,
        ILogger<VoiceBackendCoordinator>? logger = null,
        DeepSeekOptions? options = null,
        ISecretStore? secrets = null,
        DeepSeekChatSession? session = null,
        SwitchableToolExecutor? tools = null)
    {
        _xiaozhi = xiaozhi;
        _deepSeek = deepSeek;
        _settings = settings;
        _logger = logger;
        _options = options;
        _secrets = secrets;
        _session = session;
        _tools = tools;

        _xiaozhi.KeywordInterceptor = OnKeywordAsync;
        _deepSeek.ActiveChanged += OnDeepSeekActiveChanged;
    }

    public VoiceBackend Current { get; private set; } = VoiceBackend.XiaoZhi;

    public bool IsDeepSeek => Current == VoiceBackend.DeepSeek;

    public event EventHandler<VoiceBackend>? BackendChanged;

    /// <summary>
    /// 从设置读取上次选择的后端和音色（不会触发停止对话）
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            if (_settings != null)
            {
                var settings = await _settings.LoadSettingsAsync().ConfigureAwait(false);
                Current = ParseBackend(settings.VoiceBackend);
                _deepSeek.VoiceId = NormalizeVoice(settings.DeepSeekVoiceId);
                _deepSeek.UseThinking = settings.DeepSeekVoiceThinking;
                _deepSeek.DetectorOptions.EndSilenceMs = ClampEndSilence(settings.DeepSeekVoiceEndSilenceMs);
                _deepSeek.DetectorOptions.MinThreshold = MinLevelFor(settings.DeepSeekVoiceNoiseLevel);
                ApplyDeepSeekSettings(settings);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "读取语音后端设置失败，使用小智");
        }

        _logger?.LogInformation("语音对话后端：{Backend}", Current);
        BackendChanged?.Invoke(this, Current);
    }

    /// <summary>
    /// 启动时就把保存的 DeepSeek 设置与 API Key 应用到共享选项，这样不必先打开 DeepSeek 页面，语音/唤醒就能直接用
    /// </summary>
    private void ApplyDeepSeekSettings(AppSettings settings)
    {
        if (_options == null)
        {
            return;
        }

        try
        {
            _options.ApplySettings(settings, _secrets?.Get(SecretNames.DeepSeekApiKey));
            _session?.SetSystemPrompt(_options.SystemPrompt);
            if (_tools != null)
            {
                _tools.Enabled = settings.DeepSeekEnableTools;
            }

            _logger?.LogInformation("DeepSeek 设置已应用：模型 {Model}，API Key {KeyState}", _options.Model,
                string.IsNullOrWhiteSpace(_options.ApiKey) ? "未配置" : "已配置");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "应用 DeepSeek 设置失败");
        }
    }

    /// <summary>
    /// 切换后端：先让正在进行的对话停下，再保存选择
    /// </summary>
    public async Task SetBackendAsync(VoiceBackend backend, bool persist = true)
    {
        await _switchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Current == backend)
            {
                return;
            }

            _logger?.LogInformation("切换语音对话后端：{From} → {To}", Current, backend);

            if (backend == VoiceBackend.DeepSeek)
            {
                await StopXiaoZhiAsync().ConfigureAwait(false);
            }
            else
            {
                await _deepSeek.StopAsync().ConfigureAwait(false);
            }

            Current = backend;

            if (persist)
            {
                await PersistAsync(backend).ConfigureAwait(false);
            }
        }
        finally
        {
            _switchGate.Release();
        }

        BackendChanged?.Invoke(this, backend);
    }

    /// <summary>
    /// 保存并应用 DeepSeek 音色
    /// </summary>
    public async Task SetVoiceAsync(string voiceId)
    {
        _deepSeek.VoiceId = NormalizeVoice(voiceId);
        try
        {
            if (_settings != null)
            {
                var settings = await _settings.LoadSettingsAsync().ConfigureAwait(false);
                settings.DeepSeekVoiceId = _deepSeek.VoiceId;
                await _settings.SaveSettingsAsync(settings).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "保存 DeepSeek 音色失败");
        }
    }

    private async Task<bool> OnKeywordAsync(string keyword)
    {
        if (Current != VoiceBackend.DeepSeek)
        {
            return false;
        }

        if (_deepSeek.IsActive)
        {
            // 已在对话：说唤醒词视为打断当前回复
            await _deepSeek.InterruptAsync().ConfigureAwait(false);
            return true;
        }

        _logger?.LogInformation("唤醒词 '{Keyword}' → 启动 DeepSeek 连续语音对话", keyword);
        await _deepSeek.StartAsync(continuous: true).ConfigureAwait(false);
        return true;
    }

    private void OnDeepSeekActiveChanged(object? sender, bool active)
    {
        // DeepSeek 占用麦克风期间暂停唤醒词识别，结束后恢复
        try
        {
            _xiaozhi.HoldKeywordDetection(active);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "切换唤醒词暂停状态失败");
        }
    }

    private async Task StopXiaoZhiAsync()
    {
        try
        {
            _xiaozhi.KeepListening = false;
            if (_xiaozhi.IsVoiceChatActive || _xiaozhi.CurrentState != Constants.DeviceState.Idle)
            {
                await _xiaozhi.StopVoiceChatAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "切换后端时停止小智对话失败");
        }
    }

    private async Task PersistAsync(VoiceBackend backend)
    {
        try
        {
            if (_settings == null)
            {
                return;
            }

            var settings = await _settings.LoadSettingsAsync().ConfigureAwait(false);
            settings.VoiceBackend = backend == VoiceBackend.DeepSeek ? "deepseek" : "xiaozhi";
            await _settings.SaveSettingsAsync(settings).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "保存语音后端设置失败");
        }
    }

    /// <summary>"说完后等待多久才识别"限制在 300~2000ms：太小会切断句内停顿，太大回复显得迟钝</summary>
    public static int ClampEndSilence(int ms) => Math.Clamp(ms <= 0 ? 700 : ms, 300, 2000);

    /// <summary>环境噪声级别（0 安静 / 1 普通 / 2 嘈杂）对应的最低有声音量（RMS，0~1）</summary>
    public static double MinLevelFor(int noiseLevel) => noiseLevel switch
    {
        <= 0 => 0.008,
        1 => 0.012,
        _ => 0.03
    };

    private static VoiceBackend ParseBackend(string? value) =>
        string.Equals(value, "deepseek", StringComparison.OrdinalIgnoreCase) ? VoiceBackend.DeepSeek : VoiceBackend.XiaoZhi;

    private static string NormalizeVoice(string? voice) =>
        string.Equals(voice, DeepSeekTtsVoices.Tide, StringComparison.OrdinalIgnoreCase) ? DeepSeekTtsVoices.Tide : DeepSeekTtsVoices.Stella;

    public void Dispose()
    {
        _deepSeek.ActiveChanged -= OnDeepSeekActiveChanged;
        if (ReferenceEquals(_xiaozhi.KeywordInterceptor, (Func<string, Task<bool>>)OnKeywordAsync))
        {
            _xiaozhi.KeywordInterceptor = null;
        }

        _switchGate.Dispose();
    }
}
