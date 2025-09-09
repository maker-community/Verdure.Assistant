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
    private readonly IEmotionPlaybackCoordinator? _emotionPlaybackCoordinator;
    private readonly IKeywordSpottingService? _keywordSpottingService;
    private readonly IMusicPlayerService? _musicPlayerService;
    private readonly IConfigurationService? _configurationService;

    private readonly VerdureConfig _config;

    // UI thread dispatcher for cross-platform thread marshaling
    private IUIDispatcher _uiDispatcher;

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

    // 连接按钮可用状态 - 未连接且未在连接/断开过程中
    public bool IsConnectButtonEnabled => !IsConnected && !_isConnecting && !_isDisconnecting;

    // 断开按钮可用状态 - 已连接且未在连接/断开过程中
    public bool IsDisconnectButtonEnabled => IsConnected && !_isConnecting && !_isDisconnecting;

    #endregion

    #region 集合

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();

    #endregion
    public HomePageViewModel(ILogger<HomePageViewModel> logger,
      IVoiceChatService? voiceChatService = null,
      IEmotionPlaybackCoordinator? emotionPlaybackCoordinator = null,
      IKeywordSpottingService? keywordSpottingService = null,
      IMusicPlayerService? musicPlayerService = null,
      IConfigurationService? configurationService = null,
      IUIDispatcher? uiDispatcher = null) : base(logger)
    {
        _voiceChatService = voiceChatService;
        _emotionPlaybackCoordinator = emotionPlaybackCoordinator;
        _keywordSpottingService = keywordSpottingService;
        _musicPlayerService = musicPlayerService;
        _configurationService = configurationService;

        // 设置初始状态
        InitializeDefaultState();
        _uiDispatcher = uiDispatcher ?? new DefaultUIDispatcher();

        _config = new VerdureConfig
        {
            ServerUrl = ServerUrl,
            UseWebSocket = true,
            EnableVoice = true,
            AudioSampleRate = 16000,
            AudioChannels = 1,
            AudioFormat = "opus",
            AutoConnect = true, // 设置自动连接标志
            KeywordModels = new KeywordModelConfig
            {
                // WinUI项目的模型文件在 Assets/keywords 目录
                ModelsPath = null, // 使用默认自动检测
                CurrentModel = "keyword_xiaodian.table"
            }
        };
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
        //SetEmotion("neutral");

        // 确保按钮状态正确初始化
        OnPropertyChanged(nameof(IsConnectButtonEnabled));
        OnPropertyChanged(nameof(IsDisconnectButtonEnabled));
        OnPropertyChanged(nameof(IsManualButtonEnabled));
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // 绑定服务事件
        await BindEventsAsync();
    }

    private async Task BindEventsAsync()
    {
        if (_configurationService != null)
        {
            _configurationService.VerificationCodeReceived += OnConfigurationVerificationCodeReceived;
            _logger?.LogInformation("配置服务验证码事件已绑定");
        }
        
        // 绑定语音服务事件 - 优化：统一使用状态机事件，避免重复订阅
        if (_voiceChatService != null)
        {
            // 主要状态管理：仅订阅状态机事件，统一处理所有状态变化
            if (_voiceChatService.StateMachine != null)
            {
                _voiceChatService.StateMachine.StateChanged += OnStateMachineStateChanged;
                _logger?.LogInformation("已订阅状态机状态变化事件，统一状态管理");
            }

            // 数据和消息事件：仅订阅必要的业务逻辑事件
            _voiceChatService.MessageReceived += OnMessageReceived;
            _voiceChatService.ErrorOccurred += OnErrorOccurred;
            _voiceChatService.MusicMessageReceived += OnMusicMessageReceived;
            _voiceChatService.SystemStatusMessageReceived += OnSystemStatusMessageReceived;
            _voiceChatService.LlmMessageReceived += OnLlmMessageReceived;
            _voiceChatService.TtsStateChanged += OnTtsStateChanged;

            // 移除重复的VoiceChatStateChanged订阅，状态变化由StateMachine统一处理

            await _voiceChatService.InitializeAsync(_config);
        }
        
        // 绑定音乐播放服务事件
        if (_musicPlayerService != null)
        {
            _musicPlayerService.PlaybackStateChanged += OnMusicPlaybackStateChanged;
            _musicPlayerService.LyricUpdated += OnMusicLyricUpdated;
            _musicPlayerService.ProgressUpdated += OnMusicProgressUpdated;
            _logger?.LogInformation("音乐播放服务事件已绑定");
        }
    }

    #region 事件处理

    /// <summary>
    /// 处理配置服务的验证码接收事件
    /// </summary>
    private void OnConfigurationVerificationCodeReceived(object? sender, string verificationCode)
    {
        // 使用UI调度器确保线程安全的事件处理
        _ = _uiDispatcher.InvokeAsync(async () =>
        {
            try
            {
                _logger?.LogInformation("从配置服务接收到验证码事件: {Code}", verificationCode);
                await HandleVerificationCodeFromConfigurationAsync(verificationCode);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "处理配置服务验证码事件时发生错误");
            }
        });
    }

    /// <summary>
    /// 直接处理状态机状态变化事件 - 简化状态管理架构
    /// </summary>
    private void OnStateMachineStateChanged(object? sender, StateTransitionEventArgs e)
    {
        // 使用UI调度器确保线程安全的事件处理
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            _logger?.LogDebug("State machine transition: {FromState} -> {ToState} (Trigger: {Trigger})",
                e.FromState, e.ToState, e.Trigger);

            var state = e.ToState;

            switch (state)
            {
                case DeviceState.Listening:
                    IsConnected = true; // 确保连接状态正确
                    IsListening = true; // 同步监听状态
                    if (IsConnected) // 确保只在连接状态下更新
                    {
                        StatusText = "正在聆听";
                        SetEmotion("listening");
                        ShowMicrophoneVisualizer = true;

                        // 更新自动模式按钮文本
                        if (IsAutoMode && _voiceChatService?.KeepListening == true)
                        {
                            AutoButtonText = "停止对话";
                        }

                        // 确保手动模式按钮状态正确
                        if (!IsAutoMode && IsPushToTalkActive)
                        {
                            SetManualButtonRecordingState();
                        }
                    }
                    break;
                case DeviceState.Speaking:
                    IsListening = false; // 说话时不监听
                    if (IsConnected)
                    {
                        StatusText = "正在播放";
                        SetEmotion("speaking");
                        ShowMicrophoneVisualizer = false;

                        // 如果是手动模式且在等待响应，更新按钮状态
                        if (!IsAutoMode && IsWaitingForResponse)
                        {
                            SetManualButtonProcessingState();
                        }
                    }
                    break;
                case DeviceState.Connecting:
                    StatusText = "连接中";
                    SetEmotion("thinking");
                    ShowMicrophoneVisualizer = false;
                    IsListening = false;
                    break;
                case DeviceState.Idle:
                default:
                    IsListening = false; // 空闲时不监听
                    if (IsConnected)
                    {
                        StatusText = "待命";
                        SetEmotion("neutral");
                        ShowMicrophoneVisualizer = false;

                        // 更新自动模式按钮文本
                        if (IsAutoMode)
                        {
                            AutoButtonText = "开始对话";
                        }

                        // Reset push-to-talk state when AI response completes
                        if (IsWaitingForResponse)
                        {
                            IsWaitingForResponse = false;
                            IsPushToTalkActive = false;
                            RestoreManualButtonState();
                            AddMessage("✅ AI 回复完成，可以继续对话");
                            _logger?.LogInformation("AI response completed, manual button restored");
                        }

                        // 确保手动模式按钮状态正确
                        if (!IsAutoMode && IsPushToTalkActive)
                        {
                            IsPushToTalkActive = false;
                            RestoreManualButtonState();
                        }
                    }
                    else
                    {
                        StatusText = "未连接";
                        SetEmotion("neutral");
                        ShowMicrophoneVisualizer = false;

                        // 在未连接状态下，确保所有相关状态都被正确重置
                        if (IsListening || IsWaitingForResponse || IsPushToTalkActive)
                        {
                            _logger?.LogWarning("Device state is Idle but disconnected, forcing state reset. Listening: {IsListening}, Waiting: {IsWaitingForResponse}, PushToTalk: {IsPushToTalkActive}",
                                IsListening, IsWaitingForResponse, IsPushToTalkActive);

                            IsListening = false;
                            IsWaitingForResponse = false;
                            IsPushToTalkActive = false;
                            ShowMicrophoneVisualizer = false;
                            RestoreManualButtonState();
                        }
                    }
                    break;
            }

            // 更新UI可用状态
            OnPropertyChanged(nameof(IsManualButtonEnabled));

            // 验证状态一致性
            if (!IsConnected && (state == DeviceState.Listening || state == DeviceState.Speaking))
            {
                _logger?.LogWarning("Inconsistent state detected: Device state is {DeviceState} but IsConnected is false", state);
            }
        });
    }

    private void OnMessageReceived(object? sender, ChatMessage message)
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

    private void OnInterruptOccurred(object? sender, Core.Services.Interrupt.InterruptEventArgs e)
    {
        try
        {
            _logger?.LogInformation("Interrupt occurred: {SourceName} - {Description}", e.SourceName, e.Description);

            // 在UI线程中处理中断需要通过事件通知View - 转换为旧的事件格式以保持兼容性
            var oldEventArgs = new Core.Services.InterruptEventArgs(
                Enum.TryParse<AbortReason>(e.InterruptType, out var reason) ? reason : AbortReason.UserInterruption,
                e.Description);
            InterruptTriggered?.Invoke(this, oldEventArgs);
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

    // 音乐播放服务事件处理
    private void OnMusicPlaybackStateChanged(object? sender, MusicPlaybackEventArgs e)
    {
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            if (e.Track != null)
            {
                CurrentSongName = e.Track.Name;
                CurrentArtist = e.Track.Artist;
                MusicDuration = e.Track.Duration;
            }
            MusicStatus = e.Status switch
            {
                "Playing" => "播放中",
                "Paused" => "暂停",
                "Stopped" => "停止",
                "Ended" => "播放完毕",
                "Failed" => "播放失败",
                _ => "未知状态"
            };

            var stateEmoji = e.Status switch
            {
                "Playing" => "🎵",
                "Paused" => "⏸️",
                "Stopped" => "⏹️",
                "Ended" => "🔚",
                "Failed" => "❌",
                _ => "🎶"
            };

            if (e.Track != null)
            {
                AddMessage($"{stateEmoji} {MusicStatus}: {e.Track.DisplayName}", e.Status == "Failed");
            }
            else
            {
                AddMessage($"{stateEmoji} 音乐播放状态: {MusicStatus}", e.Status == "Failed");
            }

            if (e.Status == "Failed" && !string.IsNullOrEmpty(e.Message))
            {
                AddMessage($"错误详情: {e.Message}", true);
            }
        });
    }
    private void OnMusicLyricUpdated(object? sender, LyricUpdateEventArgs e)
    {
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            if (!string.IsNullOrEmpty(e.LyricText))
            {
                var timeStr = FormatTime(e.Position);
                CurrentLyric = $"[{timeStr}] {e.LyricText}";

                // 只有当前歌词有意义时才显示在消息中
                if (!string.IsNullOrWhiteSpace(e.LyricText))
                {
                    AddMessage($"🎤 {CurrentLyric}", false);
                }
            }
        });
    }
    private void OnMusicProgressUpdated(object? sender, ProgressUpdateEventArgs e)
    {
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            MusicPosition = e.Position;
            // 注意：不在这里添加消息，避免UI过度刷新
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

    private volatile bool _isConnecting = false;
    private volatile bool _isDisconnecting = false;

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsConnected || _voiceChatService == null || _isConnecting || _isDisconnecting)
        {
            _logger?.LogWarning("Connect request ignored: Connected={Connected}, Service={ServiceNull}, Connecting={Connecting}, Disconnecting={Disconnecting}",
                IsConnected, _voiceChatService == null, _isConnecting, _isDisconnecting);
            return;
        }

        _isConnecting = true;

        // 立即更新按钮状态，禁用连接按钮
        OnPropertyChanged(nameof(IsConnectButtonEnabled));
        OnPropertyChanged(nameof(IsDisconnectButtonEnabled));

        try
        {
            StatusText = "连接中";
            ConnectionStatusText = "连接中";

            // 在连接前清理之前的状态
            CleanupEventSubscriptions();

            _logger?.LogInformation("Starting connection to voice chat service with URL: {ServerUrl}", ServerUrl);

            await _voiceChatService.InitializeAsync(_config);

            // 重新绑定事件（因为服务可能被重新初始化）
            await BindEventsAsync();

            // Music player service is now injected via constructor dependency injection
            // No need to call SetMusicPlayerService anymore
            if (_musicPlayerService != null)
            {
                _logger?.LogInformation("Music player service available via constructor injection for VAD control");
            }

            // Use the service's IsConnected property to determine actual connection state
            bool isConnected = _voiceChatService.IsConnected;
            UpdateConnectionState(isConnected);

            if (isConnected)
            {
                AddMessage("✅ 连接成功");
                _logger?.LogInformation("Successfully connected to voice chat service");

                // 验证连接后的状态一致性
                var deviceState = _voiceChatService.CurrentState;
                _logger?.LogInformation("Post-connection verification - Device State: {DeviceState}, IsConnected: {IsConnected}", deviceState, IsConnected);

                // 启动关键词检测（对应py-xiaozhi的关键词唤醒功能）
                //await StartKeywordDetectionAsync();

                // 连接成功后验证状态机状态
                if (deviceState != DeviceState.Idle)
                {
                    _logger?.LogWarning("Device state is not Idle after connection: {DeviceState}", deviceState);
                }

                // 确保UI状态正确更新
                OnPropertyChanged(nameof(IsManualButtonEnabled));

                _logger?.LogInformation("Connection completed successfully, UI states updated");
            }
            else
            {
                AddMessage("❌ 连接失败: 服务未连接", true);
                StatusText = "连接失败";
                ConnectionStatusText = "离线";
                _logger?.LogWarning("Connection failed: Service not connected");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to voice chat service");
            AddMessage($"❌ 连接失败: {ex.Message}", true);
            UpdateConnectionState(false);
        }
        finally
        {
            _isConnecting = false;

            // 更新按钮状态
            OnPropertyChanged(nameof(IsConnectButtonEnabled));
            OnPropertyChanged(nameof(IsDisconnectButtonEnabled));
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!IsConnected || _voiceChatService == null || _isDisconnecting || _isConnecting)
        {
            _logger?.LogWarning("Disconnect request ignored: Connected={Connected}, Connecting={Connecting}, Disconnecting={Disconnecting}",
                IsConnected, _isConnecting, _isDisconnecting);
            return;
        }

        _isDisconnecting = true;

        // 立即更新按钮状态，禁用断开按钮
        OnPropertyChanged(nameof(IsConnectButtonEnabled));
        OnPropertyChanged(nameof(IsDisconnectButtonEnabled));

        try
        {
            StatusText = "断开连接中";
            ConnectionStatusText = "断开中";
            _logger?.LogInformation("Starting disconnection process");

            // 记录当前设备状态以便调试
            var currentDeviceState = _voiceChatService.CurrentState;
            _logger?.LogInformation("Disconnecting from device state: {CurrentState}", currentDeviceState);

            // 停止当前语音对话 - 与状态机协调
            if (IsListening || _voiceChatService.IsVoiceChatActive)
            {
                _logger?.LogInformation("Stopping active voice chat before disconnect");
                await _voiceChatService.StopVoiceChatAsync();

                // 等待状态机转换完成
                await Task.Delay(100);
                _logger?.LogInformation("Voice chat stopped, current state: {CurrentState}", _voiceChatService.CurrentState);
            }

            // 如果在自动模式，先停止自动模式
            if (IsAutoMode)
            {
                IsAutoMode = false;
                _voiceChatService.KeepListening = false;
                _logger?.LogInformation("Auto mode disabled before disconnect");
            }

            // 停止关键词检测
            await StopKeywordDetectionAsync();

            // 清理事件订阅
            CleanupEventSubscriptions();

            // 释放语音聊天服务资源
            _voiceChatService.Dispose();

            // 重置所有状态 - 确保与状态机逻辑同步
            UpdateConnectionState(false);

            // 验证状态重置是否正确
            _logger?.LogInformation("Connection state updated - IsConnected: {IsConnected}, IsListening: {IsListening}, IsAutoMode: {IsAutoMode}",
                IsConnected, IsListening, IsAutoMode);

            AddMessage("🔌 已断开连接，系统已重置为等待连接状态");
            _logger?.LogInformation("Successfully disconnected from voice chat service");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to disconnect from voice chat service");
            AddMessage($"断开连接失败: {ex.Message}", true);
            // 即使出错也要重置状态，确保界面一致性
            UpdateConnectionState(false);
        }
        finally
        {
            _isDisconnecting = false;

            // 更新按钮状态，确保连接按钮重新可用
            OnPropertyChanged(nameof(IsConnectButtonEnabled));
            OnPropertyChanged(nameof(IsDisconnectButtonEnabled));
        }
    }

    [RelayCommand]
    private async Task StartManualRecordingAsync()
    {
        if (_voiceChatService == null || !IsConnected || IsPushToTalkActive || IsWaitingForResponse)
        {
            _logger?.LogWarning("Cannot start manual recording: Service={ServiceNull}, Connected={Connected}, PushToTalk={PushToTalk}, Waiting={Waiting}",
                _voiceChatService == null, IsConnected, IsPushToTalkActive, IsWaitingForResponse);
            return;
        }

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
        {
            _logger?.LogWarning("Cannot stop manual recording: Service={ServiceNull}, Connected={Connected}, PushToTalk={PushToTalk}",
                _voiceChatService == null, IsConnected, IsPushToTalkActive);
            return;
        }

        try
        {
            if (IsListening)
            {
                await _voiceChatService.StopVoiceChatAsync();
                IsPushToTalkActive = false;
                IsWaitingForResponse = true;
                SetManualButtonProcessingState();
                AddMessage("录音结束，正在处理和等待回复...");
                _logger?.LogInformation("Push-to-talk stopped, waiting for AI response");
            }
            else
            {
                // 如果没有在听，直接重置状态
                IsPushToTalkActive = false;
                RestoreManualButtonState();
                _logger?.LogInformation("Push-to-talk stopped, but wasn't listening");
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

                // 重置相关UI状态
                if (IsPushToTalkActive || IsWaitingForResponse)
                {
                    IsPushToTalkActive = false;
                    IsWaitingForResponse = false;
                    RestoreManualButtonState();
                }

                // 更新UI可用状态
                OnPropertyChanged(nameof(IsManualButtonEnabled));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to abort current operation");
            AddMessage($"中断操作失败: {ex.Message}", true);
        }
    }

    /// <summary>
    /// 重新连接命令 - 断开当前连接并重新连接
    /// </summary>
    [RelayCommand]
    private async Task ReconnectAsync()
    {
        if (_isConnecting || _isDisconnecting)
        {
            _logger?.LogWarning("Reconnect ignored: already in transition state");
            return;
        }

        try
        {
            AddMessage("🔄 开始重新连接...", false);

            // 如果当前已连接，先断开
            if (IsConnected)
            {
                await DisconnectCommand.ExecuteAsync(null);

                // 等待断开完成
                await Task.Delay(1000);

                // 确保按钮状态正确更新
                OnPropertyChanged(nameof(IsConnectButtonEnabled));
                OnPropertyChanged(nameof(IsDisconnectButtonEnabled));
            }

            // 重新连接
            await ConnectCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to reconnect");
            AddMessage($"❌ 重新连接失败: {ex.Message}", true);

            // 确保按钮状态正确
            OnPropertyChanged(nameof(IsConnectButtonEnabled));
            OnPropertyChanged(nameof(IsDisconnectButtonEnabled));
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
        {
            if (_voiceChatService.IsKeywordDetectionEnabled)
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
        if (_configurationService == null || string.IsNullOrEmpty(VerificationCode))
        {
            _logger?.LogWarning("配置服务未设置或验证码为空");
            return;
        }

        try
        {
            await _configurationService.CopyToClipboardAsync(VerificationCode);
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
        if (_configurationService == null)
        {
            _logger?.LogWarning("配置服务未设置");
            return;
        }

        try
        {
            await _configurationService.OpenBrowserAsync("https://xiaozhi.me/login");
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

    // 音乐控制命令
    [RelayCommand]
    private async Task PlayMusicAsync(string? query = null)
    {
        if (_musicPlayerService == null)
        {
            AddMessage("❌ 音乐播放服务未可用", true);
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                // 如果没有搜索词，尝试切换播放/暂停状态
                await _musicPlayerService.TogglePlayPauseAsync();
                AddMessage("▶️ 切换播放状态");
            }
            else
            {
                // 搜索并播放指定音乐
                AddMessage($"🔍 搜索音乐: {query}");
                await _musicPlayerService.SearchAndPlayAsync(query);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "播放音乐失败: {Query}", query);
            AddMessage($"❌ 播放失败: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task PauseMusicAsync()
    {
        if (_musicPlayerService == null)
        {
            AddMessage("❌ 音乐播放服务未可用", true);
            return;
        }

        try
        {
            await _musicPlayerService.TogglePlayPauseAsync();
            AddMessage("⏸️ 切换播放状态");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "暂停音乐失败");
            AddMessage($"❌ 暂停失败: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task ResumeMusicAsync()
    {
        if (_musicPlayerService == null)
        {
            AddMessage("❌ 音乐播放服务未可用", true);
            return;
        }

        try
        {
            await _musicPlayerService.TogglePlayPauseAsync();
            AddMessage("▶️ 切换播放状态");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "恢复音乐失败");
            AddMessage($"❌ 恢复失败: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task StopMusicAsync()
    {
        if (_musicPlayerService == null)
        {
            AddMessage("❌ 音乐播放服务未可用", true);
            return;
        }

        try
        {
            await _musicPlayerService.StopAsync();
            AddMessage("⏹️ 音乐已停止");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止音乐失败");
            AddMessage($"❌ 停止失败: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task SeekMusicAsync(double position)
    {
        if (_musicPlayerService == null)
        {
            AddMessage("❌ 音乐播放服务未可用", true);
            return;
        }
        try
        {
            await _musicPlayerService.SeekAsync(position);

            var timeStr = FormatTime(position);
            AddMessage($"⏭️ 跳转到: {timeStr}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "音乐跳转失败");
            AddMessage($"❌ 跳转失败: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task SetMusicVolumeAsync(double volume)
    {
        if (_musicPlayerService == null)
        {
            AddMessage("❌ 音乐播放服务未可用", true);
            return;
        }

        try
        {
            // 确保音量在0-100范围内
            volume = Math.Max(0, Math.Min(100, volume));
            await _musicPlayerService.SetVolumeAsync(volume);
            AddMessage($"🔊 音量已设置为: {volume:F0}%");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "设置音量失败");
            AddMessage($"❌ 音量设置失败: {ex.Message}", true);
        }
    }

    #endregion

    #region 辅助方法    
    private void UpdateConnectionState(bool connected)
    {
        var previousState = IsConnected;
        IsConnected = connected;
        ConnectionStatusText = connected ? "在线" : "离线";
        StatusText = connected ? "已连接" : "未连接";

        _logger?.LogInformation("Connection state transition: {PreviousState} -> {CurrentState}", previousState, connected);

        // 确保在断开连接时重置所有相关状态，与状态机逻辑保持同步
        if (!connected)
        {
            // 重置所有语音相关状态
            var wasListening = IsListening;
            var wasInAutoMode = IsAutoMode;
            var wasWaitingForResponse = IsWaitingForResponse;
            var wasPushToTalkActive = IsPushToTalkActive;

            IsListening = false;
            IsPushToTalkActive = false;
            IsWaitingForResponse = false;
            IsAutoMode = false;
            ShowMicrophoneVisualizer = false;

            // 重置按钮状态 - 确保UI能重新使用
            RestoreManualButtonState();
            AutoButtonText = "开始对话";
            ModeToggleText = "手动";
            ManualButtonText = "按住说话";

            // 重置音乐和媒体状态
            CurrentSongName = string.Empty;
            CurrentArtist = string.Empty;
            CurrentLyric = string.Empty;
            MusicStatus = "停止";
            MusicPosition = 0.0;
            MusicDuration = 0.0;

            // 重置情感和系统状态
            SetEmotion("neutral");
            TtsText = "待命";
            SystemStatusText = string.Empty;
            IotStatusText = string.Empty;

            // 重置验证码相关状态
            IsVerificationCodeVisible = false;
            VerificationCode = string.Empty;
            VerificationCodeMessage = string.Empty;

            // 记录状态重置详情
            if (wasListening || wasInAutoMode || wasWaitingForResponse || wasPushToTalkActive)
            {
                _logger?.LogInformation("Reset UI states on disconnect - Listening: {WasListening} -> false, AutoMode: {WasAutoMode} -> false, WaitingForResponse: {WasWaitingForResponse} -> false, PushToTalk: {WasPushToTalkActive} -> false",
                    wasListening, wasInAutoMode, wasWaitingForResponse, wasPushToTalkActive);
            }

            // 确保服务端状态也被重置
            if (_voiceChatService != null)
            {
                try
                {
                    _voiceChatService.KeepListening = false;
                    _logger?.LogDebug("VoiceChatService KeepListening reset to false");
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to reset VoiceChatService KeepListening state");
                }
            }

            // 更新所有按钮状态，确保连接按钮重新可用，断开按钮不可用
            OnPropertyChanged(nameof(IsManualButtonEnabled));
            OnPropertyChanged(nameof(IsConnectButtonEnabled));
            OnPropertyChanged(nameof(IsDisconnectButtonEnabled));

            // 添加断开连接后的状态说明消息
            AddMessage("🔌 连接已断开，所有状态已重置，可以重新连接", false);
        }
        else
        {
            _logger?.LogInformation("Connected to voice chat service successfully");

            // 连接成功后，更新所有按钮状态：连接按钮不可用，断开按钮可用，手动按钮可用
            OnPropertyChanged(nameof(IsManualButtonEnabled));
            OnPropertyChanged(nameof(IsConnectButtonEnabled));
            OnPropertyChanged(nameof(IsDisconnectButtonEnabled));

            // 添加连接成功的状态说明
            AddMessage("✅ 连接成功，语音助手已就绪", false);
        }
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
            // 使用新的播放协调器设置情感（如果可用）
            if (_emotionPlaybackCoordinator != null)
            {
                // 异步调用，但不等待以避免阻塞UI
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _emotionPlaybackCoordinator.PlayEmotionAsync(emotionName);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to play emotion asynchronously: {EmotionName}", emotionName);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to set emotion: {EmotionName}", emotionName);
        }
    }

    /// <summary>
    /// 更新表情显示，使用新的播放协调器
    /// </summary>
    public async Task UpdateEmotionDisplayAsync(string emotionName)
    {
        try
        {
            // 使用新的播放协调器
            if (_emotionPlaybackCoordinator != null)
            {
                await _emotionPlaybackCoordinator.PlayEmotionAsync(emotionName);
                _logger?.LogDebug($"Updated emotion using coordinator: {emotionName}");
            }
            else
            {
                _logger?.LogWarning("No emotion playback coordinator available");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update emotion display: {EmotionName}", emotionName);
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
    /// 处理从配置服务接收到的验证码事件
    /// </summary>
    private async Task HandleVerificationCodeFromConfigurationAsync(string verificationCode)
    {
        if (_configurationService == null)
        {
            _logger?.LogWarning("配置服务未设置，无法处理验证码");
            return;
        }

        try
        {
            // 直接使用配置服务提供的验证码
            VerificationCode = verificationCode;
            VerificationCodeMessage = $"您的验证码是: {verificationCode}。已从配置服务自动获取。";
            IsVerificationCodeVisible = true;

            // 自动复制到剪贴板
            try
            {
                await _configurationService.CopyToClipboardAsync(verificationCode);
                AddMessage($"🔑 验证码 {verificationCode} 已通过配置服务自动获取并复制到剪贴板");
                _logger?.LogInformation("验证码已通过配置服务获取并复制到剪贴板: {Code}", verificationCode);
            }
            catch (Exception copyEx)
            {
                _logger?.LogWarning(copyEx, "复制验证码到剪贴板失败");
                AddMessage($"🔑 验证码 {verificationCode} 已获取，但复制到剪贴板失败");
            }

            // 尝试打开浏览器（可选）
            try
            {
                await _configurationService.OpenBrowserAsync("https://xiaozhi.me/login");
                AddMessage("🌐 已自动打开登录页面");
                _logger?.LogInformation("已自动打开登录页面");
            }
            catch (Exception browserEx)
            {
                _logger?.LogWarning(browserEx, "打开浏览器失败");
                // 不显示错误消息，因为这是可选操作
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理配置服务验证码时发生错误: {Code}", verificationCode);
            AddMessage($"❌ 处理验证码时发生错误: {ex.Message}");
        }
    }

    private void CleanupEventSubscriptions()
    {
        if (_voiceChatService != null)
        {
            // 清理状态机事件订阅
            if (_voiceChatService.StateMachine != null)
            {
                _voiceChatService.StateMachine.StateChanged -= OnStateMachineStateChanged;
                _logger?.LogInformation("状态机事件订阅已清理");
            }

            // 清理服务层事件订阅（移除了重复的VoiceChatStateChanged）
            _voiceChatService.MessageReceived -= OnMessageReceived;
            _voiceChatService.ErrorOccurred -= OnErrorOccurred;
            _voiceChatService.MusicMessageReceived -= OnMusicMessageReceived;
            _voiceChatService.SystemStatusMessageReceived -= OnSystemStatusMessageReceived;
            _voiceChatService.LlmMessageReceived -= OnLlmMessageReceived;
            _voiceChatService.TtsStateChanged -= OnTtsStateChanged;

            _logger?.LogInformation("Voice chat service event subscriptions cleaned up");
        }

        if (_musicPlayerService != null)
        {
            _musicPlayerService.PlaybackStateChanged -= OnMusicPlaybackStateChanged;
            _musicPlayerService.LyricUpdated -= OnMusicLyricUpdated;
            _musicPlayerService.ProgressUpdated -= OnMusicProgressUpdated;
            _logger?.LogInformation("Music player service event subscriptions cleaned up");
        }

        if (_configurationService != null)
        {
            _configurationService.VerificationCodeReceived -= OnConfigurationVerificationCodeReceived;
            _logger?.LogInformation("Configuration service event subscriptions cleaned up");
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

    /// <summary>
    /// 切换关键词模型
    /// </summary>
    /// <param name="modelFileName">模型文件名</param>
    /// <returns>切换是否成功</returns>
    public async Task<bool> SwitchKeywordModelAsync(string modelFileName)
    {
        if (_voiceChatService == null)
        {
            _logger?.LogWarning("VoiceChatService is null, cannot switch keyword model");
            return false;
        }

        _logger?.LogInformation("Switching keyword model to: {ModelFileName}", modelFileName);

        var result = await _voiceChatService.SwitchKeywordModelAsync(modelFileName);

        if (result)
        {
            // 更新配置中的当前模型
            _config.KeywordModels.CurrentModel = modelFileName;
            AddMessage($"[系统] 已切换关键词模型为: {modelFileName}", false);
            _logger?.LogInformation("Keyword model switched successfully");
        }
        else
        {
            AddMessage($"[系统] 切换关键词模型失败: {modelFileName}", false);
            _logger?.LogError("Failed to switch keyword model");
        }

        return result;
    }

    /// <summary>
    /// 获取可用的关键词模型列表
    /// </summary>
    /// <returns>模型文件名列表</returns>
    public string[] GetAvailableKeywordModels()
    {
        return _config.KeywordModels.AvailableModels;
    }

    /// <summary>
    /// 获取当前使用的关键词模型
    /// </summary>
    /// <returns>当前模型文件名</returns>
    public string GetCurrentKeywordModel()
    {
        return _config.KeywordModels.CurrentModel;
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

    public event EventHandler<Core.Services.InterruptEventArgs>? InterruptTriggered;
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
