using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using XiaoZhi.Core.Interfaces;
using XiaoZhi.Core.Models;

namespace XiaoZhi.Core.Services;

/// <summary>
/// WebSocket通信客户端实现
/// </summary>
public class WebSocketClient : ICommunicationClient
{
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly string? _serverUrl;
    private readonly IConfigurationService _configurationService;
    private bool _isConnected;

    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<bool>? ConnectionStateChanged;

    public bool IsConnected => _isConnected;

    public WebSocketClient(IConfigurationService service)
    {
        _configurationService = service;
    }

    public async Task ConnectAsync()
    {
        if (_isConnected) return;

        try
        {
            _webSocket = new ClientWebSocket();
            // 初始化 WebSocket
            _webSocket.Options.SetRequestHeader("Authorization", "Bearer " + "test");
            _webSocket.Options.SetRequestHeader("Protocol-Version", "1");
            _webSocket.Options.SetRequestHeader("Device-Id", _configurationService.DeviceId);
            _webSocket.Options.SetRequestHeader("Client-Id", Guid.NewGuid().ToString());
            _cancellationTokenSource = new CancellationTokenSource();

            await _webSocket.ConnectAsync(new Uri(_configurationService.WebSocketUrl), _cancellationTokenSource.Token);
            _isConnected = true;
            ConnectionStateChanged?.Invoke(this, true);

            // 开始接收消息
            _ = Task.Run(ReceiveMessagesAsync);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            ConnectionStateChanged?.Invoke(this, false);
            throw new Exception($"连接WebSocket失败: {ex.Message}", ex);
        }
    }

    public async Task DisconnectAsync()
    {
        if (!_isConnected) return;

        try
        {
            _cancellationTokenSource?.Cancel();
            
            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"断开WebSocket连接时出错: {ex.Message}");
        }
        finally
        {
            _isConnected = false;
            ConnectionStateChanged?.Invoke(this, false);
            _webSocket?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }

    public async Task SendMessageAsync(ChatMessage message)
    {
        if (!_isConnected || _webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket未连接");

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public async Task SendVoiceAsync(VoiceMessage voiceMessage)
    {
        if (!_isConnected || _webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket未连接");

        var json = JsonSerializer.Serialize(voiceMessage);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task ReceiveMessagesAsync()
    {
        var buffer = new byte[8192];

        try
        {
            while (_isConnected && _webSocket?.State == WebSocketState.Open && !_cancellationTokenSource!.Token.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    
                    try
                    {
                        var message = JsonSerializer.Deserialize<ChatMessage>(json);
                        if (message != null)
                        {
                            MessageReceived?.Invoke(this, message);
                        }
                    }
                    catch (JsonException ex)
                    {
                        System.Console.WriteLine($"解析JSON消息失败: {ex.Message}");
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消操作
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"接收WebSocket消息时出错: {ex.Message}");
        }
        finally
        {
            if (_isConnected)
            {
                _isConnected = false;
                ConnectionStateChanged?.Invoke(this, false);
            }
        }
    }

    public void Dispose()
    {
        DisconnectAsync().Wait();
    }
}
