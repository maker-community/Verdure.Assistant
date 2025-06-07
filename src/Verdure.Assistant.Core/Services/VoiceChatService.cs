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
    // Wake word detector coordination (matches py-xiaozhi behavior)
    private InterruptManager? _interruptManager;    
    // Keyword spotting service (Microsoft Cognitive Services based)
    private IKeywordSpottingService? _keywordSpottingService;
    private bool _keywordDetectionEnabled = false;    // MCP integration service (new architecture based on xiaozhi-esp32)
    private McpIntegrationService? _mcpIntegrationService;


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
        get => _currentState;
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
    }    public bool IsKeywordDetectionEnabled => _keywordDetectionEnabled;
    
    public VoiceChatService(IConfigurationService configurationService, AudioStreamManager audioStreamManager, ILogger<VoiceChatService>? logger = null)
    {
        _configurationService = configurationService;
        _audioStreamManager = audioStreamManager;
        _logger = logger;
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
    }    
    
    /// <summary>
    /// 设置MCP集成服务（新架构，基于xiaozhi-esp32的MCP实现）
    /// </summary>
    public void SetMcpIntegrationService(McpIntegrationService mcpIntegrationService)
    {
        _mcpIntegrationService = mcpIntegrationService;
        
        _logger?.LogInformation("MCP集成服务已设置");
    }    /// <summary>
    /// 处理IoT命令执行（基于xiaozhi-esp32的MCP架构）
    /// </summary>
    private async Task HandleIoTCommandAsync(IotCommandMessage command)
    {
        try
        {
            _logger?.LogInformation("执行MCP IoT命令: 设备ID={DeviceId}, 方法={Method}", 
                command.DeviceId, command.Method);

            // 使用MCP架构处理IoT命令（基于xiaozhi-esp32设计）
            if (_mcpIntegrationService != null)
            {
                await HandleMcpIoTCommandAsync(command);
                return;
            }

            _logger?.LogWarning("MCP集成服务未设置，无法执行命令");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "执行MCP IoT命令失败: {DeviceId}.{Method}", 
                command.DeviceId, command.Method);
            
            // 发送错误结果
            await SendIoTCommandErrorResult(command, ex.Message);
        }
    }

    /// <summary>
    /// 使用MCP架构处理IoT命令（基于xiaozhi-esp32实现）
    /// </summary>
    private async Task HandleMcpIoTCommandAsync(IotCommandMessage command)
    {
        try
        {
            // 构建MCP工具名称（遵循MCP命名约定）
            var toolName = $"self.{command.DeviceId?.ToLowerInvariant()}.{command.Method?.ToLowerInvariant()}";
            
            // 执行MCP工具
            var result = await _mcpIntegrationService!.ExecuteFunctionAsync(toolName, command.Parameters);
            
            // 发送命令执行结果
            await SendIoTCommandSuccessResult(command, result);
            
            _logger?.LogInformation("MCP IoT命令执行完成: {ToolName}", toolName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "MCP IoT命令执行失败: {DeviceId}.{Method}", 
                command.DeviceId, command.Method);
            await SendIoTCommandErrorResult(command, ex.Message);
        }
    }

    /// <summary>
    /// 发送IoT命令成功结果
    /// </summary>
    private async Task SendIoTCommandSuccessResult(IotCommandMessage command, object? result)
    {
        if (_communicationClient is WebSocketClient webSocketClient && IsConnected)
        {
            var resultMessage = new IotCommandResultMessage
            {
                RequestId = command.RequestId,
                DeviceId = command.DeviceId,
                Method = command.Method,
                Success = true,
                Result = result,
                Error = null
            };

            var json = System.Text.Json.JsonSerializer.Serialize(resultMessage);
            await webSocketClient.SendTextAsync(json);
        }
    }

    /// <summary>
    /// 发送IoT命令错误结果
    /// </summary>
    private async Task SendIoTCommandErrorResult(IotCommandMessage command, string error)
    {
        if (_communicationClient is WebSocketClient webSocketClient && IsConnected)
        {
            var errorResult = new IotCommandResultMessage
            {
                RequestId = command.RequestId,
                DeviceId = command.DeviceId,
                Method = command.Method,
                Success = false,
                Error = error
            };

            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(errorResult);
                await webSocketClient.SendTextAsync(json);
            }
            catch (Exception sendEx)
            {
                _logger?.LogError(sendEx, "发送IoT命令错误结果失败");
            }
        }
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
        }        try
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
    }    /// <summary>
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
    }/// <summary>
    /// 关键词检测事件处理（对应py-xiaozhi的_on_wake_word_detected回调）
    /// </summary>
    private void OnKeywordDetected(object? sender, KeywordDetectedEventArgs e)
    {
        _logger?.LogInformation($"检测到关键词: {e.Keyword} (完整文本: {e.FullText})");

        // 在后台线程处理关键词检测事件（对应py-xiaozhi的_handle_wake_word_detected）
        Task.Run(async () => await HandleKeywordDetectedAsync(e.Keyword));
    }    /// <summary>
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
                    await StartVoiceChatAsync();
                    break;                
                
                case DeviceState.Speaking:
                    // 在AI说话时检测到关键词，中断对话（对应py-xiaozhi的中断逻辑）
                    _logger?.LogInformation("在AI说话时检测到关键词，中断当前对话");
                    
                    // 立即停止对话，然后短暂延迟以确保音频流同步
                    await StopVoiceChatAsync();
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
        CurrentState = DeviceState.Connecting;

        try
        {            // Initialize configuration service first
            if (!await _configurationService.InitializeMqttInfoAsync())
            {
                throw new InvalidOperationException("Failed to initialize MQTT configuration from OTA server");
            }            
              // 初始化音频编解码器 - 暂时使用Concentus进行测试
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
            }// 连接到服务器
            await _communicationClient.ConnectAsync();
            
            // 启动关键词唤醒检测（对应py-xiaozhi的_start_wake_word_detector调用）
            if (_keywordSpottingService != null)
            {
                _logger?.LogInformation("正在启动关键词唤醒检测...");
                await StartKeywordDetectionAsync();
            }
            
            CurrentState = DeviceState.Idle;

            _logger?.LogInformation("语音聊天服务初始化完成");
        }
        catch (Exception ex)
        {
            CurrentState = DeviceState.Idle;
            _logger?.LogError(ex, "初始化语音聊天服务失败");
            ErrorOccurred?.Invoke(this, $"初始化失败: {ex.Message}");
            throw;
        }
    }    
    /// <summary>
    /// Toggle chat state for auto conversation mode (equivalent to Python toggle_chat_state)
    /// </summary>
    public async Task ToggleChatStateAsync()
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
                        // This matches Python's behavior in toggle_chat_state_impl
                        await StartListeningAsync();
                    }
                    break;

                case DeviceState.Listening:
                    // Stop current listening session
                    await StopListeningAsync(AbortReason.UserInterruption);
                    break;

                case DeviceState.Speaking:
                    // Abort current speaking and potentially restart listening if in auto mode
                    // This matches Python's abort_speaking behavior
                    await StopSpeakingAsync();
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
    }
    /// <summary>
    /// Start listening mode
    /// </summary>
    private async Task StartListeningAsync()
    {
        if (CurrentState != DeviceState.Idle || !IsConnected) return;

        try
        {
            CurrentState = DeviceState.Listening;

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
            CurrentState = DeviceState.Idle;
            _logger?.LogError(ex, "Failed to start listening");
            ErrorOccurred?.Invoke(this, $"Failed to start listening: {ex.Message}");
        }
    }    
    /// <summary>
    /// Stop listening with specific reason
    /// </summary>
    private async Task StopListeningAsync(AbortReason reason = AbortReason.None)
    {
        if (CurrentState != DeviceState.Listening) return;

        try
        {
            _lastAbortReason = reason;

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
            CurrentState = DeviceState.Idle;

            _logger?.LogInformation("Stopped listening, reason: {Reason}", reason);

            // Auto restart listening if in always-on mode and not user interrupted
            if (KeepListening && reason != AbortReason.UserInterruption)
            {
                await Task.Delay(500); // Brief pause before restarting
                await StartListeningAsync();
            }
        }
        catch (Exception ex)
        {
            CurrentState = DeviceState.Idle;
            _logger?.LogError(ex, "Failed to stop listening");
            ErrorOccurred?.Invoke(this, $"Failed to stop listening: {ex.Message}");
        }
    }

    /// <summary>
    /// Start speaking mode
    /// </summary>
    private async Task StartSpeakingAsync()
    {
        if (CurrentState != DeviceState.Listening && CurrentState != DeviceState.Idle) return;

        try
        {
            // Stop any ongoing recording first
            if (CurrentState == DeviceState.Listening)
            {
                await StopListeningAsync(AbortReason.None);
            }

            CurrentState = DeviceState.Speaking;
            _logger?.LogInformation("Started speaking");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start speaking");
            ErrorOccurred?.Invoke(this, $"Failed to start speaking: {ex.Message}");
        }
    }    
    /// <summary>
    /// Stop speaking mode
    /// </summary>
    private async Task StopSpeakingAsync()
    {
        if (CurrentState != DeviceState.Speaking) return;

        try
        {
            if (_audioPlayer != null)
            {
                await _audioPlayer.StopAsync();
            }

            CurrentState = DeviceState.Idle;
            _logger?.LogInformation("Stopped speaking");

            // Auto restart listening if in keep listening mode - more sophisticated delay like Python
            if (KeepListening)
            {
                // Give audio system time to fully complete playback (similar to Python's delayed_state_change)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Wait a bit longer to ensure audio buffers are clear (like Python's audio queue wait)
                        await Task.Delay(500);
                        
                        // Only restart if we're still in idle state and keep listening is still enabled
                        if (CurrentState == DeviceState.Idle && KeepListening)
                        {
                            await StartListeningAsync();
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
            CurrentState = DeviceState.Idle;
            _logger?.LogError(ex, "Failed to stop speaking");
            ErrorOccurred?.Invoke(this, $"Failed to stop speaking: {ex.Message}");
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

    public async Task StartVoiceChatAsync()
    {
        await StartListeningAsync();
    }    
    
    public async Task StopVoiceChatAsync()
    {
        await StopListeningAsync(AbortReason.UserInterruption);
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

            // 根据当前状态执行相应的本地停止操作
            switch (CurrentState)
            {
                case DeviceState.Listening:
                    await StopListeningAsync(reason);
                    break;
                case DeviceState.Speaking:
                    await StopSpeakingAsync();
                    break;
                default:
                    _logger?.LogDebug("Interrupt called but device is in {State} state", CurrentState);
                    break;
            }

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
    }    private async void OnAudioDataReceived(object? sender, byte[] audioData)
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
            if (CurrentState == DeviceState.Speaking)
            {
                await StopSpeakingAsync();
            }
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
                // 如果不在说话状态，切换到说话状态
                if (CurrentState != DeviceState.Speaking)
                {
                    await StartSpeakingAsync();
                }                if (_audioPlayer != null && _audioCodec != null && _config != null)
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
            CurrentState = DeviceState.Idle;
              // Send IoT device descriptors and initial states when connection is established
            // This matches the py-xiaozhi behavior in _on_audio_channel_opened
            //_ = Task.Run(async () =>
            //{
            //    try
            //    {
            //        if (_iotDeviceManager != null && _communicationClient is WebSocketClient webSocketClient)
            //        {
            //            _logger?.LogInformation("发送IoT设备描述符和初始状态");
                        
            //            // Send IoT device descriptors (similar to py-xiaozhi's send_iot_descriptors)
            //            var descriptorsJson = _iotDeviceManager.GetDescriptorsJson();
            //            var descriptors = JsonSerializer.Deserialize<object>(descriptorsJson);
            //            if (descriptors != null)
            //            {
            //                await webSocketClient.SendIotDescriptorsAsync(descriptors);
            //            }
                        
            //            // Send initial IoT device states (similar to py-xiaozhi's _update_iot_states(False))
            //            var statesJson = _iotDeviceManager.GetStatesJson();
            //            var states = JsonSerializer.Deserialize<object>(statesJson);
            //            if (states != null)
            //            {
            //                await webSocketClient.SendIotStatesAsync(states);
            //            }
                        
            //            _logger?.LogInformation("IoT设备描述符和状态发送完成");
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        _logger?.LogError(ex, "发送IoT设备信息失败");
            //    }
            //});
        }
        else
        {
            CurrentState = DeviceState.Connecting;

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
                    if (CurrentState == DeviceState.Listening)
                    {
                        await StartSpeakingAsync();
                    }
                    break;

                case "stop":
                    // TTS停止播放时，从说话状态切换回空闲或监听状态
                    if (CurrentState == DeviceState.Speaking)
                    {
                        await StopSpeakingAsync();
                    }
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
                // 如果不在说话状态，切换到说话状态
                if (CurrentState != DeviceState.Speaking)
                {
                    await StartSpeakingAsync();
                }                
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
            }
            
            // 7. 释放MCP集成服务
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
            
            // 8. 重置状态
            _isVoiceChatActive = false;
            CurrentState = DeviceState.Idle;
            _lastAbortReason = AbortReason.None;
            
            _logger?.LogInformation("VoiceChatService资源释放完成");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "释放VoiceChatService资源时发生严重错误");
        }
    }
}
