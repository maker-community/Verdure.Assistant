using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Interfaces;

/// <summary>
/// 通信客户端接口
/// </summary>
public interface ICommunicationClient : IDisposable
{
    /// <summary>
    /// 连接到服务器
    /// </summary>
    Task ConnectAsync();

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// 发送消息
    /// </summary>
    /// <param name="message">消息</param>
    Task SendMessageAsync(ChatMessage message);

    /// <summary>
    /// 发送语音数据
    /// </summary>
    /// <param name="voiceMessage">语音消息</param>
    Task SendVoiceAsync(VoiceMessage voiceMessage);

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }
}
