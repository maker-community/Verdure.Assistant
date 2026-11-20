using System.Text.Json.Serialization;

namespace Verdure.Assistant.Core.Models
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
        /// 客户端ID（首次运行后持久化，保证多次启动不变，避免服务端重复要求绑定）
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

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

        #region DeepSeek

        /// <summary>
        /// DeepSeek API 地址（API Key 不在此处保存，而是通过 <see cref="Interfaces.ISecretStore"/> 存入系统凭据库）
        /// </summary>
        public string DeepSeekBaseUrl { get; set; } = "https://api.deepseek.com";

        /// <summary>
        /// DeepSeek 模型：deepseek-flash / deepseek-v4-pro
        /// </summary>
        public string DeepSeekModel { get; set; } = "deepseek-flash";

        /// <summary>
        /// 是否开启思考模式
        /// </summary>
        public bool DeepSeekThinkingEnabled { get; set; } = true;

        /// <summary>
        /// 思考强度：low / high / max
        /// </summary>
        public string DeepSeekReasoningEffort { get; set; } = "high";

        /// <summary>
        /// 自定义系统提示词；为空时使用内置的"绿荫助手"提示词
        /// </summary>
        public string DeepSeekSystemPrompt { get; set; } = string.Empty;

        /// <summary>
        /// 是否启用 DeepSeek 服务端联网搜索
        /// </summary>
        public bool DeepSeekEnableWebSearch { get; set; } = false;

        /// <summary>
        /// 是否把本地 MCP 工具（音乐、设备控制等）暴露给 DeepSeek 调用
        /// </summary>
        public bool DeepSeekEnableTools { get; set; } = true;

        /// <summary>
        /// 收到完整回复后是否朗读
        /// </summary>
        public bool DeepSeekSpeakReplies { get; set; } = false;

        /// <summary>
        /// 语音对话后端：xiaozhi（小智服务器）或 deepseek（本地麦克风 → DeepSeek 语音识别 → DeepSeek 对话 → DeepSeek 语音合成）。
        /// 唤醒词、连续对话与按住说话都按该后端执行。
        /// </summary>
        public string VoiceBackend { get; set; } = "xiaozhi";

        /// <summary>
        /// DeepSeek 语音合成音色：stella（女声）/ tide（男声）
        /// </summary>
        public string DeepSeekVoiceId { get; set; } = "stella";

        /// <summary>
        /// 语音对话时是否开启思考模式。思考会让首个回答字晚几秒出现，语音场景默认关闭以获得更快的回复。
        /// </summary>
        public bool DeepSeekVoiceThinking { get; set; } = false;

        /// <summary>
        /// 说完话后停顿多久（毫秒）才认为一句话结束并开始识别。越小回复越快，但说话中间的停顿可能被切断。
        /// </summary>
        public int DeepSeekVoiceEndSilenceMs { get; set; } = 700;

        /// <summary>
        /// 环境噪声级别：0 安静、1 普通、2 嘈杂。越嘈杂，判定"有人在说话"所需的音量越高，
        /// 能减少键盘声/旁人说话把一句话越拖越长，但音量小的话可能被忽略。
        /// </summary>
        public int DeepSeekVoiceNoiseLevel { get; set; } = 1;

        #endregion

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
                ClientId = ClientId,
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
                AudioCodec = AudioCodec,
                DeepSeekBaseUrl = DeepSeekBaseUrl,
                DeepSeekModel = DeepSeekModel,
                DeepSeekThinkingEnabled = DeepSeekThinkingEnabled,
                DeepSeekReasoningEffort = DeepSeekReasoningEffort,
                DeepSeekSystemPrompt = DeepSeekSystemPrompt,
                DeepSeekEnableWebSearch = DeepSeekEnableWebSearch,
                DeepSeekEnableTools = DeepSeekEnableTools,
                DeepSeekSpeakReplies = DeepSeekSpeakReplies,
                VoiceBackend = VoiceBackend,
                DeepSeekVoiceId = DeepSeekVoiceId,
                DeepSeekVoiceThinking = DeepSeekVoiceThinking,
                DeepSeekVoiceEndSilenceMs = DeepSeekVoiceEndSilenceMs,
                DeepSeekVoiceNoiseLevel = DeepSeekVoiceNoiseLevel
            };
        }
    }
}
