using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Events;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// WebSocket事件管理器 - 统一管理所有WebSocket相关事件
/// 参考ConversationStateMachine的设计，提供集中化的事件处理
/// </summary>
public class WebSocketEventManager
{
    private readonly ILogger<WebSocketEventManager>? _logger;
    private readonly object _eventLock = new object();

    /// <summary>
    /// 统一的WebSocket事件 - 所有WebSocket相关事件都通过这个事件分发
    /// </summary>
    public event EventHandler<WebSocketEventArgs>? WebSocketEventOccurred;

    public WebSocketEventManager(ILogger<WebSocketEventManager>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 触发WebSocket事件
    /// </summary>
    /// <param name="trigger">事件触发器</param>
    /// <param name="eventArgs">事件参数</param>
    /// <param name="context">上下文信息</param>
    public void TriggerEvent(WebSocketEventTrigger trigger, WebSocketEventArgs eventArgs, string? context = null)
    {
        lock (_eventLock)
        {
            try
            {
                eventArgs.Trigger = trigger;
                eventArgs.Context = context;
                eventArgs.Timestamp = DateTime.Now;

                _logger?.LogDebug("WebSocket event triggered: {Trigger} (context: {Context})", 
                    trigger, context);

                WebSocketEventOccurred?.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in WebSocket event handler for trigger: {Trigger}", trigger);
            }
        }
    }

    /// <summary>
    /// 触发连接事件
    /// </summary>
    public void TriggerConnectionEvent(WebSocketEventTrigger trigger, bool isConnected, 
        string? sessionId = null, string? errorMessage = null, string? context = null)
    {
        var eventArgs = new ConnectionEventArgs
        {
            IsConnected = isConnected,
            SessionId = sessionId,
            ErrorMessage = errorMessage
        };

        TriggerEvent(trigger, eventArgs, context);
    }

    /// <summary>
    /// 触发消息事件
    /// </summary>
    public void TriggerMessageEvent(WebSocketEventTrigger trigger, ChatMessage? chatMessage = null,
        ProtocolMessage? protocolMessage = null, byte[]? audioData = null, string? context = null)
    {
        var eventArgs = new MessageEventArgs
        {
            ChatMessage = chatMessage,
            ProtocolMessage = protocolMessage,
            AudioData = audioData
        };

        TriggerEvent(trigger, eventArgs, context);
    }

    /// <summary>
    /// 触发TTS事件
    /// </summary>
    public void TriggerTtsEvent(WebSocketEventTrigger trigger, TtsMessage? ttsMessage = null,
        string? state = null, string? text = null, string? context = null)
    {
        var eventArgs = new TtsEventArgs
        {
            TtsMessage = ttsMessage,
            State = state,
            Text = text
        };

        TriggerEvent(trigger, eventArgs, context);
    }

    /// <summary>
    /// 触发音乐事件
    /// </summary>
    public void TriggerMusicEvent(WebSocketEventTrigger trigger, MusicMessage? musicMessage = null,
        string? action = null, string? songName = null, string? artist = null, 
        string? lyricText = null, double position = 0, double duration = 0, string? context = null)
    {
        var eventArgs = new MusicEventArgs
        {
            MusicMessage = musicMessage,
            Action = action,
            SongName = songName,
            Artist = artist,
            LyricText = lyricText,
            Position = position,
            Duration = duration
        };

        TriggerEvent(trigger, eventArgs, context);
    }

    /// <summary>
    /// 触发系统状态事件
    /// </summary>
    public void TriggerSystemStatusEvent(WebSocketEventTrigger trigger, SystemStatusMessage? systemStatusMessage = null,
        string? component = null, string? status = null, string? message = null, string? context = null)
    {
        var eventArgs = new SystemStatusEventArgs
        {
            SystemStatusMessage = systemStatusMessage,
            Component = component,
            Status = status,
            Message = message
        };

        TriggerEvent(trigger, eventArgs, context);
    }

    /// <summary>
    /// 触发LLM情感事件
    /// </summary>
    public void TriggerLlmEmotionEvent(WebSocketEventTrigger trigger, LlmMessage? llmMessage = null,
        string? emotion = null, string? context = null)
    {
        var eventArgs = new LlmEmotionEventArgs
        {
            LlmMessage = llmMessage,
            Emotion = emotion
        };

        TriggerEvent(trigger, eventArgs, context);
    }

    /// <summary>
    /// 触发MCP事件
    /// </summary>
    public void TriggerMcpEvent(WebSocketEventTrigger trigger, McpMessage? mcpMessage = null,
        string? responseJson = null, Exception? error = null, bool isInitialized = false, string? context = null)
    {
        var eventArgs = new McpEventArgs
        {
            McpMessage = mcpMessage,
            ResponseJson = responseJson,
            Error = error,
            IsInitialized = isInitialized
        };

        TriggerEvent(trigger, eventArgs, context);
    }
}
