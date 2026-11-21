namespace Verdure.Assistant.Core.Models;

/// <summary>
/// 配置模型
/// </summary>
public class VerdureConfig
{
    public string ServerUrl { get; set; } = "wss://api.tenclass.net/xiaozhi/v1/";
    public string MqttBroker { get; set; } = "localhost";
    public int MqttPort { get; set; } = 1883;
    public string MqttClientId { get; set; } = "xiaozhi_client";
    public string MqttTopic { get; set; } = "xiaozhi/chat";
    public bool UseWebSocket { get; set; } = true;    
    public bool EnableVoice { get; set; } = true;

    /// <summary>
    /// 是否启用本地关键词唤醒（Azure Speech 的 KWS，需要 ModelFiles 里的 .table 模型）。
    /// 用机器人自带的唤醒词时应当关掉，否则每次启动都会因为缺模型文件报两条错。
    /// </summary>
    public bool EnableLocalKeywordSpotting { get; set; } = true;
    public int AudioSampleRate { get; set; } = 16000; // INPUT_SAMPLE_RATE 匹配Python
    public int AudioOutputSampleRate { get; set; } = 16000; // OUTPUT_SAMPLE_RATE 匹配Python  
    public int AudioChannels { get; set; } = 1; // CHANNELS 匹配Python
    public string AudioFormat { get; set; } = "opus";
    public bool EnableTemperatureSensor { get; set; } = false;
    public string TemperatureSensorPin { get; set; } = "A0";
    
    /// <summary>
    /// 启动时是否自动连接到语音助手服务
    /// </summary>
    public bool AutoConnect { get; set; } = false;

    /// <summary>
    /// 说话（TTS 播放）期间是否保持唤醒词识别，以便用唤醒词打断。
    /// 扬声器外放且回声明显时可关闭，避免 AI 自己念出唤醒词时误触发。
    /// </summary>
    public bool AllowKeywordInterruptWhileSpeaking { get; set; } = true;
    
    /// <summary>
    /// 关键词模型配置
    /// </summary>
    public KeywordModelConfig KeywordModels { get; set; } = new KeywordModelConfig();
}

/// <summary>
/// 关键词模型配置
/// </summary>
public class KeywordModelConfig
{
    /// <summary>
    /// 关键词模型文件目录路径，如果为空则使用默认路径
    /// </summary>
    public string? ModelsPath { get; set; }
    
    /// <summary>
    /// 当前使用的关键词模型文件名（不含路径）
    /// </summary>
    public string CurrentModel { get; set; } = "keyword_xiaodian.table";
    
    /// <summary>
    /// 可用的关键词模型列表
    /// </summary>
    public string[] AvailableModels { get; set; } = 
    {
        "keyword_xiaodian.table",  // 小点唤醒词
        "keyword_cortana.table"    // Cortana唤醒词
    };
}
