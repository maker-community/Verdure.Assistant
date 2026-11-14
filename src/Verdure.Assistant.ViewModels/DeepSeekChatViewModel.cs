using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Text;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Services.DeepSeek;
using Verdure.Assistant.Core.Services.DeepSeek.Speech;

namespace Verdure.Assistant.ViewModels;

/// <summary>
/// DeepSeek 对话页 ViewModel：流式显示思维链与回答、工具调用、用量，并管理 API 设置。
/// 不依赖具体 UI 框架，所有界面更新都经由 <see cref="IUIDispatcher"/>。
/// </summary>
public partial class DeepSeekChatViewModel : ViewModelBase
{
    private readonly DeepSeekChatSession _session;
    private readonly DeepSeekOptions _options;
    private readonly IDeepSeekClient _client;
    private readonly SwitchableToolExecutor? _tools;
    private readonly ISettingsService<AppSettings>? _settingsService;
    private readonly ISecretStore _secrets;
    private readonly IUIDispatcher _ui;
    private readonly DeepSeekVoiceChatService? _voice;
    private readonly VoiceBackendCoordinator? _backend;
    private readonly IImageCaptureService? _capture;

    private AppSettings _settings = new();
    private CancellationTokenSource? _turnCts;
    private DeepSeekMessageItem? _voiceAssistant;
    private bool _initialized;

    public static IReadOnlyList<string> VoiceChoices { get; } = new[]
    {
        DeepSeekTtsVoices.Stella,
        DeepSeekTtsVoices.Tide
    };

    public static IReadOnlyList<string> ModelChoices { get; } = new[]
    {
        DeepSeekModelNames.Flash,
        DeepSeekModelNames.Pro
    };

    public static IReadOnlyList<string> EffortChoices { get; } = new[]
    {
        DeepSeekReasoningEffort.Low,
        DeepSeekReasoningEffort.High,
        DeepSeekReasoningEffort.Max
    };

    public ObservableCollection<DeepSeekMessageItem> Messages { get; } = new();

    #region 可观察属性

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewChatCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private string _balanceText = string.Empty;

    [ObservableProperty]
    private bool _isConfigured;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private string _settingsMessage = string.Empty;

    // 设置面板（保存前不会影响正在使用的选项）
    [ObservableProperty]
    private string _apiKeyInput = string.Empty;

    [ObservableProperty]
    private string _baseUrl = DeepSeekOptions.DefaultBaseUrl;

    [ObservableProperty]
    private string _model = DeepSeekModelNames.Flash;

    [ObservableProperty]
    private bool _thinkingEnabled = true;

    [ObservableProperty]
    private string _reasoningEffort = DeepSeekReasoningEffort.High;

    [ObservableProperty]
    private string _systemPrompt = string.Empty;

    [ObservableProperty]
    private bool _enableWebSearch;

    [ObservableProperty]
    private bool _enableTools = true;

    [ObservableProperty]
    private bool _speakReplies;

    /// <summary>DeepSeek 语音合成音色（stella 女声 / tide 男声）</summary>
    [ObservableProperty]
    private string _voiceId = DeepSeekTtsVoices.Stella;

    /// <summary>语音对话是否使用思考模式（更慢，但复杂问题更准）</summary>
    [ObservableProperty]
    private bool _voiceThinking;

    /// <summary>说完后停顿多久（毫秒）才开始识别；越小越快，但可能切断句内停顿</summary>
    [ObservableProperty]
    private double _voiceEndSilenceMs = 700;

    /// <summary>环境噪声级别：0 安静、1 普通、2 嘈杂</summary>
    [ObservableProperty]
    private int _voiceNoiseLevel = 1;

    public static IReadOnlyList<string> NoiseLevelChoices { get; } = new[] { "安静", "普通", "嘈杂" };

    /// <summary>语音对话（麦克风 → DeepSeek 识别 → 回复 → DeepSeek 朗读）是否进行中</summary>
    [ObservableProperty]
    private bool _isVoiceActive;

    [ObservableProperty]
    private string _voiceStatusText = string.Empty;

    #endregion

    /// <summary>
    /// 请求朗读一段回复（界面层使用平台 TTS 实现）
    /// </summary>
    public event EventHandler<string>? SpeakRequested;

    /// <summary>
    /// 请求停止朗读
    /// </summary>
    public event EventHandler? StopSpeakingRequested;

    /// <summary>
    /// 请求滚动到底部
    /// </summary>
    public event EventHandler? ScrollToBottomRequested;

    public DeepSeekChatViewModel(
        ILogger<DeepSeekChatViewModel> logger,
        DeepSeekChatSession session,
        DeepSeekOptions options,
        IDeepSeekClient client,
        SwitchableToolExecutor? tools = null,
        ISettingsService<AppSettings>? settingsService = null,
        ISecretStore? secretStore = null,
        IUIDispatcher? uiDispatcher = null,
        DeepSeekVoiceChatService? voice = null,
        VoiceBackendCoordinator? backend = null,
        IImageCaptureService? imageCapture = null) : base(logger)
    {
        _session = session;
        _options = options;
        _client = client;
        _tools = tools;
        _settingsService = settingsService;
        _secrets = secretStore ?? new InMemorySecretStore();
        _ui = uiDispatcher ?? new DefaultUIDispatcher();
        _voice = voice;
        _backend = backend;
        _capture = imageCapture;

        Attachments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAttachments));
            SendCommand.NotifyCanExecuteChanged();
        };
    }

    public bool IsVoiceAvailable => _voice != null;

    private bool _voiceAttached;

    private void AttachVoice()
    {
        if (_voice == null || _voiceAttached)
        {
            return;
        }

        _voiceAttached = true;

        _voice.ActiveChanged += (_, active) => _ = _ui.InvokeAsync(() =>
        {
            IsVoiceActive = active;
            VoiceStatusText = active ? "语音对话中" : string.Empty;
        });

        _voice.PhaseChanged += (_, phase) => _ = _ui.InvokeAsync(() =>
        {
            if (!_voice.IsActive && phase == DeepSeekVoicePhase.Idle)
            {
                VoiceStatusText = string.Empty;
                return;
            }

            VoiceStatusText = phase switch
            {
                DeepSeekVoicePhase.Listening => "🎤 请说话…",
                DeepSeekVoicePhase.Transcribing => "识别中…",
                DeepSeekVoicePhase.Thinking => "思考中…",
                DeepSeekVoicePhase.Speaking => "🔊 朗读中…",
                _ => "语音对话中"
            };
        });

        _voice.TurnStarted += (_, text) => _ = _ui.InvokeAsync(() =>
        {
            var user = new DeepSeekMessageItem(DeepSeekMessageRole.User) { Content = text };
            var assistant = new DeepSeekMessageItem(DeepSeekMessageRole.Assistant) { IsStreaming = true };
            Messages.Add(user);
            Messages.Add(assistant);
            _voiceAssistant = assistant;
            IsBusy = true;
            ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
        });

        _voice.ChatEventReceived += (_, ev) => _ = _ui.InvokeAsync(() =>
        {
            if (_voiceAssistant != null)
            {
                // 朗读由 DeepSeek 语音合成负责，这里不能再触发系统朗读，否则会重复发声
                ApplyEvent(_voiceAssistant, ev, allowSystemSpeech: false);
            }
        });

        _voice.TurnEnded += (_, _) => _ = _ui.InvokeAsync(() =>
        {
            if (_voiceAssistant != null)
            {
                _voiceAssistant.IsStreaming = false;
                _voiceAssistant = null;
            }

            IsBusy = false;
        });

        _voice.ErrorOccurred += (_, error) => _ = _ui.InvokeAsync(() => StatusText = error);
        _voice.InfoOccurred += (_, info) => _ = _ui.InvokeAsync(() => StatusText = info);
    }

    [RelayCommand]
    private async Task ToggleVoiceAsync()
    {
        if (_voice == null)
        {
            return;
        }

        if (_voice.IsActive)
        {
            await _voice.StopAsync().ConfigureAwait(false);
            return;
        }

        if (!IsConfigured)
        {
            StatusText = "请先配置 DeepSeek API Key";
            IsSettingsOpen = true;
            return;
        }

        if (IsBusy)
        {
            return;
        }

        _voice.VoiceId = VoiceId;
        if (_backend != null)
        {
            // 在 DeepSeek 页开启语音，等同于把语音后端切到 DeepSeek（同时让小智对话停下，避免抢麦克风）
            await _backend.SetBackendAsync(VoiceBackend.DeepSeek).ConfigureAwait(false);
        }

        await _voice.StartAsync(continuous: true).ConfigureAwait(false);
    }

    public override async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;

        await base.InitializeAsync();

        try
        {
            if (_settingsService != null)
            {
                _settings = await _settingsService.LoadSettingsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载 DeepSeek 设置失败，使用默认值");
        }

        LoadSettingsIntoPanel();
        ApplyToRuntime();
        AttachVoice();

        if (!IsConfigured)
        {
            StatusText = "请先配置 DeepSeek API Key";
            IsSettingsOpen = true;
        }
        else
        {
            _ = RefreshBalanceAsync();
        }
    }

    private void LoadSettingsIntoPanel()
    {
        BaseUrl = _settings.DeepSeekBaseUrl;
        Model = _settings.DeepSeekModel;
        ThinkingEnabled = _settings.DeepSeekThinkingEnabled;
        ReasoningEffort = _settings.DeepSeekReasoningEffort;
        SystemPrompt = _settings.DeepSeekSystemPrompt;
        EnableWebSearch = _settings.DeepSeekEnableWebSearch;
        EnableTools = _settings.DeepSeekEnableTools;
        SpeakReplies = _settings.DeepSeekSpeakReplies;
        VoiceId = string.IsNullOrWhiteSpace(_settings.DeepSeekVoiceId) ? DeepSeekTtsVoices.Stella : _settings.DeepSeekVoiceId;
        VoiceThinking = _settings.DeepSeekVoiceThinking;
        VoiceEndSilenceMs = VoiceBackendCoordinator.ClampEndSilence(_settings.DeepSeekVoiceEndSilenceMs);
        VoiceNoiseLevel = Math.Clamp(_settings.DeepSeekVoiceNoiseLevel, 0, 2);
        ApiKeyInput = string.Empty; // 不回显已保存的密钥
    }

    /// <summary>
    /// 把已保存的设置应用到共享选项/会话（保存设置或启动时调用）
    /// </summary>
    private void ApplyToRuntime()
    {
        var apiKey = _secrets.Get(SecretNames.DeepSeekApiKey);
        _options.ApplySettings(_settings, apiKey);
        _session.SetSystemPrompt(_options.SystemPrompt);

        if (_tools != null)
        {
            _tools.Enabled = _settings.DeepSeekEnableTools;
        }

        IsConfigured = !string.IsNullOrWhiteSpace(_options.ApiKey);

        if (_voice != null)
        {
            _voice.VoiceId = VoiceId;
            _voice.UseThinking = VoiceThinking;
            _voice.DetectorOptions.EndSilenceMs = VoiceBackendCoordinator.ClampEndSilence((int)VoiceEndSilenceMs);
            _voice.DetectorOptions.MinThreshold = VoiceBackendCoordinator.MinLevelFor(VoiceNoiseLevel);
        }
    }

    #region 命令

    private bool CanSend() => !IsBusy && (!string.IsNullOrWhiteSpace(InputText) || Attachments.Count > 0);

    #region 图片附件

    /// <summary>待发送的图片（发送后清空）。图片会随这条消息一起交给 deepseek-flash 理解。</summary>
    public ObservableCollection<AttachedImageItem> Attachments { get; } = new();

    public bool HasAttachments => Attachments.Count > 0;

    public bool CanCaptureCamera => _capture?.IsSupported(ImageCaptureSource.Camera) == true;

    public bool CanCaptureScreen => _capture?.IsSupported(ImageCaptureSource.Screen) == true;

    /// <summary>附加一张图片（文件、剪贴板或拍照得到的字节）。格式不受支持或过大时返回 false 并提示。</summary>
    public bool AddImage(string name, byte[] data)
    {
        try
        {
            var image = DeepSeekImage.FromBytes(data, name);
            Attachments.Add(new AttachedImageItem(image));
            StatusText = $"已添加图片：{name}";
            return true;
        }
        catch (ArgumentException ex)
        {
            StatusText = ex.Message;
            return false;
        }
    }

    [RelayCommand]
    private void RemoveAttachment(AttachedImageItem? item)
    {
        if (item != null)
        {
            Attachments.Remove(item);
        }
    }

    [RelayCommand]
    private Task CaptureCameraAsync() => CaptureAndAttachAsync(ImageCaptureSource.Camera, "拍照");

    [RelayCommand]
    private Task CaptureScreenAsync() => CaptureAndAttachAsync(ImageCaptureSource.Screen, "屏幕截图");

    private async Task CaptureAndAttachAsync(ImageCaptureSource source, string label)
    {
        if (_capture == null || IsBusy)
        {
            return;
        }

        try
        {
            StatusText = source == ImageCaptureSource.Camera ? "正在拍照…" : "正在截屏…";
            var image = await _capture.CaptureAsync(source).ConfigureAwait(false);
            await _ui.InvokeAsync(() =>
            {
                var name = $"{label}-{DateTime.Now:HHmmss}.jpg";
                Attachments.Add(new AttachedImageItem(new DeepSeekImage(image.Data, image.MimeType, name)));
                StatusText = $"已添加{label}，输入问题后发送";
            }).ConfigureAwait(false);
        }
        catch (ImageCaptureException ex)
        {
            await _ui.InvokeAsync(() => StatusText = ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Label}失败", label);
            await _ui.InvokeAsync(() => StatusText = $"{label}失败：{ex.Message}").ConfigureAwait(false);
        }
    }

    #endregion

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = InputText.Trim();
        var images = Attachments.Select(a => a.Image).ToList();
        if (text.Length == 0 && images.Count == 0)
        {
            return;
        }

        if (!IsConfigured)
        {
            StatusText = "请先配置 DeepSeek API Key";
            IsSettingsOpen = true;
            return;
        }

        StopSpeakingRequested?.Invoke(this, EventArgs.Empty);
        InputText = string.Empty;
        Attachments.Clear();
        IsBusy = true;

        var user = new DeepSeekMessageItem(DeepSeekMessageRole.User)
        {
            Content = text,
            ImageNote = images.Count > 0 ? $"📷 {images.Count} 张图片" : string.Empty
        };
        var assistant = new DeepSeekMessageItem(DeepSeekMessageRole.Assistant) { IsStreaming = true };
        Messages.Add(user);
        Messages.Add(assistant);
        ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);

        _turnCts = new CancellationTokenSource();
        var token = _turnCts.Token;
        StatusText = images.Count > 0 ? "识别图片中…" : _options.ThinkingEnabled ? "思考中…" : "生成中…";

        try
        {
            var events = images.Count > 0 ? _session.SendAsync(text, images, token) : _session.SendAsync(text, token);
            await foreach (var ev in events.ConfigureAwait(false))
            {
                await _ui.InvokeAsync(() => ApplyEvent(assistant, ev)).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // SendAsync 约定只通过 Failed 事件报告运行期错误；这里兜底参数错误等异常
            await _ui.InvokeAsync(() => ApplyEvent(assistant, new DeepSeekChatEvent.Failed(ex))).ConfigureAwait(false);
        }
        finally
        {
            await _ui.InvokeAsync(() =>
            {
                assistant.IsStreaming = false;
                IsBusy = false;
                var cts = Interlocked.Exchange(ref _turnCts, null);
                cts?.Dispose();
            }).ConfigureAwait(false);
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Stop()
    {
        try { _turnCts?.Cancel(); } catch (ObjectDisposedException) { }
        _session.CancelCurrent();
        if (_voice?.IsActive == true)
        {
            _ = _voice.InterruptAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void NewChat()
    {
        StopSpeakingRequested?.Invoke(this, EventArgs.Empty);
        _session.Reset();
        Messages.Clear();
        StatusText = "已开始新的对话";
    }

    private bool CanReset() => !IsBusy;

    [RelayCommand]
    private async Task UseSuggestionAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || IsBusy)
        {
            return;
        }

        InputText = text;
        await SendCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            var newKey = ApiKeyInput?.Trim();
            if (!string.IsNullOrEmpty(newKey))
            {
                _secrets.Set(SecretNames.DeepSeekApiKey, newKey);
            }

            _settings.DeepSeekBaseUrl = BaseUrl;
            _settings.DeepSeekModel = Model;
            _settings.DeepSeekThinkingEnabled = ThinkingEnabled;
            _settings.DeepSeekReasoningEffort = ReasoningEffort;
            _settings.DeepSeekSystemPrompt = SystemPrompt;
            _settings.DeepSeekEnableWebSearch = EnableWebSearch;
            _settings.DeepSeekEnableTools = EnableTools;
            _settings.DeepSeekSpeakReplies = SpeakReplies;
            _settings.DeepSeekVoiceId = VoiceId;
            _settings.DeepSeekVoiceThinking = VoiceThinking;
            _settings.DeepSeekVoiceEndSilenceMs = VoiceBackendCoordinator.ClampEndSilence((int)VoiceEndSilenceMs);
            _settings.DeepSeekVoiceNoiseLevel = Math.Clamp(VoiceNoiseLevel, 0, 2);

            var candidate = new DeepSeekOptions();
            candidate.ApplySettings(_settings, _secrets.Get(SecretNames.DeepSeekApiKey) ?? "placeholder");
            var problems = candidate.Validate();
            if (problems.Count > 0)
            {
                SettingsMessage = string.Join("；", problems);
                return;
            }

            if (_settingsService != null)
            {
                await _settingsService.SaveSettingsAsync(_settings);
            }

            ApplyToRuntime();
            ApiKeyInput = string.Empty;
            SettingsMessage = IsConfigured ? "设置已保存" : "设置已保存，但仍缺少 API Key";
            StatusText = IsConfigured ? "就绪" : "请先配置 DeepSeek API Key";

            if (IsConfigured)
            {
                _ = RefreshBalanceAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存 DeepSeek 设置失败");
            SettingsMessage = $"保存失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearApiKey()
    {
        _secrets.Set(SecretNames.DeepSeekApiKey, null);
        ApiKeyInput = string.Empty;
        ApplyToRuntime();
        BalanceText = string.Empty;
        SettingsMessage = "API Key 已清除";
        StatusText = "请先配置 DeepSeek API Key";
    }

    [RelayCommand]
    private async Task RefreshBalanceAsync()
    {
        if (!IsConfigured)
        {
            BalanceText = string.Empty;
            return;
        }

        try
        {
            var balance = await _client.GetBalanceAsync().ConfigureAwait(false);
            var text = balance.BalanceInfos.Count == 0
                ? "余额：未知"
                : "余额：" + string.Join(" / ", balance.BalanceInfos.Select(b => $"{b.TotalBalance} {b.Currency}"));
            if (!balance.IsAvailable)
            {
                text += "（余额不足，无法调用）";
            }

            await _ui.InvokeAsync(() => BalanceText = text).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "获取 DeepSeek 余额失败");
            await _ui.InvokeAsync(() => BalanceText = FriendlyError(ex)).ConfigureAwait(false);
        }
    }

    #endregion

    private void ApplyEvent(DeepSeekMessageItem assistant, DeepSeekChatEvent ev, bool allowSystemSpeech = true)
    {
        switch (ev)
        {
            case DeepSeekChatEvent.ReasoningDelta reasoning:
                assistant.AppendReasoning(reasoning.Text);
                StatusText = "思考中…";
                break;

            case DeepSeekChatEvent.ContentDelta content:
                assistant.AppendContent(content.Text);
                StatusText = "回答中…";
                break;

            case DeepSeekChatEvent.ToolCallRequested call:
                assistant.AddToolLine($"🔧 调用工具 {call.Name}");
                StatusText = $"调用工具 {call.Name}…";
                break;

            case DeepSeekChatEvent.ToolCallCompleted done:
                assistant.AddToolLine(done.IsError ? $"⚠️ 工具 {done.Name} 执行失败" : $"✅ 工具 {done.Name} 完成");
                StatusText = "整理结果…";
                break;

            case DeepSeekChatEvent.UsageReported usage:
                assistant.UsageText = FormatUsage(usage.Usage);
                break;

            case DeepSeekChatEvent.Completed completed:
                assistant.IsStreaming = false;
                assistant.IsReasoningExpanded = false;
                if (string.IsNullOrEmpty(assistant.Content) && !string.IsNullOrEmpty(completed.Content))
                {
                    assistant.Content = completed.Content;
                }
                if (completed.Usage != null)
                {
                    assistant.UsageText = FormatUsage(completed.Usage);
                }
                StatusText = "就绪";
                _ = RefreshBalanceAsync();

                if (allowSystemSpeech && SpeakReplies && !string.IsNullOrWhiteSpace(completed.Content))
                {
                    SpeakRequested?.Invoke(this, completed.Content);
                }
                break;

            case DeepSeekChatEvent.Failed failed:
                assistant.IsStreaming = false;
                if (failed.IsCancelled)
                {
                    assistant.Footnote = "（已停止，本轮不计入上下文）";
                    StatusText = "已停止";
                }
                else
                {
                    var message = FriendlyError(failed.Exception);
                    assistant.IsError = true;
                    assistant.Footnote = $"⚠️ {message}（本轮不计入上下文）";
                    StatusText = message;
                    _logger.LogWarning(failed.Exception, "DeepSeek 对话失败");
                }
                break;
        }

        ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatUsage(DeepSeekUsage usage)
    {
        var sb = new StringBuilder();
        sb.Append($"输入 {usage.PromptTokens}");
        if (usage.PromptCacheHitTokens > 0)
        {
            sb.Append($"（缓存命中 {usage.PromptCacheHitTokens}）");
        }
        sb.Append($" · 输出 {usage.CompletionTokens}");
        if (usage.ReasoningTokens > 0)
        {
            sb.Append($"（思考 {usage.ReasoningTokens}）");
        }
        return sb.ToString();
    }

    internal static string FriendlyError(Exception ex) => ex switch
    {
        DeepSeekApiException { IsAuthenticationError: true } => "API Key 无效或已过期，请在设置中重新填写",
        DeepSeekApiException { IsInsufficientBalance: true } => "账户余额不足，请先充值",
        DeepSeekApiException { IsRateLimited: true } => "请求过于频繁，请稍后再试",
        DeepSeekApiException api => api.Message,
        TimeoutException => "请求超时，请检查网络后重试",
        HttpRequestException => "网络连接失败，请检查网络后重试",
        _ => ex.Message
    };

    public override void Cleanup()
    {
        try { _turnCts?.Cancel(); } catch (ObjectDisposedException) { }
        base.Cleanup();
    }
}

/// <summary>待发送的图片附件</summary>
public sealed class AttachedImageItem
{
    public AttachedImageItem(DeepSeekImage image)
    {
        Image = image;
        Name = string.IsNullOrWhiteSpace(image.Name) ? "图片" : image.Name!;
        SizeText = image.Data.Length >= 1024 * 1024
            ? $"{image.Data.Length / 1024.0 / 1024.0:0.0} MB"
            : $"{Math.Max(1, image.Data.Length / 1024)} KB";
    }

    public DeepSeekImage Image { get; }
    public string Name { get; }
    public string SizeText { get; }
    public string Label => $"{Name} · {SizeText}";
}

public enum DeepSeekMessageRole
{
    User,
    Assistant
}

/// <summary>
/// 对话列表项：回答与思维链随流式事件增量更新
/// </summary>
public partial class DeepSeekMessageItem : ObservableObject
{
    private readonly StringBuilder _contentBuilder = new();
    private readonly StringBuilder _reasoningBuilder = new();
    private readonly StringBuilder _toolsBuilder = new();

    public DeepSeekMessageItem(DeepSeekMessageRole role)
    {
        Role = role;
        Timestamp = DateTime.Now;
    }

    public DeepSeekMessageRole Role { get; }
    public DateTime Timestamp { get; }
    public bool IsUser => Role == DeepSeekMessageRole.User;
    public bool IsAssistant => Role == DeepSeekMessageRole.Assistant;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContent))]
    private string _content = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReasoning))]
    private string _reasoning = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTools))]
    private string _toolSummary = string.Empty;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private bool _isReasoningExpanded;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private string _usageText = string.Empty;

    [ObservableProperty]
    private string _footnote = string.Empty;

    /// <summary>用户消息附带图片时的提示，如"📷 2 张图片"</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImageNote))]
    private string _imageNote = string.Empty;

    public bool HasImageNote => !string.IsNullOrEmpty(ImageNote);
    public bool HasContent => !string.IsNullOrEmpty(Content);
    public bool HasReasoning => !string.IsNullOrEmpty(Reasoning);
    public bool HasTools => !string.IsNullOrEmpty(ToolSummary);

    public void AppendContent(string text)
    {
        _contentBuilder.Append(text);
        Content = _contentBuilder.ToString();
    }

    public void AppendReasoning(string text)
    {
        _reasoningBuilder.Append(text);
        Reasoning = _reasoningBuilder.ToString();
        if (!IsReasoningExpanded)
        {
            IsReasoningExpanded = true; // 思考过程中展开，完成后自动折叠
        }
    }

    public void AddToolLine(string line)
    {
        if (_toolsBuilder.Length > 0)
        {
            _toolsBuilder.AppendLine();
        }
        _toolsBuilder.Append(line);
        ToolSummary = _toolsBuilder.ToString();
    }
}
