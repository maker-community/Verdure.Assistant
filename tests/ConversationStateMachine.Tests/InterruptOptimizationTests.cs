using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Services.Interrupt;
using Verdure.Assistant.Core.Services;
using Xunit;

namespace ConversationStateMachine.Tests;

/// <summary>
/// 测试优化后的打断逻辑
/// Tests for optimized interrupt logic
/// </summary>
public class InterruptOptimizationTests
{
    private readonly ILogger<Verdure.Assistant.Core.Services.ConversationStateMachine> _logger;

    public InterruptOptimizationTests()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<Verdure.Assistant.Core.Services.ConversationStateMachine>();
    }

    [Fact]
    public void ManualInterrupt_FromListening_ShouldGoToConnecting()
    {
        // Arrange
        var stateMachine = new Verdure.Assistant.Core.Services.ConversationStateMachine(_logger);
        stateMachine.RequestTransition(ConversationTrigger.StartVoiceChat, "Start listening");
        Assert.Equal(DeviceState.Listening, stateMachine.CurrentState);

        // Act - Manual interrupt during listening should go to keyword wake-up (Connecting)
        var result = stateMachine.RequestTransition(ConversationTrigger.ManualInterrupt, "Manual interrupt during listening");

        // Assert
        Assert.True(result);
        Assert.Equal(DeviceState.Connecting, stateMachine.CurrentState);
    }

    [Fact]
    public void VadInterrupt_FromListening_ShouldGoToConnecting()
    {
        // Arrange
        var stateMachine = new Verdure.Assistant.Core.Services.ConversationStateMachine(_logger);
        stateMachine.RequestTransition(ConversationTrigger.StartVoiceChat, "Start listening");
        Assert.Equal(DeviceState.Listening, stateMachine.CurrentState);

        // Act - VAD interrupt during listening should go to keyword wake-up (Connecting)
        var result = stateMachine.RequestTransition(ConversationTrigger.VadInterrupt, "VAD interrupt during listening");

        // Assert
        Assert.True(result);
        Assert.Equal(DeviceState.Connecting, stateMachine.CurrentState);
    }

    [Fact]
    public void ManualInterrupt_FromSpeaking_ShouldGoToListening()
    {
        // Arrange
        var stateMachine = new Verdure.Assistant.Core.Services.ConversationStateMachine(_logger);
        // Set up speaking state
        stateMachine.RequestTransition(ConversationTrigger.StartVoiceChat, "Start conversation");
        stateMachine.RequestTransition(ConversationTrigger.TtsStarted, "Start speaking");
        Assert.Equal(DeviceState.Speaking, stateMachine.CurrentState);

        // Act - Manual interrupt during speaking should go to listening
        var result = stateMachine.RequestTransition(ConversationTrigger.ManualInterrupt, "Manual interrupt during speaking");

        // Assert
        Assert.True(result);
        Assert.Equal(DeviceState.Listening, stateMachine.CurrentState);
    }

    [Fact]
    public void VadInterrupt_FromSpeaking_ShouldGoToListening()
    {
        // Arrange
        var stateMachine = new Verdure.Assistant.Core.Services.ConversationStateMachine(_logger);
        // Set up speaking state
        stateMachine.RequestTransition(ConversationTrigger.StartVoiceChat, "Start conversation");
        stateMachine.RequestTransition(ConversationTrigger.TtsStarted, "Start speaking");
        Assert.Equal(DeviceState.Speaking, stateMachine.CurrentState);

        // Act - VAD interrupt during speaking should go to listening
        var result = stateMachine.RequestTransition(ConversationTrigger.VadInterrupt, "VAD interrupt during speaking");

        // Assert
        Assert.True(result);
        Assert.Equal(DeviceState.Listening, stateMachine.CurrentState);
    }

    [Fact]
    public void ManualInterrupt_FromIdle_ShouldGoToConnecting()
    {
        // Arrange
        var stateMachine = new Verdure.Assistant.Core.Services.ConversationStateMachine(_logger);
        Assert.Equal(DeviceState.Idle, stateMachine.CurrentState);

        // Act - Manual interrupt from idle should trigger keyword wake-up (Connecting)
        var result = stateMachine.RequestTransition(ConversationTrigger.ManualInterrupt, "Manual interrupt from idle");

        // Assert
        Assert.True(result);
        Assert.Equal(DeviceState.Connecting, stateMachine.CurrentState);
    }

    [Fact]
    public void VadInterrupt_FromIdle_ShouldGoToConnecting()
    {
        // Arrange
        var stateMachine = new Verdure.Assistant.Core.Services.ConversationStateMachine(_logger);
        Assert.Equal(DeviceState.Idle, stateMachine.CurrentState);

        // Act - VAD interrupt from idle should trigger keyword wake-up (Connecting)
        var result = stateMachine.RequestTransition(ConversationTrigger.VadInterrupt, "VAD interrupt from idle");

        // Assert
        Assert.True(result);
        Assert.Equal(DeviceState.Connecting, stateMachine.CurrentState);
    }

    [Fact]
    public void InterruptTypeHelper_ShouldCategorizeCorrectly()
    {
        // Test manual interrupt types
        Assert.True(InterruptTypeHelper.IsManualInterrupt(InterruptTypes.Api));
        Assert.True(InterruptTypeHelper.IsManualInterrupt(InterruptTypes.Hotkey));
        Assert.True(InterruptTypeHelper.IsManualInterrupt(InterruptTypes.Manual));
        Assert.False(InterruptTypeHelper.IsManualInterrupt(InterruptTypes.VoiceActivity));

        // Test VAD interrupt types
        Assert.True(InterruptTypeHelper.IsVadInterrupt(InterruptTypes.VoiceActivity));
        Assert.False(InterruptTypeHelper.IsVadInterrupt(InterruptTypes.Api));
        Assert.False(InterruptTypeHelper.IsVadInterrupt(InterruptTypes.Hotkey));
        Assert.False(InterruptTypeHelper.IsVadInterrupt(InterruptTypes.Manual));

        // Test category mapping
        Assert.Equal(InterruptCategories.Manual, InterruptTypeHelper.GetInterruptCategory(InterruptTypes.Api));
        Assert.Equal(InterruptCategories.Manual, InterruptTypeHelper.GetInterruptCategory(InterruptTypes.Hotkey));
        Assert.Equal(InterruptCategories.Manual, InterruptTypeHelper.GetInterruptCategory(InterruptTypes.Manual));
        Assert.Equal(InterruptCategories.VoiceActivity, InterruptTypeHelper.GetInterruptCategory(InterruptTypes.VoiceActivity));
    }

    [Fact]
    public void CompleteInterruptFlow_ManualType_ShouldFollowExpectedPath()
    {
        // Arrange
        var stateMachine = new Verdure.Assistant.Core.Services.ConversationStateMachine(_logger);
        var stateTransitions = new List<(DeviceState FromState, DeviceState ToState, ConversationTrigger Trigger)>();

        stateMachine.StateChanged += (sender, args) =>
        {
            stateTransitions.Add((args.FromState, args.ToState, args.Trigger));
        };

        // Act - Simulate complete manual interrupt flow
        // 1. Manual interrupt from idle (should go to keyword wake-up)
        stateMachine.RequestTransition(ConversationTrigger.ManualInterrupt, "Manual interrupt trigger");
        
        // 2. Server connects
        stateMachine.RequestTransition(ConversationTrigger.ServerConnected, "Connected to server");
        
        // 3. Start TTS response
        stateMachine.RequestTransition(ConversationTrigger.TtsStarted, "AI response");
        
        // 4. Another manual interrupt during speaking (should go to listening)
        stateMachine.RequestTransition(ConversationTrigger.ManualInterrupt, "Manual interrupt during speech");

        // Assert
        Assert.Equal(DeviceState.Listening, stateMachine.CurrentState);
        Assert.Equal(4, stateTransitions.Count);

        Assert.Equal((DeviceState.Idle, DeviceState.Connecting, ConversationTrigger.ManualInterrupt), stateTransitions[0]);
        Assert.Equal((DeviceState.Connecting, DeviceState.Listening, ConversationTrigger.ServerConnected), stateTransitions[1]);
        Assert.Equal((DeviceState.Listening, DeviceState.Speaking, ConversationTrigger.TtsStarted), stateTransitions[2]);
        Assert.Equal((DeviceState.Speaking, DeviceState.Listening, ConversationTrigger.ManualInterrupt), stateTransitions[3]);
    }
}