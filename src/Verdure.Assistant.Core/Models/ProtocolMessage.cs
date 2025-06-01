using System.Text.Json.Serialization;

namespace Verdure.Assistant.Core.Models;

/// <summary>
/// WebSocket协议消息基类
/// </summary>
public abstract class ProtocolMessage
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }
}

/// <summary>
/// Hello消息 - 用于客户端与服务器建立连接
/// </summary>
public class HelloMessage : ProtocolMessage
{
    public override string Type => "hello";

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("transport")]
    public string Transport { get; set; } = "websocket";

    [JsonPropertyName("audio_params")]
    public AudioParams? AudioParams { get; set; }
}

/// <summary>
/// 音频参数配置
/// </summary>
public class AudioParams
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = "opus";

    [JsonPropertyName("sample_rate")]
    public int SampleRate { get; set; } = 24000;

    [JsonPropertyName("channels")]
    public int Channels { get; set; } = 1;

    [JsonPropertyName("frame_duration")]
    public int FrameDuration { get; set; } = 60;
}

/// <summary>
/// 监听控制消息
/// </summary>
public class ListenMessage : ProtocolMessage
{
    public override string Type => "listen";

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty; // start, stop, detect

    [JsonPropertyName("mode")]
    public string? Mode { get; set; } // auto, manual, realtime

    [JsonPropertyName("text")]
    public string? Text { get; set; } // 检测到的唤醒词
}

/// <summary>
/// TTS状态消息
/// </summary>
public class TtsMessage : ProtocolMessage
{
    public override string Type => "tts";

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty; // start, stop, sentence_start, sentence_end

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

/// <summary>
/// Stt状态消息
/// </summary>
public class SttMessage : ProtocolMessage
{
    public override string Type => "stt";

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}


/// <summary>
/// 中止消息
/// </summary>
public class AbortMessage : ProtocolMessage
{
    public override string Type => "abort";

    [JsonPropertyName("reason")]
    public string? Reason { get; set; } // wake_word_detected, user_interruption
}

/// <summary>
/// IoT设备消息
/// </summary>
public class IotMessage : ProtocolMessage
{
    public override string Type => "iot";

    [JsonPropertyName("descriptors")]
    public object? Descriptors { get; set; }

    [JsonPropertyName("states")]
    public object? States { get; set; }
}

/// <summary>
/// LLM情感状态消息
/// </summary>
public class LlmMessage : ProtocolMessage
{
    public override string Type => "llm";

    [JsonPropertyName("emotion")]
    public string? Emotion { get; set; }
}

/// <summary>
/// Goodbye消息 - 用于关闭会话
/// </summary>
public class GoodbyeMessage : ProtocolMessage
{
    public override string Type => "goodbye";
}

/// <summary>
/// 通用协议消息 - 用于解析未知类型的消息
/// </summary>
public class GenericProtocolMessage : ProtocolMessage
{
    private string _type = string.Empty;    
    public override string Type => _type;
    
    [JsonPropertyName("type")]
    public string TypeProperty
    {
        get => _type;
        set => _type = value;
    }

    [JsonExtensionData]
    public Dictionary<string, object>? ExtensionData { get; set; }
}
