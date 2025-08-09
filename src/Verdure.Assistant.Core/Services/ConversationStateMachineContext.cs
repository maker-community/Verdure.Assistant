using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Constants;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// 对话状态机上下文 - 处理状态转换时的具体业务逻辑
/// Context for conversation state machine to handle business logic during state transitions
/// </summary>
public class ConversationStateMachineContext
{
    private readonly ILogger<ConversationStateMachineContext>? _logger;
    private readonly ConversationStateMachine _stateMachine;

    // Action delegates for different transitions
    public Func<Task>? OnEnterListening { get; set; }
    public Func<Task>? OnExitListening { get; set; }
    public Func<Task>? OnEnterSpeaking { get; set; }
    public Func<Task>? OnExitSpeaking { get; set; }
    public Func<Task>? OnEnterIdle { get; set; }
    public Func<Task>? OnEnterConnecting { get; set; }

    public ConversationStateMachineContext(ConversationStateMachine stateMachine, ILogger<ConversationStateMachineContext>? logger = null)
    {
        _stateMachine = stateMachine;
        _logger = logger;
        _stateMachine.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// 处理状态变化事件
    /// </summary>
    private async void OnStateChanged(object? sender, StateTransitionEventArgs e)
    {
        try
        {
            _logger?.LogDebug("Processing state transition: {FromState} -> {ToState}", e.FromState, e.ToState);

            // Handle exit actions for the previous state
            await HandleExitState(e.FromState);

            // Handle enter actions for the new state
            await HandleEnterState(e.ToState);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing state transition: {FromState} -> {ToState}", e.FromState, e.ToState);
        }
    }

    /// <summary>
    /// 处理退出状态的动作
    /// </summary>
    private async Task HandleExitState(DeviceState state)
    {
        try
        {
            switch (state)
            {
                case DeviceState.Listening:
                    if (OnExitListening != null)
                    {
                        await OnExitListening();
                    }
                    break;

                case DeviceState.Speaking:
                    if (OnExitSpeaking != null)
                    {
                        await OnExitSpeaking();
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in exit state handler for {State}", state);
        }
    }

    /// <summary>
    /// 处理进入状态的动作
    /// </summary>
    private async Task HandleEnterState(DeviceState state)
    {
        try
        {
            switch (state)
            {
                case DeviceState.Idle:
                    if (OnEnterIdle != null)
                    {
                        await OnEnterIdle();
                    }
                    break;

                case DeviceState.Connecting:
                    if (OnEnterConnecting != null)
                    {
                        await OnEnterConnecting();
                    }
                    break;

                case DeviceState.Listening:
                    if (OnEnterListening != null)
                    {
                        await Task.Delay(3000); // Small delay to ensure resources are ready
                        await OnEnterListening();
                    }
                    break;

                case DeviceState.Speaking:
                    if (OnEnterSpeaking != null)
                    {
                        await OnEnterSpeaking();
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in enter state handler for {State}", state);
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _stateMachine.StateChanged -= OnStateChanged;
    }
}