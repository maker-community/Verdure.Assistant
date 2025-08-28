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
