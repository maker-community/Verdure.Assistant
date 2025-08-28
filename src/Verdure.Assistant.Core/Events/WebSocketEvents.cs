using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Events;

/// <summary>
/// WebSocket事件触发器枚举 - 统一所有WebSocket相关事件
/// </summary>
public enum WebSocketEventTrigger
{
    // 连接事件
    ConnectionEstablished,
    ConnectionLost,
    ConnectionError,
    HelloReceived,
    GoodbyeReceived,

    // 消息事件
    TextMessageReceived,
    AudioDataReceived,
    ProtocolMessageReceived,

    // TTS事件
    TtsStarted,
    TtsStopped,
    TtsSentenceStarted,
    TtsSentenceEnded,

    // 音乐事件
    MusicPlay,
    MusicPause,
    MusicStop,
    MusicLyricUpdate,
    MusicSeek,

    // 系统状态事件
    SystemStatusUpdate,
    
    // LLM情感事件
    LlmEmotionUpdate,

    // MCP事件
    McpReadyForInitialization,
    McpInitialized,
    McpToolsListRequest,
    McpToolCallRequest,
    McpResponseReceived,
    McpError
}

/// <summary>
/// WebSocket事件参数基类
/// </summary>
public class WebSocketEventArgs : EventArgs
{
    public WebSocketEventTrigger Trigger { get; set; }
    public string? Context { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// 连接事件参数
/// </summary>
public class ConnectionEventArgs : WebSocketEventArgs
{
    public bool IsConnected { get; set; }
    public string? SessionId { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 消息事件参数
/// </summary>
public class MessageEventArgs : WebSocketEventArgs
{
    public ChatMessage? ChatMessage { get; set; }
    public ProtocolMessage? ProtocolMessage { get; set; }
    public byte[]? AudioData { get; set; }
}

/// <summary>
/// TTS事件参数
/// </summary>
public class TtsEventArgs : WebSocketEventArgs
{
    public TtsMessage? TtsMessage { get; set; }
    public string? State { get; set; }
    public string? Text { get; set; }
}

/// <summary>
/// 音乐事件参数
/// </summary>
public class MusicEventArgs : WebSocketEventArgs
{
    public MusicMessage? MusicMessage { get; set; }
    public string? Action { get; set; }
    public string? SongName { get; set; }
    public string? Artist { get; set; }
    public string? LyricText { get; set; }
    public double Position { get; set; }
    public double Duration { get; set; }
}

/// <summary>
/// 系统状态事件参数
/// </summary>
public class SystemStatusEventArgs : WebSocketEventArgs
{
    public SystemStatusMessage? SystemStatusMessage { get; set; }
    public string? Component { get; set; }
    public string? Status { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// LLM情感事件参数
/// </summary>
public class LlmEmotionEventArgs : WebSocketEventArgs
{
    public LlmMessage? LlmMessage { get; set; }
    public string? Emotion { get; set; }
}

/// <summary>
/// MCP事件参数
/// </summary>
public class McpEventArgs : WebSocketEventArgs
{
    public McpMessage? McpMessage { get; set; }
    public string? ResponseJson { get; set; }
    public Exception? Error { get; set; }
    public bool IsInitialized { get; set; }
}
