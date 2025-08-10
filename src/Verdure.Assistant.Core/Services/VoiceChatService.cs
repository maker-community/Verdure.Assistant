using Microsoft.Extensions.Logging;
using System.Text.Json;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services.MCP;
using Verdure.Assistant.Core.Events;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// 语音聊天服务实现
/// </summary>
public class VoiceChatService : IVoiceChatService
{
    private readonly ILogger<VoiceChatService>? _logger;
    private readonly IConfigurationService _configurationService;
    private readonly AudioStreamManager _audioStreamManager;
    private readonly ICommunicationClient? _communicationClient;
    private readonly IAudioPlayer? _audioPlayer;
    private readonly IAudioCodec? _audioCodec;
    private VerdureConfig? _config;
    private bool _isVoiceChatActive;
    private string _sessionId = Guid.NewGuid().ToString();

    // Device state management - 简化为只需要监听模式和保持监听标志
    private ListeningMode _listeningMode = ListeningMode.Manual;

    private bool _keepListening = false;
    // State machine for conversation logic
    private ConversationStateMachine? _stateMachine;
    private ConversationStateMachineContext? _stateMachineContext;

    /// <summary>
    /// 暴露状态机供外部直接访问，实现状态事件的直接订阅
    /// </summary>
    public ConversationStateMachine? StateMachine => _stateMachine;

    // Wake word detector coordination (matches py-xiaozhi behavior)
    private InterruptManager? _interruptManager;
    // Keyword spotting service (Microsoft Cognitive Services based)
    private IKeywordSpottingService? _keywordSpottingService;
    private bool _keywordDetectionEnabled = false;

    // MCP integration service (new architecture based on xiaozhi-esp32)
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
    /// <summary>
    /// 当前设备状态 - 直接从状态机获取，简化状态管理
    /// </summary>
    public DeviceState CurrentState => _stateMachine?.CurrentState ?? DeviceState.Idle;



    #region 构造函数和初始化
    public VoiceChatService(IConfigurationService configurationService,
        AudioStreamManager audioStreamManager, ILogger<VoiceChatService>? logger = null)
    {
        _configurationService = configurationService;
        _audioStreamManager = audioStreamManager;
        _logger = logger;

        // 初始化音频编解码器 - 使用OpusSharp
        _audioCodec = new OpusSharpAudioCodec();
        // 初始化音频录制和播放
        _audioPlayer = new PortAudioPlayer();

        _audioStreamManager.DataAvailable += OnAudioDataReceived;
        _audioPlayer.PlaybackStopped += OnAudioPlaybackStopped;
        // 初始化通信客户端
        _communicationClient = new WebSocketClient(_configurationService, _logger);
        _communicationClient.MessageReceived += OnMessageReceived;
        _communicationClient.ConnectionStateChanged += OnConnectionStateChanged;
        // 订阅WebSocket专有的事件
        if (_communicationClient is WebSocketClient wsClient)
        {
            // 使用新的统一事件系统
            wsClient.WebSocketEventOccurred += OnWebSocketEventOccurred;

            // 如果已有MCP集成服务，立即配置到WebSocketClient
            if (_mcpIntegrationService != null)
            {
                wsClient.SetMcpIntegrationService(_mcpIntegrationService);
                _logger?.LogInformation("MCP集成服务已配置到WebSocketClient");
            }
        }
        // Initialize state machine
        InitializeStateMachine();
    }

    private void InitializeStateMachine()
    {
        _stateMachine = new ConversationStateMachine();
        _stateMachineContext = new ConversationStateMachineContext(_stateMachine)
        {
            // Set up state machine actions
            OnEnterListening = async () =>
                {
                    await StartListeningInternalAsync();
                },

            OnExitListening = async () =>
                {
                    await StopListeningInternalAsync();
                },

            OnEnterSpeaking = () =>
                {
                    // 进入说话状态 - 同步处理
                    return Task.CompletedTask;
                },

            OnExitSpeaking = async () =>
                {
                    await StopSpeakingInternalAsync();
                },

            OnEnterIdle = async () =>
                {
                    await EnterIdleStateAsync();
                },

            OnEnterConnecting = async () =>
                {
                    await EnterConnectingStateAsync();
                }
        };

        // Subscribe to state changes to sync with legacy state property
        _stateMachine.StateChanged += OnStateMachineStateChanged;
    }


    public async Task InitializeAsync(VerdureConfig config)
    {
        _config = config;
        try
        {
            // 将配置传递给关键词检测服务
            if (_keywordSpottingService != null)
            {
                _keywordSpottingService.SetConfig(config);
            }

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
    #endregion

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


    /// <summary>
    /// 处理状态机状态变化，简化状态同步逻辑
    /// </summary>
    private void OnStateMachineStateChanged(object? sender, StateTransitionEventArgs e)
    {
        // 直接转发状态机事件
        DeviceStateChanged?.Invoke(this, e.ToState);

        _logger?.LogDebug("State synchronized from state machine: {FromState} -> {ToState}", e.FromState, e.ToState);
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
    /// 切换关键词模型
    /// </summary>
    /// <param name="modelFileName">模型文件名</param>
    /// <returns>切换是否成功</returns>
    public async Task<bool> SwitchKeywordModelAsync(string modelFileName)
    {
        if (_keywordSpottingService == null)
        {
            _logger?.LogWarning("关键词检测服务未设置，无法切换模型");
            return false;
        }

        _logger?.LogInformation("正在切换关键词模型为: {ModelFileName}", modelFileName);
        
        var result = await _keywordSpottingService.SwitchKeywordModelAsync(modelFileName);
        
        if (result)
        {
            _logger?.LogInformation("关键词模型切换成功");
        }
        else
        {
            _logger?.LogError("关键词模型切换失败");
        }
        
        return result;
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
            // 检查音频流管理器状态，确保音频流可用
            if (_audioStreamManager != null && !_audioStreamManager.IsRecording)
            {
                _logger?.LogDebug("音频流未激活，尝试启动共享音频流用于关键词检测");
                try
                {
                    await _audioStreamManager.StartRecordingAsync(_config?.AudioSampleRate ?? 16000, _config?.AudioChannels ?? 1);
                    _logger?.LogDebug("共享音频流已启动用于关键词检测");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "启动共享音频流失败");
                    return false;
                }
            }

            // 使用共享音频流管理器启动关键词检测（对应py-xiaozhi的AudioCodec集成模式）
            var success = await _keywordSpottingService.StartAsync(_audioStreamManager);
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
                    //await Task.Delay(50);
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
                        //_keywordSpottingService?.Resume();
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
                    // 在空闲状态检测到关键词，启动对话（对应py-xiaozhi的唤醒逻辑）
                    _logger?.LogInformation("在空闲状态检测到关键词，启动语音对话");

                    // 添加小延迟确保keyword detection pause完成，避免音频流冲突
                    //await Task.Delay(50);
                    KeepListening = true; // 启用持续监听模式
                    _stateMachine?.RequestTransition(ConversationTrigger.KeywordDetected, $"Keyword '{keyword}' detected in idle state");
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

            if (_config?.EnableVoice == true && _audioStreamManager != null)
            {
                await _audioStreamManager.StartRecordingAsync(_config.AudioSampleRate, _config.AudioChannels);
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

            if (_audioStreamManager != null)
            {
                await _audioStreamManager.StopRecordingAsync();
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
    private async Task EnterIdleStateAsync()
    {
        try
        {
            if (_keywordSpottingService != null)
            {
                await StopKeywordDetectionAsync();

                _logger?.LogInformation("正在启动关键词唤醒检测...");
                await StartKeywordDetectionAsync();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "进入空闲状态时发生错误");
        }
    }

    /// <summary>
    /// Internal method to enter connecting state (called by state machine)
    /// </summary>
    private async Task EnterConnectingStateAsync()
    {
        if (_communicationClient != null)
        {
            await _communicationClient.ConnectAsync();
            // Use state machine to transition to connected state
            //_stateMachine?.RequestTransition(ConversationTrigger.ServerConnected, "Successfully connected to server");
        }
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
            if (_communicationClient.IsConnected == false)
            {
                _logger?.LogWarning("无法发送音频数据，未连接到服务器");
                return;
            }
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
    private void OnAudioPlaybackStopped(object? sender, EventArgs e)
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

    private async void OnConnectionStateChanged(object? sender, bool isConnected)
    {
        _logger?.LogInformation("连接状态变化: {IsConnected}", isConnected);

        if (isConnected)
        {
            _stateMachine?.RequestTransition(ConversationTrigger.ServerConnected, "Connection established");
        }
        else
        {
            _stateMachine?.RequestTransition(ConversationTrigger.ServerDisconnected, "Connection lost");


            if (_communicationClient != null)
            {
                _logger?.LogInformation("连接断开，尝试断开WebSocket连接");
                await _communicationClient.DisconnectAsync();
            }
        }
    }

    /// <summary>
    /// 统一处理WebSocket事件 - 简化事件处理逻辑
    /// </summary>
    private void OnWebSocketEventOccurred(object? sender, WebSocketEventArgs e)
    {
        try
        {
            _logger?.LogDebug("WebSocket event received: {Trigger}", e.Trigger);

            switch (e.Trigger)
            {
                case WebSocketEventTrigger.TtsStarted:
                    if (e is TtsEventArgs ttsStarted)
                        HandleTtsStarted(ttsStarted);
                    break;

                case WebSocketEventTrigger.TtsStopped:
                    if (e is TtsEventArgs ttsStopped)
                        HandleTtsStopped(ttsStopped);
                    break;

                case WebSocketEventTrigger.TtsSentenceStarted:
                    if (e is TtsEventArgs ttsSentenceStarted)
                        HandleTtsSentenceStarted(ttsSentenceStarted);
                    break;

                case WebSocketEventTrigger.TtsSentenceEnded:
                    if (e is TtsEventArgs ttsSentenceEnded)
                        HandleTtsSentenceEnded(ttsSentenceEnded);
                    break;

                case WebSocketEventTrigger.AudioDataReceived:
                    if (e is MessageEventArgs audioData)
                        HandleAudioDataReceived(audioData);
                    break;

                case WebSocketEventTrigger.MusicPlay:
                case WebSocketEventTrigger.MusicPause:
                case WebSocketEventTrigger.MusicStop:
                case WebSocketEventTrigger.MusicLyricUpdate:
                case WebSocketEventTrigger.MusicSeek:
                    if (e is MusicEventArgs musicEvent)
                        HandleMusicEvent(musicEvent);
                    break;

                case WebSocketEventTrigger.SystemStatusUpdate:
                    if (e is SystemStatusEventArgs systemStatus)
                        HandleSystemStatusEvent(systemStatus);
                    break;

                case WebSocketEventTrigger.LlmEmotionUpdate:
                    if (e is LlmEmotionEventArgs llmEmotion)
                        HandleLlmEmotionEvent(llmEmotion);
                    break;

                case WebSocketEventTrigger.McpReadyForInitialization:
                    if (e is McpEventArgs mcpReady)
                        HandleMcpReadyForInitialization(mcpReady);
                    break;

                case WebSocketEventTrigger.McpResponseReceived:
                    if (e is McpEventArgs mcpResponse)
                        HandleMcpResponseReceived(mcpResponse);
                    break;

                case WebSocketEventTrigger.McpError:
                    if (e is McpEventArgs mcpError)
                        HandleMcpError(mcpError);
                    break;

                default:
                    _logger?.LogDebug("Unhandled WebSocket event: {Trigger}", e.Trigger);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling WebSocket event: {Trigger}", e.Trigger);
        }
    }

    #region 分离的事件处理方法

    private void HandleTtsStarted(TtsEventArgs e)
    {
        // TTS开始播放时，从监听状态切换到说话状态
        _stateMachine?.RequestTransition(ConversationTrigger.TtsStarted, $"TTS started: {e.Text}");
        TtsStateChanged?.Invoke(this, e.TtsMessage!);
    }

    private void HandleTtsStopped(TtsEventArgs e)
    {
        // TTS停止播放时，从说话状态切换回空闲或监听状态
        _stateMachine?.RequestTransition(ConversationTrigger.TtsCompleted, "TTS completed");
        TtsStateChanged?.Invoke(this, e.TtsMessage!);
    }

    private void HandleTtsSentenceStarted(TtsEventArgs e)
    {
        _logger?.LogDebug("TTS句子开始: {Text}", e.Text);
        TtsStateChanged?.Invoke(this, e.TtsMessage!);
    }

    private void HandleTtsSentenceEnded(TtsEventArgs e)
    {
        _logger?.LogDebug("TTS句子结束");
        TtsStateChanged?.Invoke(this, e.TtsMessage!);
    }

    private async void HandleAudioDataReceived(MessageEventArgs e)
    {
        if (e.AudioData == null) return;

        _logger?.LogDebug("收到WebSocket音频数据，长度: {Length}", e.AudioData.Length);

        if (_audioPlayer != null && _audioCodec != null && _config != null)
        {
            // Use state machine to transition to speaking when audio is received
            _stateMachine?.RequestTransition(ConversationTrigger.AudioReceived, $"WebSocket audio data received: {e.AudioData.Length} bytes");

            // 解码并播放音频数据 - 使用输出采样率
            var pcmData = _audioCodec.Decode(e.AudioData, _config.AudioOutputSampleRate, _config.AudioChannels);
            await _audioPlayer.PlayAsync(pcmData, _config.AudioOutputSampleRate, _config.AudioChannels);

            // 注意：不要在这里立即停止播放，因为可能还有更多音频数据要来
            // 播放完成应该由播放器的PlaybackStopped事件或者明确的停止指令来触发
        }
    }

    private void HandleMusicEvent(MusicEventArgs e)
    {
        _logger?.LogDebug("收到音乐消息: {Action}, 歌曲: {Song}", e.Action, e.SongName);
        MusicMessageReceived?.Invoke(this, e.MusicMessage!);
    }

    private void HandleSystemStatusEvent(SystemStatusEventArgs e)
    {
        _logger?.LogDebug("收到系统状态消息: {Component}, 状态: {Status}", e.Component, e.Status);
        SystemStatusMessageReceived?.Invoke(this, e.SystemStatusMessage!);
    }

    private void HandleLlmEmotionEvent(LlmEmotionEventArgs e)
    {
        _logger?.LogDebug("收到LLM情感消息: {Emotion}", e.Emotion);
        LlmMessageReceived?.Invoke(this, e.LlmMessage!);
    }

    private async void HandleMcpReadyForInitialization(McpEventArgs e)
    {
        _logger?.LogInformation("设备已准备好MCP初始化，开始自动初始化MCP协议");

        if (_communicationClient is WebSocketClient wsClient && wsClient.IsConnected && _mcpIntegrationService != null)
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
        }
        else
        {
            _logger?.LogWarning("无法自动初始化MCP：WebSocketClient或MCP集成服务未设置");
        }
    }

    private void HandleMcpResponseReceived(McpEventArgs e)
    {
        if (string.IsNullOrEmpty(e.ResponseJson)) return;

        try
        {
            _logger?.LogDebug("收到MCP响应: {Response}", e.ResponseJson);

            // 解析JSON-RPC 2.0响应
            var responseElement = JsonSerializer.Deserialize<JsonDocument>(e.ResponseJson);
            if (responseElement != null)
            {
                // 检查是否是初始化响应
                if (responseElement.RootElement.TryGetProperty("id", out var idElement) &&
                    responseElement.RootElement.TryGetProperty("method", out var methodElement))
                {
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
                            HandleMcpToolCallResponse(e.ResponseJson);
                            break;
                        default:
                            _logger?.LogDebug("收到未知类型的协议消息: {Type}", method);
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理MCP响应时发生错误");
        }
    }

    private void HandleMcpError(McpEventArgs e)
    {
        _logger?.LogError(e.Error, "MCP协议发生错误");
        ErrorOccurred?.Invoke(this, $"MCP错误: {e.Error?.Message}");
    }

    #endregion

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
                    // 如果是WebSocket客户端，取消订阅统一事件
                    if (_communicationClient is WebSocketClient webSocketClient)
                    {
                        webSocketClient.WebSocketEventOccurred -= OnWebSocketEventOccurred;
                    }

                    _communicationClient.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "释放通信客户端时出错");
                }
            }

            // 3. 释放音频录制器
            if (_audioStreamManager != null)
            {
                try
                {
                    _audioStreamManager.DataAvailable -= OnAudioDataReceived;

                    if (_audioStreamManager is IDisposable disposableRecorder)
                    {
                        disposableRecorder.Dispose();
                    }
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

            // 10. 重置状态 - 不再需要重置_currentState，因为已经移除
            _isVoiceChatActive = false;

            _logger?.LogInformation("VoiceChatService资源释放完成");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "释放VoiceChatService资源时发生严重错误");
        }
    }
}
