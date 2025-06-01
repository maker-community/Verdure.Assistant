using System;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Services;

namespace Verdure.Assistant.Core.Interfaces;

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
    /// 设备状态变化事件
    /// </summary>
    event EventHandler<DeviceState>? DeviceStateChanged;

    /// <summary>
    /// 监听模式变化事件
    /// </summary>
    event EventHandler<ListeningMode>? ListeningModeChanged;

    /// <summary>
    /// 初始化服务
    /// </summary>
    /// <param name="config">配置</param>
    Task InitializeAsync(VerdureConfig config);

    /// <summary>
    /// 开始语音对话
    /// </summary>
    Task StartVoiceChatAsync();    
    /// <summary>
    /// 停止语音对话
    /// </summary>
    Task StopVoiceChatAsync();

    /// <summary>
    /// 打断当前对话 - 发送打断消息到服务器
    /// </summary>
    /// <param name="reason">打断原因</param>
    Task InterruptAsync(AbortReason reason = AbortReason.UserInterruption);

    /// <summary>
    /// 发送文本消息
    /// </summary>
    /// <param name="text">文本内容</param>
    Task SendTextMessageAsync(string text);

    /// <summary>
    /// 切换对话状态 (auto conversation mode)
    /// </summary>
    Task ToggleChatStateAsync();

    /// <summary>
    /// 是否正在语音对话
    /// </summary>
    bool IsVoiceChatActive { get; }

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 自动对话模式 - 持续监听
    /// </summary>
    bool KeepListening { get; set; }

    /// <summary>
    /// 当前设备状态
    /// </summary>
    DeviceState CurrentState { get; }    
    /// <summary>
    /// 当前监听模式
    /// </summary>
    ListeningMode CurrentListeningMode { get; }    /// <summary>
    /// Set interrupt manager for wake word detector coordination
    /// This enables py-xiaozhi-like wake word detector pause/resume behavior
    /// </summary>
    void SetInterruptManager(InterruptManager interruptManager);

    /// <summary>
    /// 设置关键词唤醒服务
    /// 对应py-xiaozhi的wake_word_detector集成
    /// </summary>
    void SetKeywordSpottingService(IKeywordSpottingService keywordSpottingService);

    /// <summary>
    /// 启动关键词唤醒检测
    /// 对应py-xiaozhi的_start_wake_word_detector方法
    /// </summary>
    Task<bool> StartKeywordDetectionAsync();

    /// <summary>
    /// 停止关键词唤醒检测
    /// </summary>
    void StopKeywordDetection();

    /// <summary>
    /// 关键词唤醒是否启用
    /// </summary>
    bool IsKeywordDetectionEnabled { get; }
}
