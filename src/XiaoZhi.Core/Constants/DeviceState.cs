namespace XiaoZhi.Core.Constants
{
    /// <summary>
    /// 设备状态枚举，对应Python中的DeviceState
    /// </summary>
    public enum DeviceState
    {
        /// <summary>
        /// 空闲状态，等待唤醒词或按钮触发
        /// </summary>
        Idle,

        /// <summary>
        /// 连接中状态，正在连接服务器
        /// </summary>
        Connecting,

        /// <summary>
        /// 监听状态，正在聆听用户语音
        /// </summary>
        Listening,

        /// <summary>
        /// 说话状态，正在播放语音回复
        /// </summary>
        Speaking
    }

    /// <summary>
    /// 监听模式枚举，对应Python中的ListeningMode
    /// </summary>
    public enum ListeningMode
    {
        /// <summary>
        /// 始终开启
        /// </summary>
        AlwaysOn,

        /// <summary>
        /// 自动停止
        /// </summary>
        AutoStop,

        /// <summary>
        /// 手动控制
        /// </summary>
        Manual
    }    

    /// <summary>
    /// 中止原因枚举，对应Python中的AbortReason
    /// </summary>
    public enum AbortReason
    {
        /// <summary>
        /// 无中止
        /// </summary>
        None,

        /// <summary>
        /// 唤醒词检测到
        /// </summary>
        WakeWordDetected,

        /// <summary>
        /// 用户中断（手动点击按钮）
        /// </summary>
        UserInterruption,

        /// <summary>
        /// 语音中断（VAD检测到用户说话）
        /// </summary>
        VoiceInterruption,

        /// <summary>
        /// 键盘中断（F3快捷键）
        /// </summary>
        KeyboardInterruption,

        /// <summary>
        /// 系统错误导致的中断
        /// </summary>
        SystemError,

        /// <summary>
        /// 网络连接中断
        /// </summary>
        NetworkError,

        /// <summary>
        /// 音频设备错误
        /// </summary>
        AudioDeviceError
    }
}
