using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using Verdure.Assistant.Core.Constants;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// 对话状态机上下文 - 处理状态转换时的具体业务逻辑。
/// <para>
/// 所有进入/退出动作都由单个后台循环严格按转换顺序依次执行（先 Exit 旧状态再 Enter 新状态），
/// 不会出现多个转换的异步动作交叉执行；已经被后续转换取代的状态不会再执行进入动作。
/// </para>
/// </summary>
public sealed class ConversationStateMachineContext : IDisposable
{
    private readonly ILogger<ConversationStateMachineContext>? _logger;
    private readonly ConversationStateMachine _stateMachine;

    private readonly Channel<StateTransitionEventArgs> _queue =
        Channel.CreateUnbounded<StateTransitionEventArgs>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pump;

    private int _pending;
    private DeviceState? _enteredState;
    private CancellationTokenSource? _watchdogCts;
    private bool _disposed;

    // Action delegates for different transitions
    public Func<Task>? OnEnterListening { get; set; }
    public Func<Task>? OnExitListening { get; set; }
    public Func<Task>? OnEnterSpeaking { get; set; }
    public Func<Task>? OnExitSpeaking { get; set; }
    public Func<Task>? OnEnterIdle { get; set; }
    public Func<Task>? OnEnterConnecting { get; set; }
    public Func<Task>? OnExitConnecting { get; set; }

    /// <summary>
    /// 进入 Listening 前的等待时间，给音频设备/唤醒词识别器释放资源的时间
    /// </summary>
    public TimeSpan ListeningStartDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// 连接阶段超时。超时后自动触发 <see cref="ConversationTrigger.ConnectionFailed"/>，防止永远停留在 Connecting。
    /// 设为 <see cref="TimeSpan.Zero"/> 或负值可关闭。
    /// </summary>
    public TimeSpan ConnectingTimeout { get; set; } = TimeSpan.FromSeconds(20);

    public ConversationStateMachineContext(ConversationStateMachine stateMachine,
        ILogger<ConversationStateMachineContext>? logger = null)
    {
        _stateMachine = stateMachine;
        _logger = logger;
        _stateMachine.StateChanged += OnStateChanged;
        _pump = Task.Run(PumpAsync);
    }

    /// <summary>
    /// 等待当前已排队的所有进入/退出动作执行完毕（主要用于测试与有序关闭）
    /// </summary>
    public async Task WaitForIdleAsync(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (Volatile.Read(ref _pending) > 0)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("State machine context did not become idle in time");
            }
            await Task.Delay(5).ConfigureAwait(false);
        }
    }

    private void OnStateChanged(object? sender, StateTransitionEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Interlocked.Increment(ref _pending);
        if (!_queue.Writer.TryWrite(e))
        {
            Interlocked.Decrement(ref _pending);
        }
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var e in _queue.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                try
                {
                    await ProcessAsync(e).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error processing state transition: {FromState} -> {ToState}", e.FromState, e.ToState);
                }
                finally
                {
                    Interlocked.Decrement(ref _pending);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
    }

    private async Task ProcessAsync(StateTransitionEventArgs e)
    {
        _logger?.LogDebug("Processing state transition: {FromState} -> {ToState}", e.FromState, e.ToState);

        // 只退出"真正进入过"的状态：被取代而跳过了进入动作的状态不需要退出
        if (_enteredState.HasValue)
        {
            CancelWatchdog();
            await HandleExitStateAsync(_enteredState.Value).ConfigureAwait(false);
            _enteredState = null;
        }

        if (IsSuperseded(e))
        {
            _logger?.LogDebug("Skip enter {State}: superseded by newer transition", e.ToState);
            return;
        }

        _enteredState = e.ToState;
        await HandleEnterStateAsync(e).ConfigureAwait(false);
    }

    private bool IsSuperseded(StateTransitionEventArgs e)
        => _stateMachine.Epoch != e.Epoch;

    private async Task HandleExitStateAsync(DeviceState state)
    {
        try
        {
            var handler = state switch
            {
                DeviceState.Listening => OnExitListening,
                DeviceState.Speaking => OnExitSpeaking,
                DeviceState.Connecting => OnExitConnecting,
                _ => null
            };

            if (handler != null)
            {
                await handler().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in exit state handler for {State}", state);
        }
    }

    private async Task HandleEnterStateAsync(StateTransitionEventArgs e)
    {
        var state = e.ToState;
        try
        {
            switch (state)
            {
                case DeviceState.Idle:
                    if (OnEnterIdle != null)
                    {
                        await OnEnterIdle().ConfigureAwait(false);
                    }
                    break;

                case DeviceState.Connecting:
                    StartWatchdog(e.Epoch);
                    if (OnEnterConnecting != null)
                    {
                        await OnEnterConnecting().ConfigureAwait(false);
                    }
                    break;

                case DeviceState.Listening:
                    if (OnEnterListening != null)
                    {
                        if (ListeningStartDelay > TimeSpan.Zero)
                        {
                            await Task.Delay(ListeningStartDelay, _cts.Token).ConfigureAwait(false);
                        }

                        // 等待期间状态又变了（比如用户已经打断）就不再开始监听
                        if (IsSuperseded(e))
                        {
                            _logger?.LogDebug("Skip OnEnterListening: superseded during start delay");
                            _enteredState = null;
                            return;
                        }

                        await OnEnterListening().ConfigureAwait(false);
                    }
                    break;

                case DeviceState.Speaking:
                    if (OnEnterSpeaking != null)
                    {
                        await OnEnterSpeaking().ConfigureAwait(false);
                    }
                    break;
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in enter state handler for {State}", state);
        }
    }

    private void StartWatchdog(long epoch)
    {
        CancelWatchdog();

        if (ConnectingTimeout <= TimeSpan.Zero)
        {
            return;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _watchdogCts = cts;
        var timeout = ConnectingTimeout;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(timeout, cts.Token).ConfigureAwait(false);

                if (_stateMachine.Epoch == epoch)
                {
                    _logger?.LogWarning("Connecting timed out after {Timeout}", timeout);
                    _stateMachine.RequestTransition(ConversationTrigger.ConnectionFailed, DeviceState.Connecting,
                        $"Connecting timeout ({timeout.TotalSeconds:0}s)");
                }
            }
            catch (OperationCanceledException)
            {
                // 连接已完成或被取消
            }
        });
    }

    private void CancelWatchdog()
    {
        var cts = Interlocked.Exchange(ref _watchdogCts, null);
        if (cts != null)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stateMachine.StateChanged -= OnStateChanged;
        CancelWatchdog();
        _queue.Writer.TryComplete();
        _cts.Cancel();
    }
}
