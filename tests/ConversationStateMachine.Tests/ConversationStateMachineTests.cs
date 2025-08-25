using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace ConversationStateMachine.Tests;

public class ConversationStateMachineTests
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<Verdure.Assistant.Core.Services.ConversationStateMachine> _logger;

    public ConversationStateMachineTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = loggerFactory.CreateLogger<Verdure.Assistant.Core.Services.ConversationStateMachine>();
    }

    [Fact]
    public void StateMachine_InitialState_ShouldBeIdle()
    {
        // Arrange & Act
        var stateMachine = new Verdure.Assistant.Core.Services.ConversationStateMachine(_logger);

        // Assert
        Assert.Equal(DeviceState.Idle, stateMachine.CurrentState);
    }

    [Fact]
    public void StateMachine_ValidTransition_ShouldSucceed()
    {
        // Arrange
        var stateMachine = new Verdure.Assistant.Core.Services.ConversationStateMachine(_logger);
        DeviceState? capturedNewState = null;
        ConversationTrigger? capturedTrigger = null;

        stateMachine.StateChanged += (sender, args) =>
        {
            capturedNewState = args.ToState;
            capturedTrigger = args.Trigger;
        };

        // Act
        var result = stateMachine.RequestTransition(ConversationTrigger.StartVoiceChat, "Test transition");

        // Assert
        Assert.True(result);
        Assert.Equal(DeviceState.Listening, stateMachine.CurrentState);
        Assert.Equal(DeviceState.Listening, capturedNewState);
        Assert.Equal(ConversationTrigger.StartVoiceChat, capturedTrigger);
    }

    [Fact]
    public void StateMachine_InvalidTransition_ShouldFail()
    {
        // Arrange
        var stateMachine = new Verdure.Assistant.Core.Services.ConversationStateMachine(_logger);
        var stateChangedFired = false;

        stateMachine.StateChanged += (sender, args) => stateChangedFired = true;

        // Act - Try invalid transition: Idle -> TtsCompleted (should fail)
        var result = stateMachine.RequestTransition(ConversationTrigger.TtsCompleted, "Invalid transition");

        // Assert
        Assert.False(result);
        Assert.Equal(DeviceState.Idle, stateMachine.CurrentState);
        Assert.False(stateChangedFired);
    }

    [Fact]
    public void StateMachine_ConversationFlow_ShouldFollowExpectedSequence()
    {
        // Arrange
        var stateMachine = new Verdure.Assistant.Core.Services.ConversationStateMachine(_logger);
        var stateTransitions = new List<(DeviceState From, DeviceState To, ConversationTrigger Trigger)>();

        stateMachine.StateChanged += (sender, args) =>
        {
            stateTransitions.Add((args.FromState, args.ToState, args.Trigger));
        };

        // Act - Simulate a typical conversation flow
        stateMachine.RequestTransition(ConversationTrigger.KeywordDetected, "Wake word detected");
        stateMachine.RequestTransition(ConversationTrigger.TtsStarted, "AI response started");
        stateMachine.RequestTransition(ConversationTrigger.TtsCompleted, "AI response completed");

        // Assert
        Assert.Equal(DeviceState.Idle, stateMachine.CurrentState);
        Assert.Equal(3, stateTransitions.Count);

        Assert.Equal((DeviceState.Idle, DeviceState.Listening, ConversationTrigger.KeywordDetected), stateTransitions[0]);
        Assert.Equal((DeviceState.Listening, DeviceState.Speaking, ConversationTrigger.TtsStarted), stateTransitions[1]);
        Assert.Equal((DeviceState.Speaking, DeviceState.Idle, ConversationTrigger.TtsCompleted), stateTransitions[2]);
    }

    [Fact]
    public void StateMachine_InterruptDuringSpeaking_ShouldReturnToIdle()
    {
        // Arrange
        var stateMachine = new Verdure.Assistant.Core.Services.ConversationStateMachine(_logger);

        // Set up initial state - Speaking
        stateMachine.RequestTransition(ConversationTrigger.StartVoiceChat, "Start conversation");
        stateMachine.RequestTransition(ConversationTrigger.TtsStarted, "Start speaking");

        Assert.Equal(DeviceState.Speaking, stateMachine.CurrentState);

        // Act - Interrupt with keyword detection
        var result = stateMachine.RequestTransition(ConversationTrigger.KeywordDetected, "Interrupt speaking");

        // Assert
        Assert.True(result);
        Assert.Equal(DeviceState.Idle, stateMachine.CurrentState);
    }

    [Fact]
    public void StateMachine_ConnectionLoss_ShouldTransitionToConnecting()
    {
        // Arrange
        var stateMachine = new Verdure.Assistant.Core.Services.ConversationStateMachine(_logger);

        // Test from different states
        var testStates = new[] { DeviceState.Idle, DeviceState.Listening, DeviceState.Speaking };

        foreach (var testState in testStates)
        {
            // Reset to test state
            stateMachine.RequestTransition(ConversationTrigger.ForceIdle, "Reset");
            if (testState == DeviceState.Listening)
                stateMachine.RequestTransition(ConversationTrigger.StartVoiceChat, "Go to listening");
            else if (testState == DeviceState.Speaking)
            {
                stateMachine.RequestTransition(ConversationTrigger.StartVoiceChat, "Go to listening");
                stateMachine.RequestTransition(ConversationTrigger.TtsStarted, "Go to speaking");
            }

            // Act
            var result = stateMachine.RequestTransition(ConversationTrigger.ServerDisconnected, $"Connection lost from {testState}");

            // Assert
            Assert.True(result, $"Transition should succeed from {testState}");
            Assert.Equal(DeviceState.Connecting, stateMachine.CurrentState);
        }
    }

    [Fact]
    public void StateMachine_CanTransition_ShouldReturnCorrectValues()
    {
        // Arrange
        var stateMachine = new Verdure.Assistant.Core.Services.ConversationStateMachine(_logger);

        // Assert - Test valid transitions from Idle
        Assert.True(stateMachine.CanTransition(ConversationTrigger.StartVoiceChat));
        Assert.True(stateMachine.CanTransition(ConversationTrigger.KeywordDetected));
        Assert.True(stateMachine.CanTransition(ConversationTrigger.ConnectToServer));

        // Assert - Test invalid transitions from Idle
        Assert.False(stateMachine.CanTransition(ConversationTrigger.TtsCompleted));
        Assert.False(stateMachine.CanTransition(ConversationTrigger.AudioPlaybackCompleted));

        // Change state and test again
        stateMachine.RequestTransition(ConversationTrigger.StartVoiceChat, "Go to listening");

        // Assert - Test valid transitions from Listening
        Assert.True(stateMachine.CanTransition(ConversationTrigger.StopVoiceChat));
        Assert.True(stateMachine.CanTransition(ConversationTrigger.TtsStarted));
        Assert.True(stateMachine.CanTransition(ConversationTrigger.UserInterrupt));

        // Assert - Test invalid transitions from Listening
        Assert.False(stateMachine.CanTransition(ConversationTrigger.StartVoiceChat));
        Assert.False(stateMachine.CanTransition(ConversationTrigger.TtsCompleted));
    }

    [Fact]
    public void StateMachine_Reset_ShouldReturnToIdle()
    {
        // Arrange
        var stateMachine = new Verdure.Assistant.Core.Services.ConversationStateMachine(_logger);

        // Set up in a different state
        stateMachine.RequestTransition(ConversationTrigger.StartVoiceChat, "Start conversation");
        stateMachine.RequestTransition(ConversationTrigger.TtsStarted, "Start speaking");
        Assert.Equal(DeviceState.Speaking, stateMachine.CurrentState);

        // Act
        stateMachine.Reset();

        // Assert
        Assert.Equal(DeviceState.Idle, stateMachine.CurrentState);
    }
}