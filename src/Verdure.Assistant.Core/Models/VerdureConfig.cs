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
    public bool UseWebSocket { get; set; } = true;    public bool EnableVoice { get; set; } = true;
    public int AudioSampleRate { get; set; } = 16000; // INPUT_SAMPLE_RATE 匹配Python
    public int AudioOutputSampleRate { get; set; } = 16000; // OUTPUT_SAMPLE_RATE 匹配Python  
    public int AudioChannels { get; set; } = 1; // CHANNELS 匹配Python
    public string AudioFormat { get; set; } = "opus";
    public bool EnableTemperatureSensor { get; set; } = false;
    public string TemperatureSensorPin { get; set; } = "A0";
}
