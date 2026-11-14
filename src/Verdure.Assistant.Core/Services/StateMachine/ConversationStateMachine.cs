using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Constants;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// 对话状态机 - 集中管理对话状态转换逻辑。
/// <para>
/// 线程模型：状态在锁内原子更新；<see cref="StateChanged"/> 事件始终在锁外、按转换发生的先后顺序派发，
/// 并且对重入安全（订阅者在回调里再次请求转换不会死锁，也不会打乱事件顺序）。
/// </para>
/// </summary>
public class ConversationStateMachine
{
    private const int HistoryCapacity = 64;

    private readonly ILogger<ConversationStateMachine>? _logger;
    private readonly object _stateLock = new();

    // 事件派发队列：保证事件顺序，且在锁外执行
    private readonly Queue<StateTransitionEventArgs> _dispatchQueue = new();
    private bool _isDispatching;

    private readonly Queue<StateTransitionRecord> _history = new();

    private DeviceState _currentState = DeviceState.Idle;
    private DeviceState _previousState = DeviceState.Idle;
    private long _epoch;
    private DateTimeOffset _enteredAt = DateTimeOffset.UtcNow;
    private bool _keepListening;

    /// <summary>
    /// 状态变化事件（锁外、按顺序派发）
    /// </summary>
    public event EventHandler<StateTransitionEventArgs>? StateChanged;

    /// <summary>
    /// 当前状态
    /// </summary>
    public DeviceState CurrentState
    {
        get { lock (_stateLock) { return _currentState; } }
    }

    /// <summary>
    /// 旧状态
    /// </summary>
    public DeviceState PreviousState
    {
        get { lock (_stateLock) { return _previousState; } }
    }

    /// <summary>
    /// 状态代次：每发生一次真实的状态变化自增，用于识别"过期"的异步回调
    /// </summary>
    public long Epoch
    {
        get { lock (_stateLock) { return _epoch; } }
    }

    /// <summary>
    /// 在当前状态停留的时长
    /// </summary>
    public TimeSpan TimeInCurrentState
    {
        get { lock (_stateLock) { return DateTimeOffset.UtcNow - _enteredAt; } }
    }

    /// <summary>
    /// 是否保持持续监听。为 true 时，AI 回复完成后回到 Listening；否则回到 Idle 等待下一次唤醒。
    /// </summary>
    public bool KeepListening
    {
        get { lock (_stateLock) { return _keepListening; } }
        set { lock (_stateLock) { _keepListening = value; } }
    }

    public ConversationStateMachine(ILogger<ConversationStateMachine>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 最近的状态转换记录（最旧的在前，用于诊断）
    /// </summary>
    public IReadOnlyList<StateTransitionRecord> GetHistory()
    {
        lock (_stateLock)
        {
            return _history.ToArray();
        }
    }

    /// <summary>
    /// 请求状态转换
    /// </summary>
    /// <param name="trigger">触发事件</param>
    /// <param name="context">上下文信息</param>
    /// <returns>触发事件在当前状态下是否有效（被显式忽略的触发也视为有效）</returns>
    public bool RequestTransition(ConversationTrigger trigger, string? context = null)
        => RequestTransition(trigger, expectedState: null, context);

    /// <summary>
    /// 带前置状态校验的转换请求：仅当当前状态等于 <paramref name="expectedState"/> 时才执行，
    /// 用于异步流程（连接完成、超时）避免把已经变化的状态改回去。
    /// </summary>
    public bool RequestTransition(ConversationTrigger trigger, DeviceState? expectedState, string? context = null)
    {
        bool changed;

        lock (_stateLock)
        {
            var fromState = _currentState;

            if (expectedState.HasValue && expectedState.Value != fromState)
            {
                _logger?.LogDebug("State transition skipped (expected {Expected} but was {Actual}): {Trigger} (context: {Context})",
                    expectedState.Value, fromState, trigger, context);
                return false;
            }

            var toState = GetNextState(fromState, trigger);

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

            _previousState = fromState;
            _currentState = toState.Value;
            _epoch++;
            _enteredAt = DateTimeOffset.UtcNow;

            var eventArgs = new StateTransitionEventArgs
            {
                FromState = fromState,
                ToState = toState.Value,
                Trigger = trigger,
                Context = context,
                Epoch = _epoch,
                Timestamp = _enteredAt
            };

            if (_history.Count >= HistoryCapacity)
            {
                _history.Dequeue();
            }
            _history.Enqueue(new StateTransitionRecord(eventArgs.Timestamp, fromState, toState.Value, trigger, context));

            _dispatchQueue.Enqueue(eventArgs);
            changed = true;
        }

        if (changed)
        {
            DrainDispatchQueue();
        }

        return true;
    }

    /// <summary>
    /// 在锁外按顺序派发事件。若已有线程在派发（包括重入场景），本次入队的事件由它负责派发。
    /// </summary>
    private void DrainDispatchQueue()
    {
        lock (_stateLock)
        {
            if (_isDispatching)
            {
                return;
            }
            _isDispatching = true;
        }

        while (true)
        {
            StateTransitionEventArgs next;
            lock (_stateLock)
            {
                if (!_dispatchQueue.TryDequeue(out next!))
                {
                    _isDispatching = false;
                    return;
                }
            }

            var handlers = StateChanged;
            if (handlers == null)
            {
                continue;
            }

            foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<StateTransitionEventArgs>>())
            {
                try
                {
                    handler(this, next);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in state change event handler");
                }
            }
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
    /// 获取下一个状态。返回当前状态本身表示"显式忽略"（无副作用），返回 null 表示非法转换。
    /// </summary>
    private DeviceState? GetNextState(DeviceState currentState, ConversationTrigger trigger)
    {
        // 任意状态下都有效的系统级转换：
        // 断线/失败一律回到 Idle，由上层决定是否重连，避免状态机自身陷入重连死循环
        if (trigger is ConversationTrigger.ForceIdle
            or ConversationTrigger.ServerDisconnected
            or ConversationTrigger.ConnectionFailed)
        {
            return DeviceState.Idle;
        }

        return (currentState, trigger) switch
        {
            // ---- Idle：等待唤醒词或手动触发 ----
            // 开始对话统一经过 Connecting：已连接时由上下文立即完成，未连接时先建立连接，
            // 避免进入没有连接的 Listening
            (DeviceState.Idle, ConversationTrigger.StartVoiceChat) => DeviceState.Connecting,
            (DeviceState.Idle, ConversationTrigger.KeywordDetected) => DeviceState.Connecting,
            (DeviceState.Idle, ConversationTrigger.ConnectToServer) => DeviceState.Connecting,
            (DeviceState.Idle, ConversationTrigger.StopVoiceChat) => DeviceState.Idle,
            (DeviceState.Idle, ConversationTrigger.UserInterrupt) => DeviceState.Idle,
            // 手动/VAD 打断等价于一次唤醒（需求见 docs/INTERRUPT_OPTIMIZATION_IMPLEMENTATION.md）
            (DeviceState.Idle, ConversationTrigger.ManualInterrupt) => DeviceState.Connecting,
            (DeviceState.Idle, ConversationTrigger.VadInterrupt) => DeviceState.Connecting,
            // 手动（按住说话）模式下松开按钮后会回到 Idle，服务器随后下发的 TTS 仍应表现为"正在说话"
            (DeviceState.Idle, ConversationTrigger.TtsStarted) => DeviceState.Speaking,

            // ---- Connecting ----
            (DeviceState.Connecting, ConversationTrigger.ServerConnected) => DeviceState.Listening,
            (DeviceState.Connecting, ConversationTrigger.StartVoiceChat) => DeviceState.Connecting,
            (DeviceState.Connecting, ConversationTrigger.KeywordDetected) => DeviceState.Connecting,
            (DeviceState.Connecting, ConversationTrigger.ConnectToServer) => DeviceState.Connecting,
            (DeviceState.Connecting, ConversationTrigger.StopVoiceChat) => DeviceState.Idle,
            (DeviceState.Connecting, ConversationTrigger.UserInterrupt) => DeviceState.Idle,
            (DeviceState.Connecting, ConversationTrigger.ManualInterrupt) => DeviceState.Idle,
            (DeviceState.Connecting, ConversationTrigger.VadInterrupt) => DeviceState.Connecting,

            // ---- Listening ----
            (DeviceState.Listening, ConversationTrigger.StopVoiceChat) => DeviceState.Idle,
            (DeviceState.Listening, ConversationTrigger.UserInterrupt) => DeviceState.Idle,
            (DeviceState.Listening, ConversationTrigger.TtsStarted) => DeviceState.Speaking,
            (DeviceState.Listening, ConversationTrigger.AudioReceived) => DeviceState.Speaking,
            (DeviceState.Listening, ConversationTrigger.ServerConnected) => DeviceState.Listening,
            (DeviceState.Listening, ConversationTrigger.StartVoiceChat) => DeviceState.Listening,
            (DeviceState.Listening, ConversationTrigger.ConnectToServer) => DeviceState.Listening,
            // 聆听时再次检测到唤醒词多半是回声/误触发：忽略，而不是重新连接
            (DeviceState.Listening, ConversationTrigger.KeywordDetected) => DeviceState.Listening,
            // 聆听中被打断：放弃本轮采集并重新走一遍唤醒流程；连接仍然有效时 Connecting 会立刻完成，不会真的断线重连
            (DeviceState.Listening, ConversationTrigger.ManualInterrupt) => DeviceState.Connecting,
            (DeviceState.Listening, ConversationTrigger.VadInterrupt) => DeviceState.Connecting,

            // ---- Speaking ----
            (DeviceState.Speaking, ConversationTrigger.TtsCompleted) => AfterSpeakingState(),
            (DeviceState.Speaking, ConversationTrigger.AudioPlaybackCompleted) => AfterSpeakingState(),
            (DeviceState.Speaking, ConversationTrigger.AudioReceived) => DeviceState.Speaking,
            (DeviceState.Speaking, ConversationTrigger.TtsStarted) => DeviceState.Speaking,
            (DeviceState.Speaking, ConversationTrigger.StartVoiceChat) => DeviceState.Speaking,
            (DeviceState.Speaking, ConversationTrigger.ConnectToServer) => DeviceState.Speaking,
            (DeviceState.Speaking, ConversationTrigger.ServerConnected) => DeviceState.Speaking,
            (DeviceState.Speaking, ConversationTrigger.UserInterrupt) => DeviceState.Idle,
            (DeviceState.Speaking, ConversationTrigger.StopVoiceChat) => DeviceState.Idle,
            // 说话时被打断（唤醒词 / 手动 / VAD）：转入聆听，接着听用户说
            (DeviceState.Speaking, ConversationTrigger.KeywordDetected) => DeviceState.Listening,
            (DeviceState.Speaking, ConversationTrigger.ManualInterrupt) => DeviceState.Listening,
            (DeviceState.Speaking, ConversationTrigger.VadInterrupt) => DeviceState.Listening,

            _ => null
        };
    }

    // 仅在持有 _stateLock 时调用
    private DeviceState AfterSpeakingState() => _keepListening ? DeviceState.Listening : DeviceState.Idle;

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

    // Interrupt triggers (categorized by requirements)
    ManualInterrupt,    // 手动打断 (API, Hotkey, Manual)
    VadInterrupt,       // VAD打断 (Voice Activity Detection)

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

    /// <summary>
    /// 该转换生效后的状态代次
    /// </summary>
    public long Epoch { get; set; }

    /// <summary>
    /// 转换发生时间
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 状态转换历史记录（诊断用）
/// </summary>
public readonly record struct StateTransitionRecord(
    DateTimeOffset Timestamp,
    DeviceState From,
    DeviceState To,
    ConversationTrigger Trigger,
    string? Context);
