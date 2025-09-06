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
    /// 强制清理音频系统资源
    /// 用于全局异常恢复和紧急资源释放场景
    /// </summary>
    void ForceCleanup();

    /// <summary>
    /// 音频录制停止事件 - 通知所有订阅者录制已停止
    /// </summary>
    event EventHandler? RecordingStopped;
}