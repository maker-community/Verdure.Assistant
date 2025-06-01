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
{
    private readonly IVoiceChatService? _voiceChatService;
    private readonly IEmotionManager? _emotionManager;
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
    private string _volumeText = "80%";    [ObservableProperty]
    private string _currentMessage = string.Empty;

    [ObservableProperty]
    private bool _showMicrophoneVisualizer = false;

    [ObservableProperty]
    private string _serverUrl = "ws://localhost:8080/ws";

    // Manual按钮可用状态 - 基于连接状态、推送说话状态和等待响应状态
    public bool IsManualButtonEnabled => IsConnected && !IsPushToTalkActive && !IsWaitingForResponse;

    #endregion

    #region 集合

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();

    #endregion

    public HomePageViewModel(ILogger<HomePageViewModel> logger, 
        IVoiceChatService? voiceChatService = null,
        IEmotionManager? emotionManager = null,
        InterruptManager? interruptManager = null) : base(logger)
    {
        _voiceChatService = voiceChatService;
        _emotionManager = emotionManager;
        _interruptManager = interruptManager;

        // 设置初始状态
        InitializeDefaultState();
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
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        
        // 绑定服务事件
        await BindEventsAsync();
    }

    private async Task BindEventsAsync()
    {
        // 绑定语音服务事件
        if (_voiceChatService != null)
        {
            _voiceChatService.DeviceStateChanged += OnDeviceStateChanged;
            _voiceChatService.VoiceChatStateChanged += OnVoiceChatStateChanged;
            _voiceChatService.MessageReceived += OnMessageReceived;
            _voiceChatService.ErrorOccurred += OnErrorOccurred;
        }

        // 初始化和绑定InterruptManager事件
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
    }

    #region 事件处理

    private void OnDeviceStateChanged(object? sender, DeviceState state)
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
    }

    private void OnVoiceChatStateChanged(object? sender, bool isActive)
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
    }

    private void OnMessageReceived(object? sender, ChatMessage message)
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
        }
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        AddMessage($"错误: {error}", true);
        _logger?.LogError("Voice chat error: {Error}", error);
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
            }

            // 停止当前语音对话
            if (IsListening)
            {
                await _voiceChatService.StopVoiceChatAsync();
            }

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

    #endregion

    #region 辅助方法

    private void UpdateConnectionState(bool connected)
    {
        IsConnected = connected;
        ConnectionStatusText = connected ? "在线" : "离线";
    }    private void UpdateModeUI(bool isAutoMode)
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

    private void CleanupEventSubscriptions()
    {
        if (_voiceChatService != null)
        {
            _voiceChatService.MessageReceived -= OnMessageReceived;
            _voiceChatService.VoiceChatStateChanged -= OnVoiceChatStateChanged;
            _voiceChatService.ErrorOccurred -= OnErrorOccurred;
            _voiceChatService.DeviceStateChanged -= OnDeviceStateChanged;
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

    #endregion

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
