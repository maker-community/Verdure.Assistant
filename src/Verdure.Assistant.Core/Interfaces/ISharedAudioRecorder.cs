using PortAudioSharp;

namespace Verdure.Assistant.Core.Interfaces;

/// <summary>
/// 共享音频录制接口 - 扩展基础音频录制接口，支持多订阅者模式
/// 参考 py-xiaozhi 的 AudioCodec 共享流设计
/// </summary>
public interface ISharedAudioRecorder : IAudioRecorder
{
    /// <summary>
    /// 订阅音频数据流 - 支持多个组件同时接收音频数据
    /// </summary>
    /// <param name="handler">音频数据处理器</param>
    void SubscribeToAudioData(EventHandler<byte[]> handler);

    /// <summary>
    /// 取消订阅音频数据流
    /// </summary>
    /// <param name="handler">音频数据处理器</param>
    void UnsubscribeFromAudioData(EventHandler<byte[]> handler);

    /// <summary>
    /// 获取共享的音频输入流实例
    /// 主要用于高级集成场景（如关键词检测服务的直接集成）
    /// </summary>
    /// <returns>共享的 PortAudio 输入流，如果未初始化则返回 null</returns>
    PortAudioSharp.Stream? GetSharedInputStream();

    /// <summary>
    /// 强制清理音频系统资源
    /// 用于全局异常恢复和紧急资源释放场景
    /// </summary>
    void ForceCleanup();

    /// <summary>
    /// 音频录制停止事件 - 通知所有订阅者录制已停止
    /// </summary>
    event EventHandler? RecordingStopped;
}