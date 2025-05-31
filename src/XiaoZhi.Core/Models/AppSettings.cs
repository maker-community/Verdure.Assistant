using System.Text.Json.Serialization;

namespace XiaoZhi.Core.Models
{
    /// <summary>
    /// 应用设置模型
    /// 包含所有可配置的应用设置项
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// 唤醒词功能是否启用
        /// </summary>
        public bool WakeWordEnabled { get; set; } = false;

        /// <summary>
        /// 唤醒词列表（逗号分隔）
        /// </summary>
        public string WakeWords { get; set; } = "XiaoZhi,Hello XiaoZhi";

        /// <summary>
        /// 设备ID
        /// </summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>
        /// WebSocket服务器地址
        /// </summary>
        public string WsAddress { get; set; } = "ws://localhost:8765";

        /// <summary>
        /// WebSocket认证令牌
        /// </summary>
        public string WsToken { get; set; } = string.Empty;

        /// <summary>
        /// OTA服务器地址
        /// </summary>
        public string OtaAddress { get; set; } = "api.tenclass.net/xiaozhi/ota/";

        /// <summary>
        /// OTA协议（http/https）
        /// </summary>
        public string OtaProtocol { get; set; } = "https://";

        /// <summary>
        /// WebSocket协议（ws/wss）
        /// </summary>
        public string WsProtocol { get; set; } = "wss://";

        /// <summary>
        /// 默认音量（0-100）
        /// </summary>
        public double DefaultVolume { get; set; } = 80.0;

        /// <summary>
        /// 自动调节音量
        /// </summary>
        public bool AutoAdjustVolume { get; set; } = true;

        /// <summary>
        /// 音频输入设备
        /// </summary>
        public string AudioInputDevice { get; set; } = string.Empty;

        /// <summary>
        /// 音频输出设备
        /// </summary>
        public string AudioOutputDevice { get; set; } = string.Empty;

        /// <summary>
        /// 开机自启动
        /// </summary>
        public bool AutoStart { get; set; } = false;

        /// <summary>
        /// 最小化到托盘
        /// </summary>
        public bool MinimizeToTray { get; set; } = true;

        /// <summary>
        /// 启用日志记录
        /// </summary>
        public bool EnableLogging { get; set; } = true;

        /// <summary>
        /// 主题设置
        /// </summary>
        public string Theme { get; set; } = "Default";

        /// <summary>
        /// 连接超时时间（秒）
        /// </summary>
        public double ConnectionTimeout { get; set; } = 30.0;

        /// <summary>
        /// 音频采样率
        /// </summary>
        public double AudioSampleRate { get; set; } = 16000.0;

        /// <summary>
        /// 音频声道数
        /// </summary>
        public double AudioChannels { get; set; } = 1.0;

        /// <summary>
        /// 音频编解码器
        /// </summary>
        public string AudioCodec { get; set; } = "Opus";

        /// <summary>
        /// 创建默认设置实例
        /// </summary>
        /// <returns>包含默认值的设置实例</returns>
        public static AppSettings CreateDefault()
        {
            return new AppSettings();
        }

        /// <summary>
        /// 验证设置的有效性
        /// </summary>
        /// <returns>验证结果</returns>
        public bool IsValid()
        {
            return DefaultVolume >= 0 && DefaultVolume <= 100 &&
                   ConnectionTimeout >= 5 && ConnectionTimeout <= 60 &&
                   AudioSampleRate >= 8000 && AudioSampleRate <= 48000 &&
                   AudioChannels >= 1 && AudioChannels <= 2;
        }

        /// <summary>
        /// 创建设置的深拷贝
        /// </summary>
        /// <returns>设置的副本</returns>
        public AppSettings Clone()
        {
            return new AppSettings
            {
                WakeWordEnabled = WakeWordEnabled,
                WakeWords = WakeWords,
                DeviceId = DeviceId,
                WsAddress = WsAddress,
                WsToken = WsToken,
                OtaAddress = OtaAddress,
                OtaProtocol = OtaProtocol,
                WsProtocol = WsProtocol,
                DefaultVolume = DefaultVolume,
                AutoAdjustVolume = AutoAdjustVolume,
                AudioInputDevice = AudioInputDevice,
                AudioOutputDevice = AudioOutputDevice,
                AutoStart = AutoStart,
                MinimizeToTray = MinimizeToTray,
                EnableLogging = EnableLogging,
                Theme = Theme,
                ConnectionTimeout = ConnectionTimeout,
                AudioSampleRate = AudioSampleRate,
                AudioChannels = AudioChannels,
                AudioCodec = AudioCodec
            };
        }
    }
}
