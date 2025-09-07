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
    // Protocol message events
    event EventHandler<MusicMessage>? MusicMessageReceived;
    event EventHandler<SystemStatusMessage>? SystemStatusMessageReceived;
    event EventHandler<LlmMessage>? LlmMessageReceived;
    event EventHandler<TtsMessage>? TtsStateChanged;
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
    /// 触发API打断 - 通过增强的打断管理器
    /// </summary>
    /// <param name="endpoint">API端点</param>
    /// <param name="requestData">请求数据</param>
    void TriggerApiInterrupt(string endpoint, object? requestData = null);

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
    ListeningMode CurrentListeningMode { get; }    
    
    /// <summary>
    /// 获取对话状态机，用于直接状态事件订阅
    /// </summary>
    ConversationStateMachine? StateMachine { get; }
    
    /// <summary>
    /// 触发手动打断
    /// </summary>
    Task TriggerManualInterruptAsync(string description, object? data = null);
    
    /// <summary>
    /// 启用或禁用VAD检测
    /// </summary>
    Task SetVADEnabledAsync(bool enabled);
    
    /// <summary>
    /// 启用或禁用热键检测
    /// </summary>
    Task SetHotkeyEnabledAsync(bool enabled);
    
    /// <summary>
    /// 检查VAD是否应该激活（仅在音乐播放时）
    /// </summary>
    bool ShouldVadBeActive();

    /// <summary>
    /// 启动关键词唤醒检测
    /// 对应py-xiaozhi的_start_wake_word_detector方法
    /// </summary>
    Task<bool> StartKeywordDetectionAsync();    
    
    /// <summary>
    /// 停止关键词唤醒检测
    /// </summary>
    Task StopKeywordDetectionAsync();    
    
    /// <summary>
    /// 关键词唤醒是否启用
    /// </summary>
    bool IsKeywordDetectionEnabled { get; }    
    
    /// <summary>
    /// 切换关键词模型
    /// </summary>
    /// <param name="modelFileName">模型文件名</param>
    /// <returns>切换是否成功</returns>
    Task<bool> SwitchKeywordModelAsync(string modelFileName);
}
