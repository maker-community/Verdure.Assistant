using Microsoft.Extensions.Logging;
using System.Text.Json;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services.MCP;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// 语音聊天服务实现
/// </summary>
public class VoiceChatService : IVoiceChatService
{
    private readonly ILogger<VoiceChatService>? _logger;
    private readonly IConfigurationService _configurationService;
    private readonly AudioStreamManager _audioStreamManager;
    private ICommunicationClient? _communicationClient;
    private IAudioRecorder? _audioRecorder;
    private IAudioPlayer? _audioPlayer;
    private IAudioCodec? _audioCodec;
    private VerdureConfig? _config;
    private bool _isVoiceChatActive;
    private string _sessionId = Guid.NewGuid().ToString();

    // Device state management
    private DeviceState _currentState = DeviceState.Idle;
    private ListeningMode _listeningMode = ListeningMode.Manual;
    private bool _keepListening = false;
    private AbortReason _lastAbortReason = AbortReason.None;
    
    // State machine for conversation logic
    private ConversationStateMachine? _stateMachine;
    private ConversationStateMachineContext? _stateMachineContext;
    // Wake word detector coordination (matches py-xiaozhi behavior)
    private InterruptManager? _interruptManager;
    // Keyword spotting service (Microsoft Cognitive Services based)
    private IKeywordSpottingService? _keywordSpottingService;
    private bool _keywordDetectionEnabled = false;    // MCP integration service (new architecture based on xiaozhi-esp32)
    private McpIntegrationService? _mcpIntegrationService;
    
    // Music voice coordination service
    private MusicVoiceCoordinationService? _musicVoiceCoordinationService;


    public event EventHandler<bool>? VoiceChatStateChanged;
    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<string>? ErrorOccurred;

    // Device state events
    public event EventHandler<DeviceState>? DeviceStateChanged;
    public event EventHandler<ListeningMode>? ListeningModeChanged;

    // Protocol message events
    public event EventHandler<MusicMessage>? MusicMessageReceived;
    public event EventHandler<SystemStatusMessage>? SystemStatusMessageReceived;
    public event EventHandler<LlmMessage>? LlmMessageReceived;
    public event EventHandler<TtsMessage>? TtsStateChanged;

    public bool IsVoiceChatActive => _isVoiceChatActive;
    public bool IsConnected => _communicationClient?.IsConnected ?? false;

    // Auto dialogue mode properties
    public bool KeepListening
    {
        get => _keepListening;
        set
        {
            if (_keepListening != value)
            {
                _keepListening = value;
                _logger?.LogInformation("Keep listening mode changed: {KeepListening}", value);

                if (value)
                {
                    SetListeningMode(ListeningMode.AlwaysOn);
                }
                else
                {
                    SetListeningMode(ListeningMode.Manual);
                }
            }
        }
    }
    public DeviceState CurrentState
    {
        get => _stateMachine?.CurrentState ?? _currentState;
        private set
        {
            if (_currentState != value)
            {
                var previousState = _currentState;
                _currentState = value;
                _logger?.LogInformation("Device state changed from {PreviousState} to {CurrentState}", previousState, value);

                // Wake word detector coordination (matches py-xiaozhi behavior)
                CoordinateWakeWordDetector(value);

                DeviceStateChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>
    /// Coordinate wake word detector state based on device state changes
    /// Matches py-xiaozhi behavior: pause during Listening, resume during Speaking/Idle
    /// Now also coordinates Microsoft Cognitive Services keyword spotting
    /// </summary>
    private void CoordinateWakeWordDetector(DeviceState newState)
    {
        try
        {
            switch (newState)
            {
                case DeviceState.Listening:
                    // Pause both VAD and keyword detection during user input (matches py-xiaozhi behavior)
                    // This prevents conflicts between user speech input and wake word detection
                    _interruptManager?.PauseVAD();
                    _keywordSpottingService?.Pause();
                    _logger?.LogDebug("Wake word detector and keyword spotting paused during Listening state");
                    break;

                case DeviceState.Speaking:
                case DeviceState.Idle:
                    // Resume both VAD and keyword detection during Speaking/Idle states (matches py-xiaozhi behavior)
                    // This allows interrupt detection during AI speaking and auto-activation during idle
                    // Add small delay to ensure audio stream synchronization is complete
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Brief delay to allow audio stream to stabilize
                            await Task.Delay(100);

                            _interruptManager?.ResumeVAD();
                            _keywordSpottingService?.Resume();
                            _logger?.LogDebug("Wake word detector and keyword spotting resumed during {State} state", newState);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Failed to resume wake word detection for state {State}", newState);
                        }
                    });
                    break;

                case DeviceState.Connecting:
                    // Keep both VAD and keyword detection paused during connection state
                    _interruptManager?.PauseVAD();
                    _keywordSpottingService?.Pause();
                    _logger?.LogDebug("Wake word detector and keyword spotting paused during Connecting state");
                    break;

                default:
                    _logger?.LogWarning("Unknown device state: {State}", newState);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to coordinate wake word detector and keyword spotting for state {State}", newState);
        }
    }

    public ListeningMode CurrentListeningMode
    {
        get => _listeningMode;
        private set
        {
            if (_listeningMode != value)
            {
                _listeningMode = value;
                _logger?.LogInformation("Listening mode changed to {ListeningMode}", value);
                ListeningModeChanged?.Invoke(this, value);
            }
        }
    }
    public bool IsKeywordDetectionEnabled => _keywordDetectionEnabled;

    public VoiceChatService(IConfigurationService configurationService, AudioStreamManager audioStreamManager, ILogger<VoiceChatService>? logger = null)
    {
        _configurationService = configurationService;
        _audioStreamManager = audioStreamManager;
        _logger = logger;
        
        // Initialize state machine
        InitializeStateMachine();
    }

    /// <summary>
    /// 初始化状态机
    /// </summary>
    private void InitializeStateMachine()
    {
        _stateMachine = new ConversationStateMachine();
        _stateMachineContext = new ConversationStateMachineContext(_stateMachine);
        
        // Set up state machine actions
        _stateMachineContext.OnEnterListening = async () =>
        {
            await StartListeningInternalAsync();
        };
        
        _stateMachineContext.OnExitListening = async () =>
        {
            await StopListeningInternalAsync();
        };
        
        _stateMachineContext.OnEnterSpeaking = async () =>
        {
            await StartSpeakingInternalAsync();
        };
        
        _stateMachineContext.OnExitSpeaking = async () =>
        {
            await StopSpeakingInternalAsync();
        };
        
        _stateMachineContext.OnEnterIdle = async () =>
        {
            await EnterIdleStateAsync();
        };
        
        _stateMachineContext.OnEnterConnecting = async () =>
        {
            await EnterConnectingStateAsync();
        };
        
        // Subscribe to state changes to sync with legacy state property
        _stateMachine.StateChanged += OnStateMachineStateChanged;
    }

    /// <summary>
    /// 处理状态机状态变化，同步到遗留的状态属性
    /// </summary>
    private void OnStateMachineStateChanged(object? sender, StateTransitionEventArgs e)
    {
        // Update the private field to sync with state machine
        if (_currentState != e.ToState)
        {
            var previousState = _currentState;
            _currentState = e.ToState;
            
            // Coordinate wake word detector
            CoordinateWakeWordDetector(e.ToState);
            
            // Fire the legacy event
            DeviceStateChanged?.Invoke(this, e.ToState);
        }
    }

    /// <summary>
    /// Set the interrupt manager for wake word detector coordination
    /// This enables py-xiaozhi-like wake word detector pause/resume behavior
    /// </summary>
    public void SetInterruptManager(InterruptManager interruptManager)
    {
        _interruptManager = interruptManager;
        _logger?.LogInformation("InterruptManager set for wake word detector coordination");
    }

    /// <summary>
    /// 设置关键词唤醒服务（对应py-xiaozhi的wake_word_detector集成）
    /// </summary>
    public void SetKeywordSpottingService(IKeywordSpottingService keywordSpottingService)
    {
        _keywordSpottingService = keywordSpottingService;

        // 订阅关键词检测事件
        _keywordSpottingService.KeywordDetected += OnKeywordDetected;
        _keywordSpottingService.ErrorOccurred += OnKeywordDetectionError;

        _logger?.LogInformation("关键词唤醒服务已设置");
    }    /// <summary>
    /// 设置MCP集成服务（新架构，基于xiaozhi-esp32的MCP实现）
    /// </summary>
    public void SetMcpIntegrationService(McpIntegrationService mcpIntegrationService)
    {
        _mcpIntegrationService = mcpIntegrationService;

        _logger?.LogInformation("MCP集成服务已设置");

        // 如果WebSocketClient已经存在，立即配置MCP集成服务
        if (_communicationClient is WebSocketClient wsClient)
        {
            wsClient.SetMcpIntegrationService(mcpIntegrationService);
            _logger?.LogInformation("MCP集成服务已配置到现有WebSocketClient");
        }
    }

    /// <summary>
    /// 设置音乐语音协调服务（用于音乐播放时暂停语音识别）
    /// </summary>
    public void SetMusicVoiceCoordinationService(MusicVoiceCoordinationService musicVoiceCoordinationService)
    {
        _musicVoiceCoordinationService = musicVoiceCoordinationService;
        _logger?.LogInformation("音乐语音协调服务已设置");
    }

    /// <summary>
    /// 启动关键词唤醒检测（对应py-xiaozhi的_start_wake_word_detector方法）
    /// </summary>
    public async Task<bool> StartKeywordDetectionAsync()
    {
        if (_keywordSpottingService == null)
        {
            _logger?.LogWarning("关键词唤醒服务未设置");
            return false;
        }
        try
        {
            // 使用共享音频流管理器启动关键词检测（对应py-xiaozhi的AudioCodec集成模式）
            var success = await _keywordSpottingService.StartAsync();
            if (success)
            {
                _keywordDetectionEnabled = true;
                _logger?.LogInformation("关键词唤醒检测已启动");
            }
            else
            {
                _logger?.LogError("启动关键词唤醒检测失败");
            }
            return success;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "启动关键词唤醒检测时发生错误");
            return false;
        }
    }

    /// <summary>
    /// 停止关键词唤醒检测
    /// </summary>
    public async Task StopKeywordDetectionAsync()
    {
        if (_keywordSpottingService != null)
        {
            await _keywordSpottingService.StopAsync();
            _keywordDetectionEnabled = false;
            _logger?.LogInformation("关键词唤醒检测已停止");
        }
    }

    /// <summary>
    /// 关键词检测事件处理（对应py-xiaozhi的_on_wake_word_detected回调）
    /// </summary>
    private void OnKeywordDetected(object? sender, KeywordDetectedEventArgs e)
    {
        _logger?.LogInformation($"检测到关键词: {e.Keyword} (完整文本: {e.FullText})");

        // 在后台线程处理关键词检测事件（对应py-xiaozhi的_handle_wake_word_detected）
        Task.Run(async () => await HandleKeywordDetectedAsync(e.Keyword));
    }

    /// <summary>
    /// 处理关键词检测事件（对应py-xiaozhi的_handle_wake_word_detected方法）
    /// </summary>
    private async Task HandleKeywordDetectedAsync(string keyword)
    {
        try
        {
            switch (CurrentState)
            {
                case DeviceState.Idle:
                    // 在空闲状态检测到关键词，启动对话（对应py-xiaozhi的唤醒逻辑）
                    _logger?.LogInformation("在空闲状态检测到关键词，启动语音对话");

                    // 添加小延迟确保keyword detection pause完成，避免音频流冲突
                    await Task.Delay(50);
                    KeepListening = true; // 启用持续监听模式
                    _stateMachine?.RequestTransition(ConversationTrigger.KeywordDetected, $"Keyword '{keyword}' detected in idle state");
                    break;

                case DeviceState.Speaking:
                    // 在AI说话时检测到关键词，中断对话（对应py-xiaozhi的中断逻辑）
                    _logger?.LogInformation("在AI说话时检测到关键词，中断当前对话");

                    // Use state machine to handle keyword interrupt
                    _stateMachine?.RequestTransition(ConversationTrigger.KeywordDetected, $"Keyword '{keyword}' detected during speaking - interrupt");
                    
                    await Task.Delay(50);

                    // 恢复关键词检测服务，准备下一次唤醒
                    try
                    {
                        _keywordSpottingService?.Resume();
                        _logger?.LogDebug("关键词检测服务已恢复");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "恢复关键词检测服务时出错");
                    }
                    break;

                case DeviceState.Listening:
                    // 在监听状态检测到关键词，可能是误触发，忽略
                    _logger?.LogDebug("在监听状态检测到关键词，忽略（可能是误触发）");
                    break;

                case DeviceState.Connecting:
                    // 在连接状态检测到关键词，暂不处理
                    _logger?.LogDebug("在连接状态检测到关键词，暂不处理");
                    break;

                default:
                    _logger?.LogDebug($"在状态 {CurrentState} 检测到关键词，暂不处理");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理关键词检测事件时发生错误");
        }
    }

    /// <summary>
    /// 关键词检测错误处理
    /// </summary>
    private void OnKeywordDetectionError(object? sender, string error)
    {
        _logger?.LogError($"关键词检测错误: {error}");

        // 尝试在空闲状态重新启动检测器（对应py-xiaozhi的错误恢复逻辑）
        if (CurrentState == DeviceState.Idle)
        {
            Task.Run(async () =>
            {
                await Task.Delay(1000); // 等待1秒后重试
                await StartKeywordDetectionAsync();
            });
        }
    }

    public async Task InitializeAsync(VerdureConfig config)
    {
        _config = config;
        
        // Use state machine to transition to connecting state
        _stateMachine?.RequestTransition(ConversationTrigger.ConnectToServer, "Service initialization");

        try
        {            // Initialize configuration service first
            if (!await _configurationService.InitializeMqttInfoAsync())
            {
                throw new InvalidOperationException("Failed to initialize MQTT configuration from OTA server");
            }
            // 初始化音频编解码器 - 使用OpusSharp
            _audioCodec = new OpusSharpAudioCodec();
            // 初始化音频录制和播放
            if (config.EnableVoice)
            {
                // 使用共享音频流管理器，而不是创建独立的录制器
                _audioRecorder = _audioStreamManager;
                _audioPlayer = new PortAudioPlayer();

                _audioRecorder.DataAvailable += OnAudioDataReceived;
                _audioPlayer.PlaybackStopped += OnAudioPlaybackStopped;
            }
            // 初始化通信客户端
            if (config.UseWebSocket)
            {
                _communicationClient = new WebSocketClient(_configurationService, _logger);
            }
            else
            {
                var mqttInfo = _configurationService.MqttInfo;
                if (mqttInfo != null)
                {
                    _communicationClient = new MqttNetClient(
                        mqttInfo.Endpoint,
                        1883, // Default MQTT port
                        mqttInfo.ClientId,
                        mqttInfo.PublishTopic);
                }
                else
                {
                    throw new InvalidOperationException("MQTT configuration not available");
                }
            }
            _communicationClient.MessageReceived += OnMessageReceived;
            _communicationClient.ConnectionStateChanged += OnConnectionStateChanged;
            // 订阅WebSocket专有的TTS状态变化事件
            if (_communicationClient is WebSocketClient wsClient)
            {
                wsClient.TtsStateChanged += OnTtsStateChanged;
                wsClient.AudioDataReceived += OnWebSocketAudioDataReceived;
                wsClient.MusicMessageReceived += OnMusicMessageReceived;
                wsClient.SystemStatusMessageReceived += OnSystemStatusMessageReceived;
                wsClient.LlmMessageReceived += OnLlmMessageReceived;

                // 订阅MCP事件
                wsClient.McpReadyForInitialization += OnMcpReadyForInitialization;
                wsClient.McpResponseReceived += OnMcpResponseReceived;
                wsClient.McpErrorOccurred += OnMcpErrorOccurred;

                // 如果已有MCP集成服务，立即配置到WebSocketClient
                if (_mcpIntegrationService != null)
                {
                    wsClient.SetMcpIntegrationService(_mcpIntegrationService);
                    _logger?.LogInformation("MCP集成服务已配置到WebSocketClient");
                }
            }// 连接到服务器
            await _communicationClient.ConnectAsync();

            // Use state machine to transition to connected state
            _stateMachine?.RequestTransition(ConversationTrigger.ServerConnected, "Successfully connected to server");

            // 启动关键词唤醒检测（对应py-xiaozhi的_start_wake_word_detector调用）
            if (_keywordSpottingService != null)
            {
                _logger?.LogInformation("正在启动关键词唤醒检测...");
                await StartKeywordDetectionAsync();
            }

            _logger?.LogInformation("语音聊天服务初始化完成");
        }
        catch (Exception ex)
        {
            // Use state machine to handle connection failure
            _stateMachine?.RequestTransition(ConversationTrigger.ConnectionFailed, $"Initialization failed: {ex.Message}");
            _logger?.LogError(ex, "初始化语音聊天服务失败");
            ErrorOccurred?.Invoke(this, $"初始化失败: {ex.Message}");
            throw;
        }
    }
    /// <summary>
    /// Toggle chat state for auto conversation mode (equivalent to Python toggle_chat_state)
    /// </summary>
    public Task ToggleChatStateAsync()
    {
        try
        {
            _logger?.LogInformation("Toggling chat state from {CurrentState}", CurrentState);

            switch (CurrentState)
            {
                case DeviceState.Idle:
                    if (KeepListening)
                    {
                        // When starting auto mode, set keep listening and start listening
                        _stateMachine?.RequestTransition(ConversationTrigger.StartVoiceChat, "Toggle chat state from idle");
                    }
                    break;

                case DeviceState.Listening:
                    // Stop current listening session
                    _stateMachine?.RequestTransition(ConversationTrigger.UserInterrupt, "Toggle chat state - stop listening");
                    break;

                case DeviceState.Speaking:
                    // Abort current speaking
                    _stateMachine?.RequestTransition(ConversationTrigger.UserInterrupt, "Toggle chat state - stop speaking");
                    break;

                case DeviceState.Connecting:
                    _logger?.LogWarning("Cannot toggle chat state while connecting");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to toggle chat state");
            ErrorOccurred?.Invoke(this, $"Failed to toggle chat state: {ex.Message}");
        }
        
        return Task.CompletedTask;
    }
    /// <summary>
    /// Internal method to start listening (called by state machine)
    /// </summary>
    private async Task StartListeningInternalAsync()
    {
        if (!IsConnected) return;

        try
        {
            // Send start listen message first before starting audio recording
            if (_communicationClient is WebSocketClient wsClient)
            {
                // Determine listening mode based on KeepListening setting
                var mode = KeepListening ? ListeningMode.AutoStop : ListeningMode.Manual;
                await wsClient.SendStartListenAsync(mode);
                _logger?.LogDebug("Sent start listen message with mode: {Mode}", mode);
            }

            if (_config?.EnableVoice == true && _audioRecorder != null)
            {
                await _audioRecorder.StartRecordingAsync(_config.AudioSampleRate, _config.AudioChannels);
                _isVoiceChatActive = true;
                VoiceChatStateChanged?.Invoke(this, true);
                _logger?.LogInformation("Started listening");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start listening");
            ErrorOccurred?.Invoke(this, $"Failed to start listening: {ex.Message}");
            // Transition back to idle on error
            _stateMachine?.RequestTransition(ConversationTrigger.ForceIdle, "Error in start listening");
        }
    }

    /// <summary>
    /// Internal method to stop listening (called by state machine)
    /// </summary>
    private async Task StopListeningInternalAsync()
    {
        try
        {
            // Send stop listen message first
            if (_communicationClient is WebSocketClient wsClient)
            {
                await wsClient.SendStopListenAsync();
                _logger?.LogDebug("Sent stop listen message");
            }

            if (_audioRecorder != null)
            {
                await _audioRecorder.StopRecordingAsync();
            }

            _isVoiceChatActive = false;
            VoiceChatStateChanged?.Invoke(this, false);

            _logger?.LogInformation("Stopped listening");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to stop listening");
            ErrorOccurred?.Invoke(this, $"Failed to stop listening: {ex.Message}");
        }
    }

    /// <summary>
    /// Internal method to start speaking (called by state machine)
    /// </summary>
    private async Task StartSpeakingInternalAsync()
    {
        try
        {
            _logger?.LogInformation("Started speaking");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start speaking");
            ErrorOccurred?.Invoke(this, $"Failed to start speaking: {ex.Message}");
        }
    }

    /// <summary>
    /// Internal method to stop speaking (called by state machine)
    /// </summary>
    private async Task StopSpeakingInternalAsync()
    {
        try
        {
            if (_audioPlayer != null)
            {
                await _audioPlayer.StopAsync();
            }

            _logger?.LogInformation("Stopped speaking");

            // Auto restart listening if in keep listening mode
            if (KeepListening)
            {
                // Give audio system time to fully complete playback
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(500);

                        // Only restart if we're still in idle state and keep listening is still enabled
                        if (CurrentState == DeviceState.Idle && KeepListening)
                        {
                            _stateMachine?.RequestTransition(ConversationTrigger.StartVoiceChat, "Auto-restart from keep listening mode");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to auto-restart listening");
                        ErrorOccurred?.Invoke(this, $"Failed to auto-restart listening: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to stop speaking");
            ErrorOccurred?.Invoke(this, $"Failed to stop speaking: {ex.Message}");
        }
    }

    /// <summary>
    /// Internal method to enter idle state (called by state machine)
    /// </summary>
    private Task EnterIdleStateAsync()
    {
        // Nothing specific needed for entering idle state currently
        return Task.CompletedTask;
    }

    /// <summary>
    /// Internal method to enter connecting state (called by state machine)
    /// </summary>
    private Task EnterConnectingStateAsync()
    {
        // Nothing specific needed for entering connecting state currently
        return Task.CompletedTask;
    }

    /// <summary>
    /// Set listening mode
    /// </summary>
    private void SetListeningMode(ListeningMode mode)
    {
        CurrentListeningMode = mode;

        switch (mode)
        {
            case ListeningMode.AlwaysOn:
                _keepListening = true;
                break;
            case ListeningMode.Manual:
                _keepListening = false;
                break;
        }
    }

    public Task StartVoiceChatAsync()
    {
        _stateMachine?.RequestTransition(ConversationTrigger.StartVoiceChat, "Manual start voice chat");
        return Task.CompletedTask;
    }

    public Task StopVoiceChatAsync()
    {
        _stateMachine?.RequestTransition(ConversationTrigger.StopVoiceChat, "Manual stop voice chat");
        return Task.CompletedTask;
    }

    public async Task InterruptAsync(AbortReason reason = AbortReason.UserInterruption)
    {
        try
        {
            _logger?.LogInformation("Interrupting current conversation with reason: {Reason}", reason);

            // 发送打断消息到WebSocket服务器
            if (_communicationClient is WebSocketClient wsClient)
            {
                await wsClient.SendAbortAsync(reason);
                _logger?.LogDebug("Sent abort message to server with reason: {Reason}", reason);
            }

            // Use state machine to handle interrupt
            _stateMachine?.RequestTransition(ConversationTrigger.UserInterrupt, $"Manual interrupt: {reason}");

            _lastAbortReason = reason;
            _logger?.LogInformation("Conversation interrupted successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to interrupt conversation");
            ErrorOccurred?.Invoke(this, $"打断对话失败: {ex.Message}");
            throw;
        }
    }

    public async Task SendTextMessageAsync(string text)
    {
        if (!IsConnected || _communicationClient == null) return;

        try
        {
            var message = new ChatMessage
            {
                Type = "text",
                Content = text,
                Role = "user",
                SessionId = _sessionId
            };

            await _communicationClient.SendMessageAsync(message);
            _logger?.LogInformation("发送文本消息: {Text}", text);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "发送文本消息失败");
            ErrorOccurred?.Invoke(this, $"发送文本消息失败: {ex.Message}");
        }
    }
    private async void OnAudioDataReceived(object? sender, byte[] audioData)
    {
        if (!_isVoiceChatActive || _communicationClient == null || _audioCodec == null || _config == null)
            return;

        try
        {
            // 编码音频数据
            var encodedData = _audioCodec.Encode(audioData, _config.AudioSampleRate, _config.AudioChannels);

            var voiceMessage = new VoiceMessage
            {
                Type = "voice",
                Data = encodedData,
                SampleRate = _config.AudioSampleRate,
                Channels = _config.AudioChannels,
                Format = _config.AudioFormat
            };

            await _communicationClient.SendVoiceAsync(voiceMessage);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理音频数据失败");
            ErrorOccurred?.Invoke(this, $"处理音频数据失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理音频播放完成事件
    /// </summary>
    private async void OnAudioPlaybackStopped(object? sender, EventArgs e)
    {
        try
        {
            // 当音频播放完成时，如果在说话状态，切换回空闲状态
            _stateMachine?.RequestTransition(ConversationTrigger.AudioPlaybackCompleted, "Audio playback completed");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理音频播放完成事件失败");
        }
    }

    private async void OnMessageReceived(object? sender, ChatMessage message)
    {
        try
        {
            MessageReceived?.Invoke(this, message);

            // Start speaking mode when receiving audio response
            if (message.AudioData != null)
            {
                // Use state machine to transition to speaking when audio is received
                _stateMachine?.RequestTransition(ConversationTrigger.AudioReceived, "Audio response received");
                
                if (_audioPlayer != null && _audioCodec != null && _config != null)
                {
                    var pcmData = _audioCodec.Decode(message.AudioData, _config.AudioOutputSampleRate, _config.AudioChannels);
                    await _audioPlayer.PlayAsync(pcmData, _config.AudioOutputSampleRate, _config.AudioChannels);
                }

                // 注意：不要在这里立即停止播放，因为可能还有更多音频数据要来
                // 播放完成应该由播放器的PlaybackStopped事件或者明确的停止指令来触发
            }

            _logger?.LogInformation("收到消息: {Type} - {Content}", message.Type, message.Content);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理接收消息失败");
            ErrorOccurred?.Invoke(this, $"处理接收消息失败: {ex.Message}");
        }
    }

    private void OnConnectionStateChanged(object? sender, bool isConnected)
    {
        _logger?.LogInformation("连接状态变化: {IsConnected}", isConnected);

        if (isConnected)
        {
            _stateMachine?.RequestTransition(ConversationTrigger.ServerConnected, "Connection established");
        }
        else
        {
            _stateMachine?.RequestTransition(ConversationTrigger.ServerDisconnected, "Connection lost");

            if (_isVoiceChatActive)
            {
                // 连接断开时自动停止语音对话
                _ = Task.Run(() => StopVoiceChatAsync());
            }
        }
    }

    /// <summary>
    /// 处理TTS状态变化事件
    /// </summary>
    private async void OnTtsStateChanged(object? sender, TtsMessage message)
    {
        try
        {
            _logger?.LogDebug("收到TTS状态变化: {State}, 文本: {Text}", message.State, message.Text);

            switch (message.State?.ToLowerInvariant())
            {
                case "start":
                    // TTS开始播放时，从监听状态切换到说话状态
                    _stateMachine?.RequestTransition(ConversationTrigger.TtsStarted, $"TTS started: {message.Text}");
                    break;

                case "stop":
                    // TTS停止播放时，从说话状态切换回空闲或监听状态
                    _stateMachine?.RequestTransition(ConversationTrigger.TtsCompleted, "TTS completed");
                    break;

                case "sentence_start":
                    _logger?.LogDebug("TTS句子开始: {Text}", message.Text);
                    break;

                case "sentence_end":
                    _logger?.LogDebug("TTS句子结束");
                    break;
            }

            TtsStateChanged?.Invoke(this, message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理TTS状态变化失败");
            ErrorOccurred?.Invoke(this, $"处理TTS状态变化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理WebSocket接收到的音频数据事件
    /// </summary>
    private async void OnWebSocketAudioDataReceived(object? sender, byte[] audioData)
    {
        try
        {
            _logger?.LogDebug("收到WebSocket音频数据，长度: {Length}", audioData.Length);

            if (_audioPlayer != null && _audioCodec != null && _config != null)
            {
                // Use state machine to transition to speaking when audio is received
                _stateMachine?.RequestTransition(ConversationTrigger.AudioReceived, $"WebSocket audio data received: {audioData.Length} bytes");
                
                // 解码并播放音频数据 - 使用输出采样率
                var pcmData = _audioCodec.Decode(audioData, _config.AudioOutputSampleRate, _config.AudioChannels);
                await _audioPlayer.PlayAsync(pcmData, _config.AudioOutputSampleRate, _config.AudioChannels);

                // 注意：不要在这里立即停止播放，因为可能还有更多音频数据要来
                // 播放完成应该由播放器的PlaybackStopped事件或者明确的停止指令来触发
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理WebSocket音频数据失败");
            ErrorOccurred?.Invoke(this, $"处理WebSocket音频数据失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理音乐播放器消息事件
    /// </summary>
    private void OnMusicMessageReceived(object? sender, MusicMessage message)
    {
        try
        {
            _logger?.LogDebug("收到音乐消息: {Action}, 歌曲: {Song}", message.Action, message.SongName);
            MusicMessageReceived?.Invoke(this, message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理音乐消息失败");
            ErrorOccurred?.Invoke(this, $"处理音乐消息失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理系统状态消息事件
    /// </summary>
    private void OnSystemStatusMessageReceived(object? sender, SystemStatusMessage message)
    {
        try
        {
            _logger?.LogDebug("收到系统状态消息: {Component}, 状态: {Status}", message.Component, message.Status);
            SystemStatusMessageReceived?.Invoke(this, message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理系统状态消息失败");
            ErrorOccurred?.Invoke(this, $"处理系统状态消息失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理LLM情感消息事件
    /// </summary>
    private void OnLlmMessageReceived(object? sender, LlmMessage message)
    {
        try
        {
            _logger?.LogDebug("收到LLM情感消息: {Emotion}", message.Emotion);
            LlmMessageReceived?.Invoke(this, message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理LLM情感消息失败");
            ErrorOccurred?.Invoke(this, $"处理LLM情感消息失败: {ex.Message}");
        }
    }
    /// <summary>
    /// 处理MCP准备就绪事件 - 设备声明支持MCP时自动初始化
    /// </summary>
    private void OnMcpReadyForInitialization(object? sender, EventArgs e)
    {
        try
        {
            _logger?.LogInformation("设备已准备好MCP初始化，开始自动初始化MCP协议");

            if (_communicationClient is WebSocketClient wsClient && _mcpIntegrationService != null)
            {
                // 在后台线程执行MCP初始化
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await wsClient.InitializeMcpAsync();
                        _logger?.LogInformation("MCP协议自动初始化完成");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "MCP自动初始化过程中发生错误");
                    }
                });
            }
            else
            {
                _logger?.LogWarning("无法自动初始化MCP：WebSocketClient或MCP集成服务未设置");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "MCP自动初始化失败");
            ErrorOccurred?.Invoke(this, $"MCP初始化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理MCP响应接收事件
    /// 根据xiaozhi-esp32协议文档实现MCP响应处理逻辑
    /// </summary>
    private void OnMcpResponseReceived(object? sender, string response)
    {
        try
        {
            _logger?.LogDebug("收到MCP响应: {Response}", response);

            // 解析JSON-RPC 2.0响应
            var responseElement = JsonSerializer.Deserialize<JsonDocument>(response);
            if (responseElement != null)
            {
                // 检查是否是初始化响应
                if (responseElement.RootElement.TryGetProperty("id", out var idElement) &&
                    responseElement.RootElement.TryGetProperty("method", out var methodElement))
                {
                    //var requestId = idElement.GetString();

                    var method = methodElement.GetString();

                    _logger?.LogDebug("处理MCP响应, 方法: {Method}", method);

                    responseElement.RootElement.TryGetProperty("params", out var resultElement);
                    switch (method)
                    {
                        case "initialize":
                            // 处理初始化响应（id通常为1）

                            HandleMcpInitializeResponse(resultElement);
                            break;

                        case "tools/list":
                            // 处理工具列表响应（id通常为2或更大）
                            HandleMcpToolsListResponse(resultElement);
                            break;

                        case "tools/call":
                            // 处理工具调用响应
                            HandleMcpToolCallResponse(response);
                            break;
                        default:
                            _logger?.LogDebug("收到未知类型的协议消息: {Type}", method);
                            break;
                    }
                }
                //// 处理通知消息（没有id字段）
                //else if (responseElement.RootElement.TryGetProperty("method", out var methodElement))
                //{
                //    var method = methodElement.GetString();
                //    if (method?.StartsWith("notifications/") == true)
                //    {
                //        HandleMcpNotification(responseElement.RootElement);
                //    }
                //}
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理MCP响应时发生错误");
        }
    }

    /// <summary>
    /// 处理MCP初始化响应
    /// </summary>
    private void HandleMcpInitializeResponse(JsonElement resultElement)
    {
        try
        {
            _logger?.LogInformation("设备已准备好MCP初始化，开始自动初始化MCP协议");

            if (_communicationClient is WebSocketClient wsClient && _mcpIntegrationService != null)
            {
                // 在后台线程执行MCP初始化
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await wsClient.InitializeMcpAsync();
                        _logger?.LogInformation("MCP协议初始化完成");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "MCP初始化过程中发生错误");
                    }
                });
            }
            else
            {
                _logger?.LogWarning("无法自动初始化MCP：WebSocketClient或MCP集成服务未设置");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "MCP自动初始化失败");
            ErrorOccurred?.Invoke(this, $"MCP初始化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理MCP工具列表响应
    /// </summary>
    private void HandleMcpToolsListResponse(JsonElement toolsElement)
    {
        try
        {
            // 如果有MCP集成服务，注册工具
            if (_mcpIntegrationService != null)
            {
                var tools = _mcpIntegrationService.GetAllSimpleMcpTools();
                _logger?.LogInformation("响应mcp 工具列表");

                if (_communicationClient is WebSocketClient wsClient && _mcpIntegrationService != null)
                {
                    // 在后台线程执行MCP初始化
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await wsClient.SendMcpToolsListResponseAsync(2, tools, null);
                            _logger?.LogInformation("mcp 工具列表已发送");
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "mcp 工具列表发生错误");
                        }
                    });
                }
                else
                {
                    _logger?.LogWarning("无法自动初始化MCP：WebSocketClient或MCP集成服务未设置");
                }

            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理MCP工具列表响应失败");
        }
    }

    /// <summary>
    /// 处理MCP工具调用响应
    /// </summary>
    private void HandleMcpToolCallResponse(string resultJson)
    {
        try
        {
            if (_communicationClient is WebSocketClient wsClient && _mcpIntegrationService != null)
            {
                // 在后台线程执行MCP初始化
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 如果有MCP集成服务，注册工具调用结果
                        _logger?.LogInformation("响应mcp 工具调用结果");
                        var content = await _mcpIntegrationService.HandleMcpRequestAsync(resultJson);
                        await wsClient.SendMcpMessageAsync(JsonDocument.Parse(content));
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "mcp 工具列表发生错误");
                    }
                });
            }
            else
            {
                _logger?.LogWarning("处理MCP工具调用响应失败：WebSocketClient或MCP集成服务未设置");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理MCP工具调用响应失败");
        }
    }

    /// <summary>
    /// 处理MCP通知消息
    /// </summary>
    private void HandleMcpNotification(JsonElement notificationElement)
    {
        try
        {
            if (notificationElement.TryGetProperty("method", out var methodElement))
            {
                var method = methodElement.GetString();
                _logger?.LogDebug("收到MCP通知: {Method}", method);

                // 根据不同的通知类型进行处理
                if (method == "notifications/state_changed")
                {
                    HandleDeviceStateNotification(notificationElement);
                }
                else
                {
                    _logger?.LogDebug("未处理的MCP通知类型: {Method}", method);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理MCP通知失败");
        }
    }

    /// <summary>
    /// 处理设备状态变化通知
    /// </summary>
    private void HandleDeviceStateNotification(JsonElement notificationElement)
    {
        try
        {
            if (notificationElement.TryGetProperty("params", out var paramsElement))
            {
                var newState = "";
                var oldState = "";

                if (paramsElement.TryGetProperty("newState", out var newStateElement))
                {
                    newState = newStateElement.GetString() ?? "";
                }

                if (paramsElement.TryGetProperty("oldState", out var oldStateElement))
                {
                    oldState = oldStateElement.GetString() ?? "";
                }

                _logger?.LogInformation("设备状态变化通知: {OldState} -> {NewState}", oldState, newState);

                // 可以在这里根据设备状态变化进行相应处理
                // 比如更新UI状态、触发相应的语音提示等
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理设备状态变化通知失败");
        }
    }

    /// <summary>
    /// 处理MCP错误事件
    /// </summary>
    private void OnMcpErrorOccurred(object? sender, Exception error)
    {
        try
        {
            _logger?.LogError(error, "MCP协议发生错误");
            ErrorOccurred?.Invoke(this, $"MCP错误: {error.Message}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理MCP错误事件时发生异常");
        }
    }

    public void Dispose()
    {
        try
        {
            _logger?.LogInformation("开始释放VoiceChatService资源");

            // 1. 停止语音聊天并等待完成
            try
            {
                var stopTask = StopVoiceChatAsync();
                if (!stopTask.Wait(3000)) // 最多等待3秒
                {
                    _logger?.LogWarning("停止语音聊天超时");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "停止语音聊天时出错");
            }

            // 2. 释放通信客户端
            if (_communicationClient != null)
            {
                try
                {
                    _communicationClient.MessageReceived -= OnMessageReceived;
                    _communicationClient.ConnectionStateChanged -= OnConnectionStateChanged;
                    // 如果是WebSocket客户端，取消订阅更多事件
                    if (_communicationClient is WebSocketClient webSocketClient)
                    {
                        //webSocketClient.ProtocolMessageReceived -= OnProtocolMessageReceived;
                        webSocketClient.AudioDataReceived -= OnWebSocketAudioDataReceived;
                        webSocketClient.TtsStateChanged -= OnTtsStateChanged;
                        webSocketClient.MusicMessageReceived -= OnMusicMessageReceived;
                        webSocketClient.SystemStatusMessageReceived -= OnSystemStatusMessageReceived;
                        webSocketClient.LlmMessageReceived -= OnLlmMessageReceived;

                        // 取消订阅MCP事件
                        webSocketClient.McpReadyForInitialization -= OnMcpReadyForInitialization;
                        webSocketClient.McpResponseReceived -= OnMcpResponseReceived;
                        webSocketClient.McpErrorOccurred -= OnMcpErrorOccurred;
                    }

                    _communicationClient.Dispose();
                    _communicationClient = null;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "释放通信客户端时出错");
                }
            }

            // 3. 释放音频录制器
            if (_audioRecorder != null)
            {
                try
                {
                    _audioRecorder.DataAvailable -= OnAudioDataReceived;

                    if (_audioRecorder is IDisposable disposableRecorder)
                    {
                        disposableRecorder.Dispose();
                    }
                    _audioRecorder = null;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "释放音频录制器时出错");
                }
            }

            // 4. 释放音频播放器
            if (_audioPlayer != null)
            {
                try
                {
                    _audioPlayer.PlaybackStopped -= OnAudioPlaybackStopped;

                    if (_audioPlayer is IDisposable disposablePlayer)
                    {
                        disposablePlayer.Dispose();
                    }
                    _audioPlayer = null;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "释放音频播放器时出错");
                }
            }
            // 5. 释放音频编解码器
            if (_audioCodec != null)
            {
                try
                {
                    if (_audioCodec is IDisposable disposableCodec)
                    {
                        disposableCodec.Dispose();
                    }
                    _audioCodec = null;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "释放音频编解码器时出错");
                }
            }

            // 6. 释放关键词检测服务
            if (_keywordSpottingService != null)
            {
                try
                {
                    _keywordSpottingService.KeywordDetected -= OnKeywordDetected;
                    _keywordSpottingService.ErrorOccurred -= OnKeywordDetectionError;
                    _keywordSpottingService.Dispose();
                    _keywordSpottingService = null;
                    _keywordDetectionEnabled = false;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "释放关键词检测服务时出错");
                }
            }            // 7. 释放MCP集成服务
            if (_mcpIntegrationService != null)
            {
                try
                {
                    _mcpIntegrationService.Dispose();
                    _mcpIntegrationService = null;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "释放MCP集成服务时出错");
                }
            }

            // 8. 释放音乐语音协调服务
            if (_musicVoiceCoordinationService != null)
            {
                try
                {
                    _musicVoiceCoordinationService.Dispose();
                    _musicVoiceCoordinationService = null;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "释放音乐语音协调服务时出错");
                }
            }

            // 9. 释放状态机
            if (_stateMachineContext != null)
            {
                try
                {
                    _stateMachineContext.Dispose();
                    _stateMachineContext = null;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "释放状态机上下文时出错");
                }
            }

            if (_stateMachine != null)
            {
                try
                {
                    _stateMachine.StateChanged -= OnStateMachineStateChanged;
                    _stateMachine = null;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "释放状态机时出错");
                }
            }

            // 10. 重置状态
            _isVoiceChatActive = false;
            _currentState = DeviceState.Idle;
            _lastAbortReason = AbortReason.None;

            _logger?.LogInformation("VoiceChatService资源释放完成");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "释放VoiceChatService资源时发生严重错误");
        }
    }
}
