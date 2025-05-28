using Microsoft.Extensions.Logging;
using XiaoZhi.Core.Interfaces;
using XiaoZhi.Core.Models;

namespace XiaoZhi.Core.Services;

/// <summary>
/// 语音聊天服务实现
/// </summary>
public class VoiceChatService : IVoiceChatService
{
    private readonly ILogger<VoiceChatService>? _logger;
    private ICommunicationClient? _communicationClient;
    private IAudioRecorder? _audioRecorder;
    private IAudioPlayer? _audioPlayer;
    private IAudioCodec? _audioCodec;
    private XiaoZhiConfig? _config;
    private bool _isVoiceChatActive;
    private string _sessionId = Guid.NewGuid().ToString();

    public event EventHandler<bool>? VoiceChatStateChanged;
    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsVoiceChatActive => _isVoiceChatActive;
    public bool IsConnected => _communicationClient?.IsConnected ?? false;

    public VoiceChatService(ILogger<VoiceChatService>? logger = null)
    {
        _logger = logger;
    }

    public async Task InitializeAsync(XiaoZhiConfig config)
    {
        _config = config;

        try
        {
            // 初始化音频编解码器
            _audioCodec = new OpusAudioCodec();            
            // 初始化音频录制和播放
            if (config.EnableVoice)
            {
                _audioRecorder = new PortAudioRecorder();
                _audioPlayer = new PortAudioPlayer();
                
                _audioRecorder.DataAvailable += OnAudioDataReceived;
            }

            // 初始化通信客户端
            if (config.UseWebSocket)
            {
                _communicationClient = new WebSocketClient(config.ServerUrl);
            }            
            else
            {
                _communicationClient = new MqttNetClient(
                    config.MqttBroker, 
                    config.MqttPort, 
                    config.MqttClientId, 
                    config.MqttTopic);
            }

            _communicationClient.MessageReceived += OnMessageReceived;
            _communicationClient.ConnectionStateChanged += OnConnectionStateChanged;

            // 连接到服务器
            await _communicationClient.ConnectAsync();
            
            _logger?.LogInformation("语音聊天服务初始化完成");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "初始化语音聊天服务失败");
            ErrorOccurred?.Invoke(this, $"初始化失败: {ex.Message}");
            throw;
        }
    }

    public async Task StartVoiceChatAsync()
    {
        if (_isVoiceChatActive || !IsConnected) return;

        try
        {
            if (_config?.EnableVoice == true && _audioRecorder != null)
            {
                await _audioRecorder.StartRecordingAsync(_config.AudioSampleRate, _config.AudioChannels);
                _isVoiceChatActive = true;
                VoiceChatStateChanged?.Invoke(this, true);
                _logger?.LogInformation("语音对话已开始");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "开始语音对话失败");
            ErrorOccurred?.Invoke(this, $"开始语音对话失败: {ex.Message}");
        }
    }

    public async Task StopVoiceChatAsync()
    {
        if (!_isVoiceChatActive) return;

        try
        {
            if (_audioRecorder != null)
            {
                await _audioRecorder.StopRecordingAsync();
            }
            
            if (_audioPlayer != null)
            {
                await _audioPlayer.StopAsync();
            }

            _isVoiceChatActive = false;
            VoiceChatStateChanged?.Invoke(this, false);
            _logger?.LogInformation("语音对话已停止");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止语音对话失败");
            ErrorOccurred?.Invoke(this, $"停止语音对话失败: {ex.Message}");
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

    private async void OnMessageReceived(object? sender, ChatMessage message)
    {
        try
        {
            MessageReceived?.Invoke(this, message);

            // 如果收到音频回复，播放音频
            if (message.AudioData != null && _audioPlayer != null && _audioCodec != null && _config != null)
            {
                var pcmData = _audioCodec.Decode(message.AudioData, _config.AudioSampleRate, _config.AudioChannels);
                await _audioPlayer.PlayAsync(pcmData, _config.AudioSampleRate, _config.AudioChannels);
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
        
        if (!isConnected && _isVoiceChatActive)
        {
            // 连接断开时自动停止语音对话
            _ = Task.Run(StopVoiceChatAsync);
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
