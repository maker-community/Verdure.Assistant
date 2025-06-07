using System.Text.Encodings.Web;
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
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    #region Hello Messages    
    
    /// <summary>
    /// 创建客户端Hello消息
    /// </summary>
    /// <param name="sessionId">会话ID（可选）</param>
    /// <param name="sampleRate">采样率</param>
    /// <param name="channels">声道数</param>
    /// <param name="frameDuration">帧持续时间</param>
    /// <param name="supportMcp">是否支持MCP协议</param>
    /// <returns>Hello消息JSON字符串</returns>
    public static string CreateHelloMessage(string? sessionId = null, int sampleRate = 24000, int channels = 1, int frameDuration = 60, bool supportMcp = true)
    {
        var features = new Dictionary<string, object>();
        if (supportMcp)
        {
            features["mcp"] = true;
        }

        var message = new HelloMessage
        {
            SessionId = sessionId,
            Version = 1,
            Transport = "websocket",
            Features = features.Count > 0 ? features : null,
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
                return null;            // 根据类型直接反序列化为具体消息
            return messageType switch
            {
                "hello" => JsonSerializer.Deserialize<HelloMessage>(json, JsonOptions),
                "listen" => JsonSerializer.Deserialize<ListenMessage>(json, JsonOptions),
                "tts" => JsonSerializer.Deserialize<TtsMessage>(json, JsonOptions),
                "stt" => JsonSerializer.Deserialize<SttMessage>(json, JsonOptions),
                "abort" => JsonSerializer.Deserialize<AbortMessage>(json, JsonOptions),
                "llm" => JsonSerializer.Deserialize<LlmMessage>(json, JsonOptions),
                "goodbye" => JsonSerializer.Deserialize<GoodbyeMessage>(json, JsonOptions),
                "music" => JsonSerializer.Deserialize<MusicMessage>(json, JsonOptions),
                "system_status" => JsonSerializer.Deserialize<SystemStatusMessage>(json, JsonOptions),
                "mcp" => JsonSerializer.Deserialize<McpMessage>(json, JsonOptions),
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

    #region MCP Messages

    /// <summary>
    /// 创建MCP消息
    /// 对应xiaozhi-esp32的SendMcpMessage方法
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="payload">MCP JSON-RPC负载</param>
    /// <returns>MCP消息JSON字符串</returns>
    public static string CreateMcpMessage(string? sessionId, object payload)
    {
        var message = new McpMessage
        {
            SessionId = sessionId,
            Payload = payload
        };

        return JsonSerializer.Serialize(message, JsonOptions);
    }

    /// <summary>
    /// 创建MCP初始化请求消息
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="id">请求ID</param>
    /// <param name="capabilities">客户端能力</param>
    /// <returns>MCP初始化消息JSON字符串</returns>
    public static string CreateMcpInitializeMessage(string? sessionId, int id, object? capabilities = null)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = capabilities ?? new { },
                clientInfo = new
                {
                    name = "Verdure Assistant MCP Client",
                    version = "1.0.0"
                }
            },
            id = id
        };

        return CreateMcpMessage(sessionId, payload);
    }

    /// <summary>
    /// 创建MCP工具列表请求消息
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="id">请求ID</param>
    /// <param name="cursor">分页游标</param>
    /// <returns>MCP工具列表请求消息JSON字符串</returns>
    public static string CreateMcpToolsListMessage(string? sessionId, int id, string cursor = "")
    {
        var payload = new
        {
            jsonrpc = "2.0",
            method = "tools/list",
            @params = new
            {
                cursor = cursor
            },
            id = id
        };

        return CreateMcpMessage(sessionId, payload);
    }

    /// <summary>
    /// 创建MCP工具调用请求消息
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="id">请求ID</param>
    /// <param name="toolName">工具名称</param>
    /// <param name="arguments">工具参数</param>
    /// <returns>MCP工具调用请求消息JSON字符串</returns>
    public static string CreateMcpToolCallMessage(string? sessionId, int id, string toolName, Dictionary<string, object>? arguments = null)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = arguments ?? new Dictionary<string, object>()
            },
            id = id
        };

        return CreateMcpMessage(sessionId, payload);
    }

    #endregion
}
