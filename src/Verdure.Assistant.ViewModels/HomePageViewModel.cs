using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services;

namespace Verdure.Assistant.ViewModels;

/// <summary>
/// 主页ViewModel - 语音对话界面逻辑
/// </summary>
public partial class HomePageViewModel : ViewModelBase
{    private readonly IVoiceChatService? _voiceChatService;
    private readonly IEmotionManager? _emotionManager;
    private readonly IKeywordSpottingService? _keywordSpottingService;
    private readonly IVerificationService? _verificationService;

    // UI thread dispatcher for cross-platform thread marshaling
    private IUIDispatcher _uiDispatcher;


    private InterruptManager? _interruptManager;

    #region 可观察属性

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private bool _isListening = false;

    [ObservableProperty]
    private bool _isAutoMode = false;

    [ObservableProperty]
    private bool _isPushToTalkActive = false;

    [ObservableProperty]
    private bool _isWaitingForResponse = false;

    [ObservableProperty]
    private string _statusText = "未连接";

    [ObservableProperty]
    private string _connectionStatusText = "离线";

    [ObservableProperty]
    private string _ttsText = "待命";

    [ObservableProperty]
    private string _defaultEmotionText = "😊";

    [ObservableProperty]
    private string _modeToggleText = "手动";

    [ObservableProperty]
    private string _autoButtonText = "开始对话";

    [ObservableProperty]
    private string _manualButtonText = "按住说话";

    [ObservableProperty]
    private double _volumeValue = 80;

    [ObservableProperty]
    private string _volumeText = "80%";

    [ObservableProperty]
    private string _currentMessage = string.Empty;

    [ObservableProperty]
    private bool _showMicrophoneVisualizer = false;

    [ObservableProperty]
    private string _serverUrl = "ws://localhost:8080/ws";

    // 验证码相关属性
    [ObservableProperty]
    private bool _isVerificationCodeVisible = false;

    [ObservableProperty]
    private string _verificationCode = string.Empty;

    [ObservableProperty]
    private string _verificationCodeMessage = string.Empty;

    // 音乐播放器相关属性
    [ObservableProperty]
    private string _currentSongName = string.Empty;

    [ObservableProperty]
    private string _currentArtist = string.Empty;

    [ObservableProperty]
    private string _currentLyric = string.Empty;

    [ObservableProperty]
    private double _musicPosition = 0.0;

    [ObservableProperty]
    private double _musicDuration = 0.0;

    [ObservableProperty]
    private string _musicStatus = "停止";

    // 系统状态信息
    [ObservableProperty]
    private string _systemStatusText = string.Empty;

    [ObservableProperty]
    private string _iotStatusText = string.Empty;

    [ObservableProperty]
    private string _currentEmotion = "😊";

    // Manual按钮可用状态 - 基于连接状态、推送说话状态和等待响应状态
    public bool IsManualButtonEnabled => IsConnected && !IsPushToTalkActive && !IsWaitingForResponse;

    #endregion

    #region 集合

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();

    #endregion      
    public HomePageViewModel(ILogger<HomePageViewModel> logger,
        IVoiceChatService? voiceChatService = null,
        IEmotionManager? emotionManager = null,
        InterruptManager? interruptManager = null,
        IKeywordSpottingService? keywordSpottingService = null,
        IVerificationService? verificationService = null,
        IUIDispatcher? uiDispatcher = null) : base(logger)
    {
        _voiceChatService = voiceChatService;
        _emotionManager = emotionManager;
        _interruptManager = interruptManager;
        _keywordSpottingService = keywordSpottingService;
        _verificationService = verificationService;

        // 设置初始状态
        InitializeDefaultState();
        _uiDispatcher = uiDispatcher ?? new DefaultUIDispatcher();
    }

    private void InitializeDefaultState()
    {
        StatusText = "未连接";
        ConnectionStatusText = "离线";
        TtsText = "待命";
        DefaultEmotionText = "😊";
        VolumeValue = 80;
        UpdateVolumeText(80);
        ModeToggleText = "手动";
        ManualButtonText = "按住说话";
        AutoButtonText = "开始对话";
        SetEmotion("neutral");
    }    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // 初始化EmotionManager
        if (_emotionManager != null)
        {
            try
            {
                await _emotionManager.InitializeAsync();
                _logger?.LogInformation("EmotionManager initialized successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize EmotionManager");
            }
        }

        // 绑定服务事件
        await BindEventsAsync();
    }

    private async Task BindEventsAsync()
    {        // 绑定语音服务事件
        if (_voiceChatService != null)
        {
            _voiceChatService.DeviceStateChanged += OnDeviceStateChanged;
            _voiceChatService.VoiceChatStateChanged += OnVoiceChatStateChanged;
            _voiceChatService.MessageReceived += OnMessageReceived;
            _voiceChatService.ErrorOccurred += OnErrorOccurred;
            _voiceChatService.MusicMessageReceived += OnMusicMessageReceived;
            _voiceChatService.SystemStatusMessageReceived += OnSystemStatusMessageReceived;
            _voiceChatService.IotMessageReceived += OnIotMessageReceived;
            _voiceChatService.LlmMessageReceived += OnLlmMessageReceived;
            _voiceChatService.TtsStateChanged += OnTtsStateChanged;
        }// 初始化和绑定InterruptManager事件
        if (_interruptManager != null)
        {
            try
            {
                await _interruptManager.InitializeAsync();
                _interruptManager.InterruptTriggered += OnInterruptTriggered;
                _logger?.LogInformation("InterruptManager initialized successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize InterruptManager");
            }
        }

        // 设置关键词检测服务（对应py-xiaozhi的wake_word_detector集成）
        if (_voiceChatService != null && _keywordSpottingService != null && _interruptManager != null)
        {
            try
            {
                _voiceChatService.SetInterruptManager(_interruptManager);
                _voiceChatService.SetKeywordSpottingService(_keywordSpottingService);
                _logger?.LogInformation("关键词唤醒服务已集成到语音聊天服务");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to integrate keyword spotting service");
            }
        }
    }

    #region 事件处理

    private void OnDeviceStateChanged(object? sender, DeviceState state)
    {
        // 使用UI调度器确保线程安全的事件处理
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            switch (state)
            {
                case DeviceState.Listening:
                    StatusText = "正在聆听";
                    SetEmotion("listening");
                    break;
                case DeviceState.Speaking:
                    StatusText = "正在播放";
                    SetEmotion("speaking");
                    break;
                case DeviceState.Connecting:
                    StatusText = "连接中";
                    SetEmotion("thinking");
                    break;
                case DeviceState.Idle:
                default:
                    StatusText = "待命";
                    SetEmotion("neutral");

                    // Reset push-to-talk state when AI response completes
                    if (IsWaitingForResponse)
                    {
                        IsWaitingForResponse = false;
                        IsPushToTalkActive = false;
                        RestoreManualButtonState();
                        AddMessage("✅ AI 回复完成，可以继续对话");
                    }
                    break;
            }

        });
    }

    private void OnVoiceChatStateChanged(object? sender, bool isActive)
    {
        // 使用UI调度器确保线程安全的事件处理
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            IsListening = isActive;
            ShowMicrophoneVisualizer = isActive;

            // Update auto button text when in auto mode
            if (IsAutoMode)
            {
                if (_voiceChatService?.KeepListening == true && IsListening)
                {
                    AutoButtonText = "停止对话";
                }
                else if (_voiceChatService?.KeepListening == false || !IsListening)
                {
                    AutoButtonText = "开始对话";
                }
            }

        });            
    }    private void OnMessageReceived(object? sender, ChatMessage message)
    {
        // 使用UI调度器确保线程安全的事件处理
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            var displayText = message.Role switch
            {
                "user" => $"用户: {message.Content}",
                "assistant" => $"绿荫助手: {message.Content}",
                _ => message.Content
            };

            AddMessage(displayText, false);

            // 如果是助手消息，更新TTS文本
            if (message.Role == "assistant")
            {
                TtsText = message.Content;
                
                // 检查是否包含验证码
                _ = HandleVerificationCodeAsync(message.Content);
            }
        });
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        // 使用UI调度器确保线程安全的事件处理
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            AddMessage($"错误: {error}", true);
            _logger?.LogError("Voice chat error: {Error}", error);
        });
    }

    private void OnInterruptTriggered(object? sender, InterruptEventArgs e)
    {
        try
        {
            _logger?.LogInformation("Interrupt triggered: {Reason} - {Description}", e.Reason, e.Description);

            // 在UI线程中处理中断需要通过事件通知View
            InterruptTriggered?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to process interrupt event");
        }
    }


    private void OnMusicMessageReceived(object? sender, MusicMessage message)
    {
        // 使用UI调度器确保线程安全的事件处理
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            switch (message.Action?.ToLowerInvariant())
            {
                case "play":
                    CurrentSongName = message.SongName ?? string.Empty;
                    CurrentArtist = message.Artist ?? string.Empty;
                    MusicDuration = message.Duration;
                    MusicStatus = "播放中";
                    AddMessage($"🎵 开始播放: {message.SongName} - {message.Artist}", false);
                    break;

                case "pause":
                    MusicStatus = "暂停";
                    AddMessage("⏸️ 音乐已暂停", false);
                    break;

                case "stop":
                    MusicStatus = "停止";
                    CurrentLyric = string.Empty;
                    AddMessage("⏹️ 音乐已停止", false);
                    break;

                case "lyric_update":
                    if (!string.IsNullOrEmpty(message.LyricText))
                    {
                        MusicPosition = message.Position;
                        // 格式化歌词显示，参考py-xiaozhi的实现
                        var positionStr = FormatTime(message.Position);
                        var durationStr = FormatTime(message.Duration);
                        CurrentLyric = $"[{positionStr}/{durationStr}] {message.LyricText}";
                        AddMessage($"🎤 {CurrentLyric}", false);
                    }
                    break;

                case "seek":
                    MusicPosition = message.Position;
                    break;
            }
        });
    }

    private void OnSystemStatusMessageReceived(object? sender, SystemStatusMessage message)
    {
        // 使用UI调度器确保线程安全的事件处理
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            var statusText = $"{message.Component}: {message.Status}";
            if (!string.IsNullOrEmpty(message.Message))
            {
                statusText += $" - {message.Message}";
            }
            
            SystemStatusText = statusText;
            AddMessage($"📊 {statusText}", false);
        });
    }

    private void OnIotMessageReceived(object? sender, IotMessage message)
    {
        // 使用UI调度器确保线程安全的事件处理
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            var iotInfo = "IoT设备状态更新";
            if (message.States != null)
            {
                iotInfo += $": {message.States}";
            }
            
            IotStatusText = iotInfo;
            AddMessage($"🏠 {iotInfo}", false);
        });
    }    
    
    private void OnLlmMessageReceived(object? sender, LlmMessage message)
    {
        // 使用UI调度器确保线程安全的事件处理
        _ = _uiDispatcher.InvokeAsync(async () =>
        {
            if (!string.IsNullOrEmpty(message.Emotion))
            {
                // 更新情感显示，优先使用GIF动画
                await UpdateEmotionDisplayAsync(message.Emotion);
                AddMessage($"😊 情感变化: {message.Emotion}", false);
            }
        });
    }

    private void OnTtsStateChanged(object? sender, TtsMessage message)
    {
        // 使用UI调度器确保线程安全的事件处理
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            switch (message.State?.ToLowerInvariant())
            {
                case "start":
                    TtsText = "正在说话...";
                    break;
                case "stop":
                    TtsText = "待命";
                    break;
                case "sentence_start":
                    if (!string.IsNullOrEmpty(message.Text))
                    {
                        TtsText = message.Text;
                    }
                    break;
            }
        });
    }
    #endregion

    #region 命令

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsConnected || _voiceChatService == null) return;

        try
        {
            StatusText = "连接中";
            ConnectionStatusText = "连接中";

            // 创建配置
            var config = new VerdureConfig
            {
                ServerUrl = ServerUrl,
                UseWebSocket = true,
                EnableVoice = true,
                AudioSampleRate = 16000,
                AudioChannels = 1,
                AudioFormat = "opus"
            };

            await _voiceChatService.InitializeAsync(config);

            // Set up wake word detector coordination
            if (_interruptManager != null)
            {
                _voiceChatService.SetInterruptManager(_interruptManager);
                _logger?.LogInformation("Wake word detector coordination enabled");
            }

            // Use the service's IsConnected property to determine actual connection state
            bool isConnected = _voiceChatService.IsConnected;
            UpdateConnectionState(isConnected);

            if (isConnected)
            {
                AddMessage("连接成功");
                StatusText = "已连接";

                // 启动关键词检测（对应py-xiaozhi的关键词唤醒功能）
                await StartKeywordDetectionAsync();
            }
            else
            {
                AddMessage("连接失败: 服务未连接", true);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to voice chat service");
            AddMessage($"连接失败: {ex.Message}", true);
            UpdateConnectionState(false);
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!IsConnected || _voiceChatService == null) return;

        try
        {
            // Reset push-to-talk state before disconnecting
            if (IsPushToTalkActive || IsWaitingForResponse)
            {
                IsPushToTalkActive = false;
                IsWaitingForResponse = false;
                RestoreManualButtonState();
            }            // 停止当前语音对话
            if (IsListening)
            {
                await _voiceChatService.StopVoiceChatAsync();
            }            // 停止关键词检测
            await StopKeywordDetectionAsync();

            // 清理事件订阅
            CleanupEventSubscriptions();

            _voiceChatService.Dispose();

            // 重置所有状态
            UpdateConnectionState(false);
            IsListening = false;

            AddMessage("已断开连接");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to disconnect from voice chat service");
            AddMessage($"断开连接失败: {ex.Message}", true);
            UpdateConnectionState(false);
        }
    }

    [RelayCommand]
    private async Task StartManualRecordingAsync()
    {
        if (_voiceChatService == null || !IsConnected || IsPushToTalkActive || IsWaitingForResponse)
            return;

        try
        {
            IsPushToTalkActive = true;

            if (!IsListening)
            {
                await _voiceChatService.StartVoiceChatAsync();
                SetManualButtonRecordingState();
                AddMessage("🎤 正在录音... 松开按钮结束录音");
                _logger?.LogInformation("Push-to-talk activated, recording started");
            }
        }
        catch (Exception ex)
        {
            IsPushToTalkActive = false;
            RestoreManualButtonState();
            _logger?.LogError(ex, "Failed to start push-to-talk recording");
            AddMessage($"开始录音失败: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task StopManualRecordingAsync()
    {
        if (_voiceChatService == null || !IsConnected || !IsPushToTalkActive)
            return;

        try
        {
            if (IsListening)
            {
                await _voiceChatService.StopVoiceChatAsync();
                IsPushToTalkActive = false;
                IsWaitingForResponse = true;
                SetManualButtonProcessingState();
                AddMessage("录音结束，正在处理和等待回复...");
            }
        }
        catch (Exception ex)
        {
            IsPushToTalkActive = false;
            IsWaitingForResponse = false;
            RestoreManualButtonState();
            _logger?.LogError(ex, "Failed to stop manual voice chat");
            AddMessage($"停止录音失败: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task ToggleAutoModeAsync()
    {
        if (_voiceChatService == null || !IsConnected) return;

        try
        {
            if (!IsListening)
            {
                _voiceChatService.KeepListening = true;
                await _voiceChatService.ToggleChatStateAsync();
                AutoButtonText = "停止对话";
                AddMessage("自动对话已开始");
            }
            else
            {
                _voiceChatService.KeepListening = false;
                await _voiceChatService.ToggleChatStateAsync();
                AutoButtonText = "开始对话";
                AddMessage("自动对话已停止");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to toggle auto chat mode");
            AddMessage($"切换自动对话失败: {ex.Message}", true);
        }
    }


    [RelayCommand]
    private async Task AbortAsync()
    {
        try
        {
            if (_voiceChatService != null && (_voiceChatService.IsVoiceChatActive || _voiceChatService.CurrentState != DeviceState.Idle))
            {
                await _voiceChatService.InterruptAsync(AbortReason.UserInterruption);
                AddMessage("已中断当前操作");
                TtsText = "待命";
                SetEmotion("neutral");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to abort current operation");
            AddMessage($"中断操作失败: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var message = CurrentMessage.Trim();
        if (string.IsNullOrEmpty(message) || _voiceChatService == null || !IsConnected)
            return;

        try
        {
            AddMessage($"我: {message}", false);
            CurrentMessage = "";
            await _voiceChatService.SendTextMessageAsync(message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send text message");
            AddMessage($"发送失败: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private void ToggleMode()
    {
        IsAutoMode = !IsAutoMode;
        UpdateModeUI(IsAutoMode);
        AddMessage($"已切换到{(IsAutoMode ? "自动" : "手动")}对话模式");
    }

    [RelayCommand]
    private void ToggleMute()
    {
        var isMuted = VolumeValue == 0;
        VolumeValue = isMuted ? 80 : 0;
    }

    [RelayCommand]
    private async Task ToggleKeywordDetectionAsync()
    {
        if (_voiceChatService == null || !IsConnected) return;

        try
        {            if (_voiceChatService.IsKeywordDetectionEnabled)
            {
                await StopKeywordDetectionAsync();
                AddMessage("🔇 关键词唤醒已关闭");
            }
            else
            {
                await StartKeywordDetectionAsync();
                AddMessage("🎯 关键词唤醒已启用");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "切换关键词检测状态失败");
            AddMessage($"切换关键词检测失败: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task CopyVerificationCodeAsync()
    {
        if (_verificationService == null || string.IsNullOrEmpty(VerificationCode))
        {
            _logger?.LogWarning("验证码服务未设置或验证码为空");
            return;
        }

        try
        {
            await _verificationService.CopyToClipboardAsync(VerificationCode);
            AddMessage($"✅ 验证码 {VerificationCode} 已复制到剪贴板");
            _logger?.LogInformation("验证码已复制到剪贴板: {Code}", VerificationCode);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "复制验证码失败");
            AddMessage("❌ 复制验证码失败", true);
        }
    }

    [RelayCommand]
    private async Task OpenLoginPageAsync()
    {
        if (_verificationService == null)
        {
            _logger?.LogWarning("验证码服务未设置");
            return;
        }

        try
        {
            await _verificationService.OpenBrowserAsync("https://xiaozhi.me/login");
            AddMessage("🌐 已打开登录页面");
            _logger?.LogInformation("已打开登录页面");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "打开登录页面失败");
            AddMessage("❌ 打开登录页面失败", true);
        }
    }

    [RelayCommand]
    private void DismissVerificationCode()
    {
        IsVerificationCodeVisible = false;
        VerificationCode = string.Empty;
        VerificationCodeMessage = string.Empty;
        _logger?.LogInformation("验证码提示已关闭");
    }

    #endregion

    #region 辅助方法    
    private void UpdateConnectionState(bool connected)
    {
        IsConnected = connected;
        ConnectionStatusText = connected ? "在线" : "离线";
    }

    /// <summary>
    /// 启动关键词检测（对应py-xiaozhi的wake_word_detector启动）
    /// </summary>
    private async Task StartKeywordDetectionAsync()
    {
        if (_voiceChatService == null)
        {
            _logger?.LogWarning("VoiceChatService未设置，无法启动关键词检测");
            return;
        }

        try
        {
            var success = await _voiceChatService.StartKeywordDetectionAsync();
            if (success)
            {
                AddMessage("🎯 关键词唤醒功能已启用");
                _logger?.LogInformation("关键词检测启动成功");
            }
            else
            {
                AddMessage("⚠️ 关键词唤醒功能启用失败", true);
                _logger?.LogWarning("关键词检测启动失败");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "启动关键词检测时发生错误");
            AddMessage($"关键词唤醒启动错误: {ex.Message}", true);
        }
    }    /// <summary>
    /// 停止关键词检测
    /// </summary>
    private async Task StopKeywordDetectionAsync()
    {
        if (_voiceChatService == null) return;

        try
        {
            await _voiceChatService.StopKeywordDetectionAsync();
            _logger?.LogInformation("关键词检测已停止");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止关键词检测时发生错误");
        }
    }

    private void UpdateModeUI(bool isAutoMode)
    {
        IsAutoMode = isAutoMode;
        ModeToggleText = isAutoMode ? "自动" : "手动";

        if (isAutoMode)
        {
            // 进入自动模式时，设置Auto按钮文本
            if (_voiceChatService != null && _voiceChatService.KeepListening == true && IsListening)
            {
                AutoButtonText = "停止对话";
            }
            else
            {
                AutoButtonText = "开始对话";
            }
        }
        else
        {
            // 进入手动模式时，重置Manual按钮文本
            ManualButtonText = "按住说话";
        }
    }

    private void AddMessage(string message, bool isError = false)
    {
        // 如果已经有默认消息，清除它
        if (Messages.Count > 0 && Messages[0].Content.Contains("等待对话开始"))
        {
            Messages.Clear();
        }

        Messages.Add(new ChatMessageViewModel
        {
            Content = message,
            IsError = isError,
            Timestamp = DateTime.Now
        });

        // 通知View滚动到底部
        ScrollToBottomRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateVolumeText(double value)
    {
        VolumeText = $"{(int)value}%";
    }    
    
    private void SetEmotion(string emotionName)
    {
        try
        {
            if (_emotionManager != null)
            {
                var emoji = _emotionManager.GetEmotionEmoji(emotionName);
                DefaultEmotionText = emoji;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to set emotion: {EmotionName}", emotionName);
        }
    }

    /// <summary>
    /// 更新表情显示，优先使用GIF动画，类似py-xiaozhi的表情切换
    /// </summary>
    private async Task UpdateEmotionDisplayAsync(string emotionName)
    {
        try
        {
            if (_emotionManager != null)
            {
                // 首先尝试获取GIF动画路径
                var gifPath = await _emotionManager.GetEmotionImageAsync(emotionName);
                
                if (!string.IsNullOrEmpty(gifPath))
                {
                    // 有GIF动画可用，通知View切换到动画显示
                    EmotionGifPathChanged?.Invoke(this, new EmotionGifPathEventArgs 
                    { 
                        GifPath = gifPath,
                        EmotionName = emotionName
                    });
                    
                    _logger?.LogDebug($"Updated emotion to GIF: {emotionName} -> {gifPath}");
                }
                else
                {
                    // 没有GIF动画，使用表情符号作为后备
                    var emoji = _emotionManager.GetEmotionEmoji(emotionName);
                    CurrentEmotion = emoji;
                    DefaultEmotionText = emoji;
                    
                    // 通知View切换回文本显示
                    EmotionGifPathChanged?.Invoke(this, new EmotionGifPathEventArgs 
                    { 
                        GifPath = null,
                        EmotionName = emotionName
                    });
                    
                    _logger?.LogDebug($"Updated emotion to emoji: {emotionName} -> {emoji}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update emotion display: {EmotionName}", emotionName);
            
            // 出错时回退到简单表情符号
            CurrentEmotion = ConvertEmotionToEmoji(emotionName);
            DefaultEmotionText = CurrentEmotion;
        }
    }

    private void RestoreManualButtonState()
    {
        ManualButtonText = "按住说话";
        // 通知View恢复按钮状态
        ManualButtonStateChanged?.Invoke(this, new ManualButtonStateEventArgs
        {
            State = ManualButtonState.Normal
        });
    }

    private void SetManualButtonRecordingState()
    {
        ManualButtonText = "正在录音...";
        ManualButtonStateChanged?.Invoke(this, new ManualButtonStateEventArgs
        {
            State = ManualButtonState.Recording
        });
    }

    private void SetManualButtonProcessingState()
    {
        ManualButtonText = "处理中...";
        ManualButtonStateChanged?.Invoke(this, new ManualButtonStateEventArgs
        {
            State = ManualButtonState.Processing        
        });
    }

    /// <summary>
    /// 处理验证码信息（对应py-xiaozhi的_handle_verification_code功能）
    /// </summary>
    private async Task HandleVerificationCodeAsync(string text)
    {
        if (_verificationService == null)
        {
            _logger?.LogWarning("验证码服务未设置，无法处理验证码");
            return;
        }

        try
        {
            // 使用验证码服务提取验证码
            var code = await _verificationService.ExtractVerificationCodeAsync(text);
            if (!string.IsNullOrEmpty(code))
            {
                // 设置验证码相关属性
                VerificationCode = code;
                VerificationCodeMessage = $"您的验证码是: {code}";
                IsVerificationCodeVisible = true;

                // 自动复制到剪贴板
                try
                {
                    await _verificationService.CopyToClipboardAsync(code);
                    AddMessage($"🔑 验证码 {code} 已提取并复制到剪贴板");
                    _logger?.LogInformation("验证码已提取并复制到剪贴板: {Code}", code);
                }
                catch (Exception copyEx)
                {
                    _logger?.LogWarning(copyEx, "复制验证码到剪贴板失败");
                    AddMessage($"🔑 验证码 {code} 已提取，但复制到剪贴板失败");
                }

                // 尝试打开浏览器（可选）
                try
                {
                    await _verificationService.OpenBrowserAsync("https://xiaozhi.me/login");
                    AddMessage("🌐 已自动打开登录页面");
                    _logger?.LogInformation("已自动打开登录页面");
                }
                catch (Exception browserEx)
                {
                    _logger?.LogWarning(browserEx, "打开浏览器失败");
                    // 不显示错误消息，因为这是可选操作
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理验证码时发生错误");
            AddMessage("❌ 处理验证码时发生错误", true);
        }
    }    private void CleanupEventSubscriptions()
    {
        if (_voiceChatService != null)
        {
            _voiceChatService.MessageReceived -= OnMessageReceived;
            _voiceChatService.VoiceChatStateChanged -= OnVoiceChatStateChanged;
            _voiceChatService.ErrorOccurred -= OnErrorOccurred;
            _voiceChatService.DeviceStateChanged -= OnDeviceStateChanged;
            _voiceChatService.MusicMessageReceived -= OnMusicMessageReceived;
            _voiceChatService.SystemStatusMessageReceived -= OnSystemStatusMessageReceived;
            _voiceChatService.IotMessageReceived -= OnIotMessageReceived;
            _voiceChatService.LlmMessageReceived -= OnLlmMessageReceived;
            _voiceChatService.TtsStateChanged -= OnTtsStateChanged;
        }

        if (_interruptManager != null)
        {
            _interruptManager.InterruptTriggered -= OnInterruptTriggered;
        }
    }

    public async Task HandleInterruptAsync(AbortReason reason, string description)
    {
        if (_voiceChatService == null)
        {
            _logger?.LogWarning("VoiceChatService is null, cannot handle interrupt");
            return;
        }

        var currentState = _voiceChatService.CurrentState;
        _logger?.LogInformation("Handling interrupt {Reason} in state {State}", reason, currentState);

        AddMessage($"[打断] {description}", false);

        switch (reason)
        {
            case AbortReason.VoiceInterruption:
                if (currentState == DeviceState.Speaking)
                {
                    await _voiceChatService.StopVoiceChatAsync();
                    _logger?.LogInformation("Voice chat stopped due to voice interruption");

                    if (_voiceChatService.KeepListening)
                    {
                        await Task.Delay(200);
                        if (_voiceChatService.CurrentState == DeviceState.Idle)
                        {
                            await _voiceChatService.StartVoiceChatAsync();
                            _logger?.LogInformation("Auto-restarted listening after voice interrupt");
                        }
                    }
                }
                break;

            case AbortReason.KeyboardInterruption:
            case AbortReason.UserInterruption:
                switch (currentState)
                {
                    case DeviceState.Speaking:
                        await _voiceChatService.StopVoiceChatAsync();
                        _logger?.LogInformation("Voice chat stopped due to user interrupt");
                        break;
                    case DeviceState.Listening:
                        await _voiceChatService.StopVoiceChatAsync();
                        _logger?.LogInformation("Listening stopped due to user interrupt");
                        break;
                    case DeviceState.Idle:
                        if (IsAutoMode)
                        {
                            await _voiceChatService.ToggleChatStateAsync();
                            _logger?.LogInformation("Toggled chat state due to user interrupt in idle");
                        }
                        break;
                }
                break;

            case AbortReason.WakeWordDetected:
                switch (currentState)
                {
                    case DeviceState.Speaking:
                        await _voiceChatService.StopVoiceChatAsync();
                        await Task.Delay(100);
                        await _voiceChatService.StartVoiceChatAsync();
                        _logger?.LogInformation("Switched from speaking to listening due to wake word");
                        break;
                    case DeviceState.Idle:
                        await _voiceChatService.StartVoiceChatAsync();
                        _logger?.LogInformation("Started listening due to wake word");
                        break;
                }
                break;

            default:
                _logger?.LogWarning("Unhandled interrupt reason: {Reason}", reason);
                break;
        }
    }

    partial void OnVolumeValueChanged(double value)
    {
        UpdateVolumeText(value);
    }

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsManualButtonEnabled));
    }

    partial void OnIsPushToTalkActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsManualButtonEnabled));
    }

    partial void OnIsWaitingForResponseChanged(bool value)
    {
        OnPropertyChanged(nameof(IsManualButtonEnabled));
    }

    #endregion    
    
    #region 事件

    public event EventHandler<InterruptEventArgs>? InterruptTriggered;
    public event EventHandler? ScrollToBottomRequested;
    public event EventHandler<ManualButtonStateEventArgs>? ManualButtonStateChanged;
    public event EventHandler<EmotionGifPathEventArgs>? EmotionGifPathChanged;

    #endregion


    /// <summary>
    /// 格式化时间显示（参考py-xiaozhi的_format_time方法）
    /// </summary>
    private string FormatTime(double seconds)
    {
        var timeSpan = TimeSpan.FromSeconds(seconds);
        if (timeSpan.TotalHours >= 1)
        {
            return timeSpan.ToString(@"h\:mm\:ss");
        }
        else
        {
            return timeSpan.ToString(@"m\:ss");
        }
    }

    /// <summary>
    /// 将情感文本转换为对应的表情符号
    /// </summary>
    private string ConvertEmotionToEmoji(string emotion)
    {
        return emotion.ToLowerInvariant() switch
        {
            "happy" or "joy" => "😊",
            "sad" => "😢",
            "angry" => "😠",
            "surprise" => "😲",
            "fear" => "😨",
            "disgust" => "😒",
            "thinking" => "🤔",
            "neutral" => "😐",
            _ => "😊"
        };
    }

    public override void Cleanup()
    {
        CleanupEventSubscriptions();
        base.Cleanup();
    }
}

/// <summary>
/// 聊天消息ViewModel
/// </summary>
public class ChatMessageViewModel
{
    public string Content { get; set; } = string.Empty;
    public bool IsError { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 手动按钮状态
/// </summary>
public enum ManualButtonState
{
    Normal,
    Recording,
    Processing
}

/// <summary>
/// 手动按钮状态事件参数
/// </summary>
public class ManualButtonStateEventArgs : EventArgs
{
    public ManualButtonState State { get; set; }
}

/// <summary>
/// 表情GIF路径变化事件参数
/// </summary>
public class EmotionGifPathEventArgs : EventArgs
{
    public string? GifPath { get; set; }
    public string? EmotionName { get; set; }
}
