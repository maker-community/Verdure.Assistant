using System;
using XiaoZhi.Core.Models;

namespace XiaoZhi.Core.Interfaces;

/// <summary>
/// 语音聊天服务接口
/// </summary>
public interface IVoiceChatService : IDisposable
{
    /// <summary>
    /// 语音对话状态变化事件
    /// </summary>
    event EventHandler<bool>? VoiceChatStateChanged;

    /// <summary>
    /// 消息接收事件
    /// </summary>
    event EventHandler<ChatMessage>? MessageReceived;

    /// <summary>
    /// 错误事件
    /// </summary>
    event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// 初始化服务
    /// </summary>
    /// <param name="config">配置</param>
    Task InitializeAsync(XiaoZhiConfig config);

    /// <summary>
    /// 开始语音对话
    /// </summary>
    Task StartVoiceChatAsync();

    /// <summary>
    /// 停止语音对话
    /// </summary>
    Task StopVoiceChatAsync();

    /// <summary>
    /// 发送文本消息
    /// </summary>
    /// <param name="text">文本内容</param>
    Task SendTextMessageAsync(string text);

    /// <summary>
    /// 是否正在语音对话
    /// </summary>
    bool IsVoiceChatActive { get; }

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }
}
