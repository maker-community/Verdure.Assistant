using Microsoft.Extensions.Logging;
using XiaoZhi.Core.Constants;
using XiaoZhi.Core.Interfaces;
using XiaoZhi.Core.Models;

namespace XiaoZhi.Core.Services;

/// <summary>
/// 语音聊天服务实现
/// </summary>
public class VoiceChatService : IVoiceChatService
{
    private readonly ILogger<VoiceChatService>? _logger;
    private readonly IConfigurationService _configurationService;
    private ICommunicationClient? _communicationClient;
    private IAudioRecorder? _audioRecorder;
    private IAudioPlayer? _audioPlayer;
    private IAudioCodec? _audioCodec;
    private XiaoZhiConfig? _config;
    private bool _isVoiceChatActive;
    private string _sessionId = Guid.NewGuid().ToString();

    // Device state management
    private DeviceState _currentState = DeviceState.Idle;
    private ListeningMode _listeningMode = ListeningMode.Manual;
    private bool _keepListening = false;
    private AbortReason _lastAbortReason = AbortReason.None;

    public event EventHandler<bool>? VoiceChatStateChanged;
    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<string>? ErrorOccurred;

    // Device state events
    public event EventHandler<DeviceState>? DeviceStateChanged;
    public event EventHandler<ListeningMode>? ListeningModeChanged;

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
                DeviceStateChanged?.Invoke(this, value);
            }
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

    public VoiceChatService(IConfigurationService configurationService, ILogger<VoiceChatService>? logger = null)
    {
        _configurationService = configurationService;
        _logger = logger;
    }

    public async Task InitializeAsync(XiaoZhiConfig config)
    {
        _config = config;
        CurrentState = DeviceState.Connecting;

        try
        {
            // Initialize configuration service first
            if (!await _configurationService.InitializeMqttInfoAsync())
            {
                throw new InvalidOperationException("Failed to initialize MQTT configuration from OTA server");
            }            // 初始化音频编解码器
            _audioCodec = new OpusAudioCodec();

            // 初始化音频录制和播放
            if (config.EnableVoice)
            {
                _audioRecorder = new PortAudioRecorder();
                _audioPlayer = new PortAudioPlayer();

                _audioRecorder.DataAvailable += OnAudioDataReceived;
            }            // 初始化通信客户端
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

            // 连接到服务器
            await _communicationClient.ConnectAsync();
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
            switch (CurrentState)
            {
                case DeviceState.Idle:
                    if (KeepListening)
                    {
                        await StartListeningAsync();
                    }
                    break;

                case DeviceState.Listening:
                    await StopListeningAsync(AbortReason.UserInterruption);
                    break;

                case DeviceState.Speaking:
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

            // Auto restart listening if in keep listening mode
            if (KeepListening)
            {
                await Task.Delay(200); // Brief pause before restarting
                await StartListeningAsync();
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

    private async void OnMessageReceived(object? sender, ChatMessage message)
    {
        try
        {
            MessageReceived?.Invoke(this, message);

            // Start speaking mode when receiving audio response
            if (message.AudioData != null)
            {
                await StartSpeakingAsync();

                if (_audioPlayer != null && _audioCodec != null && _config != null)
                {
                    var pcmData = _audioCodec.Decode(message.AudioData, _config.AudioSampleRate, _config.AudioChannels);
                    await _audioPlayer.PlayAsync(pcmData, _config.AudioSampleRate, _config.AudioChannels);
                }

                // Finish speaking and potentially restart listening
                await StopSpeakingAsync();
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

    public void Dispose()
    {
        StopVoiceChatAsync().Wait();
        _communicationClient?.Dispose();
        (_audioRecorder as IDisposable)?.Dispose();
        (_audioPlayer as IDisposable)?.Dispose();
        (_audioCodec as IDisposable)?.Dispose();
    }
}
