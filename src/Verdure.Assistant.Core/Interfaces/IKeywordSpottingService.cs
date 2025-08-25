using System;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Interfaces;

/// <summary>
/// 关键词唤醒服务接口 - 基于Microsoft认知服务的离线关键词检测
/// 参考py-xiaozhi的WakeWordDetector实现，提供相同的功能和行为模式
/// </summary>
public interface IKeywordSpottingService : IDisposable
{
    /// <summary>
    /// 关键词检测到事件
    /// </summary>
    event EventHandler<KeywordDetectedEventArgs>? KeywordDetected;

    /// <summary>
    /// 检测错误事件
    /// </summary>
    event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// 是否正在运行
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 是否已暂停
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// 是否已启用（对应py-xiaozhi的enabled属性）
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 启动关键词检测
    /// 对应py-xiaozhi的start()方法，支持多种启动模式
    /// </summary>
    /// <param name="audioRecorder">可选的音频录制器，如果提供则使用外部音频流</param>
    /// <returns>启动是否成功</returns>
    Task<bool> StartAsync(IAudioRecorder? audioRecorder = null);    
    /// <summary>
    /// 停止关键词检测
    /// 对应py-xiaozhi的stop()方法
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// 暂停检测（对应py-xiaozhi的pause()方法）
    /// 在用户说话或AI回应时调用，避免误触发
    /// </summary>
    void Pause();

    /// <summary>
    /// 恢复检测（对应py-xiaozhi的resume()方法）
    /// 在Idle状态或Speaking结束后调用
    /// </summary>
    void Resume();

    /// <summary>
    /// 更新使用的音频流（对应py-xiaozhi的update_stream()方法）
    /// </summary>
    /// <param name="audioRecorder">新的音频录制器</param>
    /// <returns>更新是否成功</returns>
    bool UpdateAudioSource(IAudioRecorder audioRecorder);

    /// <summary>
    /// 设置配置信息
    /// </summary>
    /// <param name="config">配置对象</param>
    void SetConfig(VerdureConfig config);

    /// <summary>
    /// 切换关键词模型
    /// </summary>
    /// <param name="modelFileName">模型文件名</param>
    /// <returns>切换是否成功</returns>
    Task<bool> SwitchKeywordModelAsync(string modelFileName);
}

/// <summary>
/// 关键词检测事件参数
/// </summary>
public class KeywordDetectedEventArgs : EventArgs
{
    /// <summary>
    /// 检测到的关键词
    /// </summary>
    public string Keyword { get; set; } = string.Empty;

    /// <summary>
    /// 完整的识别文本
    /// </summary>
    public string FullText { get; set; } = string.Empty;

    /// <summary>
    /// 检测置信度
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// 使用的模型名称
    /// </summary>
    public string ModelName { get; set; } = string.Empty;
}
