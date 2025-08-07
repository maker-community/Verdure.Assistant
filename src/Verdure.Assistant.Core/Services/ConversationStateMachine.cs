using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Constants;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// 对话状态机 - 用于集中管理对话状态转换逻辑
/// Centralized conversation state machine for managing state transitions
/// </summary>
public class ConversationStateMachine
{
    private readonly ILogger<ConversationStateMachine>? _logger;
    private DeviceState _currentState;
    private readonly object _stateLock = new object();

    /// <summary>
    /// 状态变化事件
    /// </summary>
    public event EventHandler<StateTransitionEventArgs>? StateChanged;

    /// <summary>
    /// 当前状态
    /// </summary>
    public DeviceState CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _currentState;
            }
        }
    }

    public ConversationStateMachine(ILogger<ConversationStateMachine>? logger = null)
    {
        _logger = logger;
        _currentState = DeviceState.Idle;
    }

    /// <summary>
    /// 请求状态转换
    /// </summary>
    /// <param name="trigger">触发事件</param>
    /// <param name="context">上下文信息</param>
    /// <returns>是否成功转换</returns>
    public bool RequestTransition(ConversationTrigger trigger, string? context = null)
    {
        lock (_stateLock)
        {
            var fromState = _currentState;
            var toState = GetNextState(_currentState, trigger);

            if (toState == null)
            {
                _logger?.LogWarning("Invalid state transition: {FromState} -> {Trigger} (context: {Context})", 
                    fromState, trigger, context);
                return false;
            }

            if (fromState == toState.Value)
            {
                _logger?.LogDebug("State transition ignored (already in target state): {State} -> {Trigger}", 
                    fromState, trigger);
                return true;
            }

            _logger?.LogInformation("State transition: {FromState} -> {ToState} (trigger: {Trigger}, context: {Context})", 
                fromState, toState.Value, trigger, context);

            _currentState = toState.Value;

            // Fire state change event
            var eventArgs = new StateTransitionEventArgs
            {
                FromState = fromState,
                ToState = toState.Value,
                Trigger = trigger,
                Context = context
            };

            try
            {
                StateChanged?.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in state change event handler");
            }

            return true;
        }
    }

    /// <summary>
    /// 检查状态转换是否有效
    /// </summary>
    /// <param name="trigger">触发事件</param>
    /// <returns>是否可以转换</returns>
    public bool CanTransition(ConversationTrigger trigger)
    {
        lock (_stateLock)
        {
            return GetNextState(_currentState, trigger) != null;
        }
    }

    /// <summary>
    /// 获取下一个状态
    /// </summary>
    /// <param name="currentState">当前状态</param>
    /// <param name="trigger">触发事件</param>
    /// <returns>下一个状态，如果转换无效则返回null</returns>
    private DeviceState? GetNextState(DeviceState currentState, ConversationTrigger trigger)
    {
        return (currentState, trigger) switch
        {
            // From Idle state
            (DeviceState.Idle, ConversationTrigger.StartVoiceChat) => DeviceState.Listening,
            (DeviceState.Idle, ConversationTrigger.KeywordDetected) => DeviceState.Listening,
            (DeviceState.Idle, ConversationTrigger.ConnectToServer) => DeviceState.Connecting,

            // From Connecting state
            (DeviceState.Connecting, ConversationTrigger.ServerConnected) => DeviceState.Idle,
            (DeviceState.Connecting, ConversationTrigger.ConnectionFailed) => DeviceState.Idle,

            // From Listening state
            (DeviceState.Listening, ConversationTrigger.StopVoiceChat) => DeviceState.Idle,
            (DeviceState.Listening, ConversationTrigger.UserInterrupt) => DeviceState.Idle,
            (DeviceState.Listening, ConversationTrigger.TtsStarted) => DeviceState.Speaking,
            (DeviceState.Listening, ConversationTrigger.AudioReceived) => DeviceState.Speaking,
            (DeviceState.Listening, ConversationTrigger.ServerDisconnected) => DeviceState.Connecting,

            // From Speaking state
            (DeviceState.Speaking, ConversationTrigger.TtsCompleted) => DeviceState.Idle,
            (DeviceState.Speaking, ConversationTrigger.AudioPlaybackCompleted) => DeviceState.Idle,
            (DeviceState.Speaking, ConversationTrigger.UserInterrupt) => DeviceState.Idle,
            (DeviceState.Speaking, ConversationTrigger.KeywordDetected) => DeviceState.Idle, // Interrupt speaking
            (DeviceState.Speaking, ConversationTrigger.StopVoiceChat) => DeviceState.Idle,
            (DeviceState.Speaking, ConversationTrigger.ServerDisconnected) => DeviceState.Connecting,

            // Universal transitions (from any state)
            (_, ConversationTrigger.ServerDisconnected) => DeviceState.Connecting,
            (_, ConversationTrigger.ForceIdle) => DeviceState.Idle,

            // Invalid transitions
            _ => null
        };
    }

    /// <summary>
    /// 重置状态机到空闲状态
    /// </summary>
    public void Reset()
    {
        RequestTransition(ConversationTrigger.ForceIdle, "State machine reset");
    }
}

/// <summary>
/// 对话触发事件枚举
/// </summary>
public enum ConversationTrigger
{
    // Voice chat control
    StartVoiceChat,
    StopVoiceChat,
    
    // Keyword detection
    KeywordDetected,
    
    // Audio events
    TtsStarted,
    TtsCompleted,
    AudioReceived,
    AudioPlaybackCompleted,
    
    // User interaction
    UserInterrupt,
    
    // Connection events
    ConnectToServer,
    ServerConnected,
    ServerDisconnected,
    ConnectionFailed,
    
    // System events
    ForceIdle
}

/// <summary>
/// 状态转换事件参数
/// </summary>
public class StateTransitionEventArgs : EventArgs
{
    public DeviceState FromState { get; set; }
    public DeviceState ToState { get; set; }
    public ConversationTrigger Trigger { get; set; }
    public string? Context { get; set; }
}