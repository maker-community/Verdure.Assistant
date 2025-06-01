using System.Text.Json;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// WebSocket协议消息构建器
/// 基于Python实现的websocket_protocol.py和提供的C#模板
/// </summary>
public static class WebSocketProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region Hello Messages

    /// <summary>
    /// 创建客户端Hello消息
    /// </summary>
    /// <param name="sessionId">会话ID（可选）</param>
    /// <param name="sampleRate">采样率</param>
    /// <param name="channels">声道数</param>
    /// <param name="frameDuration">帧持续时间</param>
    /// <returns>Hello消息JSON字符串</returns>
    public static string CreateHelloMessage(string? sessionId = null, int sampleRate = 24000, int channels = 1, int frameDuration = 60)
    {
        var message = new HelloMessage
        {
            SessionId = sessionId,
            Version = 1,
            Transport = "websocket",
            AudioParams = new AudioParams
            {
                Format = "opus",
                SampleRate = sampleRate,
                Channels = channels,
                FrameDuration = frameDuration
            }
        };

        return JsonSerializer.Serialize(message, JsonOptions);
    }

    /// <summary>
    /// 创建服务器Hello响应消息（仅用于测试）
    /// </summary>
    /// <param name="sampleRate">服务器采样率</param>
    /// <returns>Hello响应消息JSON字符串</returns>
    public static string CreateHelloResponse(int sampleRate = 24000)
    {
        var message = new HelloMessage
        {
            Transport = "websocket",
            AudioParams = new AudioParams
            {
                SampleRate = sampleRate
            }
        };

        return JsonSerializer.Serialize(message, JsonOptions);
    }

    #endregion

    #region Listen Messages

    /// <summary>
    /// 创建开始监听消息
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="mode">监听模式</param>
    /// <returns>开始监听消息JSON字符串</returns>
    public static string CreateStartListenMessage(string? sessionId, ListeningMode mode)
    {
        var modeString = mode switch
        {
            ListeningMode.AlwaysOn => "realtime",
            ListeningMode.Manual => "manual",
            ListeningMode.AutoStop => "auto",
            _ => "auto"
        };

        var message = new ListenMessage
        {
            SessionId = sessionId,
            State = "start",
            Mode = modeString
        };

        return JsonSerializer.Serialize(message, JsonOptions);
    }

    /// <summary>
    /// 创建停止监听消息
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>停止监听消息JSON字符串</returns>
    public static string CreateStopListenMessage(string? sessionId)
    {
        var message = new ListenMessage
        {
            SessionId = sessionId,
            State = "stop"
        };

        return JsonSerializer.Serialize(message, JsonOptions);
    }

    /// <summary>
    /// 创建唤醒词检测消息
    /// </summary>
    /// <param name="wakeWord">检测到的唤醒词</param>
    /// <param name="sessionId">会话ID（可选）</param>
    /// <returns>唤醒词检测消息JSON字符串</returns>
    public static string CreateWakeWordDetectedMessage(string wakeWord, string? sessionId = null)
    {
        var message = new ListenMessage
        {
            SessionId = sessionId,
            State = "detect",
            Text = wakeWord
        };

        return JsonSerializer.Serialize(message, JsonOptions);
    }

    #endregion

    #region TTS Messages

    /// <summary>
    /// 创建TTS开始消息
    /// </summary>
    /// <param name="sessionId">会话ID（可选）</param>
    /// <returns>TTS开始消息JSON字符串</returns>
    public static string CreateTtsStartMessage(string? sessionId = null)
    {
        var message = new TtsMessage
        {
            SessionId = sessionId,
            State = "start"
        };

        return JsonSerializer.Serialize(message, JsonOptions);
    }

    /// <summary>
    /// 创建TTS停止消息
    /// </summary>
    /// <param name="sessionId">会话ID（可选）</param>
    /// <returns>TTS停止消息JSON字符串</returns>
    public static string CreateTtsStopMessage(string? sessionId = null)
    {
        var message = new TtsMessage
        {
            SessionId = sessionId,
            State = "stop"
        };

        return JsonSerializer.Serialize(message, JsonOptions);
    }

    /// <summary>
    /// 创建TTS句子开始消息
    /// </summary>
    /// <param name="text">文本内容</param>
    /// <param name="sessionId">会话ID（可选）</param>
    /// <returns>TTS句子开始消息JSON字符串</returns>
    public static string CreateTtsSentenceStartMessage(string text, string? sessionId = null)
    {
        var message = new TtsMessage
        {
            SessionId = sessionId,
            State = "sentence_start",
            Text = text
        };

        return JsonSerializer.Serialize(message, JsonOptions);
    }

    /// <summary>
    /// 创建TTS句子结束消息
    /// </summary>
    /// <param name="text">文本内容（可选）</param>
    /// <param name="sessionId">会话ID（可选）</param>
    /// <returns>TTS句子结束消息JSON字符串</returns>
    public static string CreateTtsSentenceEndMessage(string? text = null, string? sessionId = null)
    {
        var message = new TtsMessage
        {
            SessionId = sessionId,
            State = "sentence_end",
            Text = text
        };

        return JsonSerializer.Serialize(message, JsonOptions);
    }

    #endregion

    #region Abort Messages

    /// <summary>
    /// 创建中止消息
    /// </summary>
    /// <param name="reason">中止原因</param>
    /// <param name="sessionId">会话ID</param>
    /// <returns>中止消息JSON字符串</returns>
    public static string CreateAbortMessage(AbortReason reason, string? sessionId)
    {
        var reasonString = reason switch
        {
            AbortReason.WakeWordDetected => "wake_word_detected",
            AbortReason.UserInterruption => "user_interruption",
            _ => null
        };

        var message = new AbortMessage
        {
            SessionId = sessionId,
            Reason = reasonString
        };

        return JsonSerializer.Serialize(message, JsonOptions);
    }

    #endregion

    #region IoT Messages

    /// <summary>
    /// 创建IoT设备描述消息
    /// </summary>
    /// <param name="descriptors">设备描述JSON对象</param>
    /// <param name="sessionId">会话ID</param>
    /// <returns>IoT设备描述消息JSON字符串</returns>
    public static string CreateIotDescriptorsMessage(object descriptors, string? sessionId)
    {
        var message = new IotMessage
        {
            SessionId = sessionId,
            Descriptors = descriptors
        };

        return JsonSerializer.Serialize(message, JsonOptions);
    }

    /// <summary>
    /// 创建IoT设备状态消息
    /// </summary>
    /// <param name="states">状态JSON对象</param>
    /// <param name="sessionId">会话ID</param>
    /// <returns>IoT设备状态消息JSON字符串</returns>
    public static string CreateIotStatesMessage(object states, string? sessionId)
    {
        var message = new IotMessage
        {
            SessionId = sessionId,
            States = states
        };

        return JsonSerializer.Serialize(message, JsonOptions);
    }

    #endregion

    #region LLM Messages

    /// <summary>
    /// 创建情感状态消息
    /// </summary>
    /// <param name="emotion">情感类型</param>
    /// <param name="sessionId">会话ID（可选）</param>
    /// <returns>情感状态消息JSON字符串</returns>
    public static string CreateEmotionMessage(string emotion, string? sessionId = null)
    {
        var message = new LlmMessage
        {
            SessionId = sessionId,
            Emotion = emotion
        };

        return JsonSerializer.Serialize(message, JsonOptions);
    }

    #endregion

    #region Goodbye Messages

    /// <summary>
    /// 创建Goodbye消息
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>Goodbye消息JSON字符串</returns>
    public static string CreateGoodbyeMessage(string? sessionId)
    {
        var message = new GoodbyeMessage
        {
            SessionId = sessionId
        };

        return JsonSerializer.Serialize(message, JsonOptions);
    }

    /// <summary>
    /// 解析WebSocket消息
    /// </summary>
    /// <param name="json">JSON字符串</param>
    /// <returns>协议消息对象</returns>
    public static ProtocolMessage? ParseMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            // 直接获取类型字段
            if (!document.RootElement.TryGetProperty("type", out var typeElement))
                return null;

            string? messageType = typeElement.GetString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(messageType))
                return null;

            // 根据类型直接反序列化为具体消息
            return messageType switch
            {
                "hello" => JsonSerializer.Deserialize<HelloMessage>(json, JsonOptions),
                "listen" => JsonSerializer.Deserialize<ListenMessage>(json, JsonOptions),
                "tts" => JsonSerializer.Deserialize<TtsMessage>(json, JsonOptions),
                "stt" => JsonSerializer.Deserialize<SttMessage>(json, JsonOptions),
                "abort" => JsonSerializer.Deserialize<AbortMessage>(json, JsonOptions),
                "iot" => JsonSerializer.Deserialize<IotMessage>(json, JsonOptions),
                "llm" => JsonSerializer.Deserialize<LlmMessage>(json, JsonOptions),
                "goodbye" => JsonSerializer.Deserialize<GoodbyeMessage>(json, JsonOptions),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 获取消息类型
    /// </summary>
    /// <param name="json">JSON字符串</param>
    /// <returns>消息类型，失败返回null</returns>
    public static string? GetMessageType(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("type", out var typeElement) 
                ? typeElement.GetString() 
                : null;
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
