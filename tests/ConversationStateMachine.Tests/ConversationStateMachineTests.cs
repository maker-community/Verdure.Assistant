using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Services;
using Xunit;
using Machine = Verdure.Assistant.Core.Services.ConversationStateMachine;

namespace ConversationStateMachine.Tests;

public class ConversationStateMachineTests
{
    private static Machine ToState(DeviceState target, bool keepListening = false)
    {
        var m = new Machine { KeepListening = keepListening };
        switch (target)
        {
            case DeviceState.Connecting:
                m.RequestTransition(ConversationTrigger.StartVoiceChat);
                break;
            case DeviceState.Listening:
                m.RequestTransition(ConversationTrigger.StartVoiceChat);
                m.RequestTransition(ConversationTrigger.ServerConnected);
                break;
            case DeviceState.Speaking:
                m.RequestTransition(ConversationTrigger.StartVoiceChat);
                m.RequestTransition(ConversationTrigger.ServerConnected);
                m.RequestTransition(ConversationTrigger.TtsStarted);
                break;
        }
        Assert.Equal(target, m.CurrentState);
        return m;
    }

    [Fact]
    public void InitialState_IsIdle()
    {
        Assert.Equal(DeviceState.Idle, new Machine().CurrentState);
    }

    [Fact]
    public void StartVoiceChat_FromIdle_GoesThroughConnecting()
    {
        var m = new Machine();
        var seen = new List<(DeviceState From, DeviceState To)>();
        m.StateChanged += (_, e) => seen.Add((e.FromState, e.ToState));

        Assert.True(m.RequestTransition(ConversationTrigger.StartVoiceChat));
        Assert.Equal(DeviceState.Connecting, m.CurrentState);

        Assert.True(m.RequestTransition(ConversationTrigger.ServerConnected));
        Assert.Equal(DeviceState.Listening, m.CurrentState);

        Assert.Equal(new[]
        {
            (DeviceState.Idle, DeviceState.Connecting),
            (DeviceState.Connecting, DeviceState.Listening)
        }, seen);
    }

    [Fact]
    public void InvalidTransition_IsRejected_AndRaisesNoEvent()
    {
        var m = new Machine();
        var fired = false;
        m.StateChanged += (_, _) => fired = true;

        Assert.False(m.RequestTransition(ConversationTrigger.TtsCompleted));
        Assert.Equal(DeviceState.Idle, m.CurrentState);
        Assert.False(fired);
    }

    [Fact]
    public void FullConversation_WithKeepListening_ReturnsToListening()
    {
        var m = ToState(DeviceState.Speaking, keepListening: true);
        Assert.True(m.RequestTransition(ConversationTrigger.TtsCompleted));
        Assert.Equal(DeviceState.Listening, m.CurrentState);
    }

    [Theory]
    [InlineData(ConversationTrigger.TtsCompleted)]
    [InlineData(ConversationTrigger.AudioPlaybackCompleted)]
    public void SpeakingFinished_WithoutKeepListening_ReturnsToIdle(ConversationTrigger trigger)
    {
        var m = ToState(DeviceState.Speaking, keepListening: false);
        Assert.True(m.RequestTransition(trigger));
        Assert.Equal(DeviceState.Idle, m.CurrentState);
    }

    [Theory]
    [InlineData(DeviceState.Idle)]
    [InlineData(DeviceState.Connecting)]
    [InlineData(DeviceState.Listening)]
    [InlineData(DeviceState.Speaking)]
    public void ConnectionLoss_FromAnyState_GoesIdle_NotStuckInConnecting(DeviceState start)
    {
        var m = ToState(start);
        Assert.True(m.RequestTransition(ConversationTrigger.ServerDisconnected));
        Assert.Equal(DeviceState.Idle, m.CurrentState);
    }

    [Fact]
    public void ConnectionFailed_FromConnecting_GoesIdle()
    {
        var m = ToState(DeviceState.Connecting);
        Assert.True(m.RequestTransition(ConversationTrigger.ConnectionFailed));
        Assert.Equal(DeviceState.Idle, m.CurrentState);
    }

    [Fact]
    public void TtsStarted_FromIdle_GoesSpeaking_ForPushToTalkReplies()
    {
        var m = new Machine { KeepListening = false };

        Assert.True(m.RequestTransition(ConversationTrigger.TtsStarted));
        Assert.Equal(DeviceState.Speaking, m.CurrentState);

        m.RequestTransition(ConversationTrigger.TtsCompleted);
        Assert.Equal(DeviceState.Idle, m.CurrentState);
    }

    [Fact]
    public void KeywordDetected_WhileListening_IsIgnored_NotReconnect()
    {
        var m = ToState(DeviceState.Listening);
        var epoch = m.Epoch;

        Assert.True(m.RequestTransition(ConversationTrigger.KeywordDetected));

        Assert.Equal(DeviceState.Listening, m.CurrentState);
        Assert.Equal(epoch, m.Epoch);
    }

    [Fact]
    public void KeywordDetected_WhileConnecting_IsIgnored()
    {
        var m = ToState(DeviceState.Connecting);
        Assert.True(m.RequestTransition(ConversationTrigger.KeywordDetected));
        Assert.Equal(DeviceState.Connecting, m.CurrentState);
    }

    [Fact]
    public void KeywordDetected_WhileSpeaking_BargesInToListening()
    {
        var m = ToState(DeviceState.Speaking);
        Assert.True(m.RequestTransition(ConversationTrigger.KeywordDetected));
        Assert.Equal(DeviceState.Listening, m.CurrentState);
    }

    [Theory]
    [InlineData(DeviceState.Idle, ConversationTrigger.ManualInterrupt, DeviceState.Connecting)]
    [InlineData(DeviceState.Idle, ConversationTrigger.VadInterrupt, DeviceState.Connecting)]
    [InlineData(DeviceState.Listening, ConversationTrigger.ManualInterrupt, DeviceState.Connecting)]
    [InlineData(DeviceState.Listening, ConversationTrigger.VadInterrupt, DeviceState.Connecting)]
    [InlineData(DeviceState.Speaking, ConversationTrigger.ManualInterrupt, DeviceState.Listening)]
    [InlineData(DeviceState.Speaking, ConversationTrigger.VadInterrupt, DeviceState.Listening)]
    [InlineData(DeviceState.Connecting, ConversationTrigger.ManualInterrupt, DeviceState.Idle)]
    [InlineData(DeviceState.Connecting, ConversationTrigger.VadInterrupt, DeviceState.Connecting)]
    public void Interrupts_FollowDocumentedMatrix(DeviceState from, ConversationTrigger trigger, DeviceState expected)
    {
        var m = ToState(from);
        Assert.True(m.RequestTransition(trigger));
        Assert.Equal(expected, m.CurrentState);
    }

    [Theory]
    [InlineData(DeviceState.Connecting)]
    [InlineData(DeviceState.Listening)]
    [InlineData(DeviceState.Speaking)]
    public void StopVoiceChat_FromActiveStates_GoesIdle(DeviceState start)
    {
        var m = ToState(start);
        Assert.True(m.RequestTransition(ConversationTrigger.StopVoiceChat));
        Assert.Equal(DeviceState.Idle, m.CurrentState);
    }

    [Fact]
    public void ExpectedState_Guard_RejectsStaleTransitions()
    {
        var m = ToState(DeviceState.Connecting);
        m.RequestTransition(ConversationTrigger.StopVoiceChat);
        Assert.Equal(DeviceState.Idle, m.CurrentState);

        // 迟到的"连接成功"回调不能把已取消的连接重新拉回 Listening
        var accepted = m.RequestTransition(ConversationTrigger.ServerConnected, DeviceState.Connecting, "late callback");

        Assert.False(accepted);
        Assert.Equal(DeviceState.Idle, m.CurrentState);
    }

    [Fact]
    public void Epoch_IncrementsOnlyOnRealChanges()
    {
        var m = new Machine();
        var e0 = m.Epoch;

        m.RequestTransition(ConversationTrigger.ForceIdle);      // 已经是 Idle
        Assert.Equal(e0, m.Epoch);

        m.RequestTransition(ConversationTrigger.StartVoiceChat);
        Assert.Equal(e0 + 1, m.Epoch);
    }

    [Fact]
    public void Events_AreRaisedOutsideTheLock_AndReentrantRequestsKeepOrder()
    {
        var m = new Machine();
        var order = new List<string>();

        m.StateChanged += (_, e) =>
        {
            order.Add($"{e.FromState}->{e.ToState}");
            // 订阅者在回调里再次请求转换：不能死锁，也不能乱序
            if (e.ToState == DeviceState.Connecting)
            {
                m.RequestTransition(ConversationTrigger.ServerConnected);
            }
        };

        m.RequestTransition(ConversationTrigger.StartVoiceChat);

        Assert.Equal(new[] { "Idle->Connecting", "Connecting->Listening" }, order);
        Assert.Equal(DeviceState.Listening, m.CurrentState);
    }

    [Fact]
    public void ThrowingSubscriber_DoesNotBreakOthers_OrFutureTransitions()
    {
        var m = new Machine();
        var received = 0;
        m.StateChanged += (_, _) => throw new InvalidOperationException("boom");
        m.StateChanged += (_, _) => received++;

        m.RequestTransition(ConversationTrigger.StartVoiceChat);
        m.RequestTransition(ConversationTrigger.StopVoiceChat);

        Assert.Equal(2, received);
        Assert.Equal(DeviceState.Idle, m.CurrentState);
    }

    [Fact]
    public async Task ConcurrentTransitions_NeverLoseOrReorderEvents()
    {
        var m = new Machine { KeepListening = true };
        var events = new List<StateTransitionEventArgs>();
        var gate = new object();
        m.StateChanged += (_, e) => { lock (gate) events.Add(e); };

        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                m.RequestTransition(ConversationTrigger.StartVoiceChat);
                m.RequestTransition(ConversationTrigger.ServerConnected);
                m.RequestTransition(ConversationTrigger.TtsStarted);
                m.RequestTransition(ConversationTrigger.TtsCompleted);
                m.RequestTransition(ConversationTrigger.StopVoiceChat);
            }
        }));
        await Task.WhenAll(tasks);

        lock (gate)
        {
            // 事件链必须首尾相接：每个事件的 From 等于上一个事件的 To，Epoch 严格递增
            for (var i = 1; i < events.Count; i++)
            {
                Assert.Equal(events[i - 1].ToState, events[i].FromState);
                Assert.Equal(events[i - 1].Epoch + 1, events[i].Epoch);
            }
            Assert.Equal(m.Epoch, events[^1].Epoch);
            Assert.Equal(m.CurrentState, events[^1].ToState);
        }
    }

    [Fact]
    public void History_KeepsRecentTransitions()
    {
        var m = ToState(DeviceState.Speaking);
        var history = m.GetHistory();

        Assert.Equal(3, history.Count);
        Assert.Equal(ConversationTrigger.TtsStarted, history[^1].Trigger);
        Assert.Equal(DeviceState.Speaking, history[^1].To);
    }

    [Fact]
    public void CanTransition_ReflectsCurrentState()
    {
        var m = new Machine();
        Assert.True(m.CanTransition(ConversationTrigger.StartVoiceChat));
        Assert.False(m.CanTransition(ConversationTrigger.TtsCompleted));

        m.RequestTransition(ConversationTrigger.StartVoiceChat);
        m.RequestTransition(ConversationTrigger.ServerConnected);

        Assert.True(m.CanTransition(ConversationTrigger.TtsStarted));
        Assert.True(m.CanTransition(ConversationTrigger.StopVoiceChat));
        Assert.False(m.CanTransition(ConversationTrigger.TtsCompleted));
    }

    [Fact]
    public void Reset_ReturnsToIdle()
    {
        var m = ToState(DeviceState.Speaking);
        m.Reset();
        Assert.Equal(DeviceState.Idle, m.CurrentState);
    }
}

public class ConversationStateMachineContextTests
{
    private static Machine NewMachine() => new() { KeepListening = true };

    private static ConversationStateMachineContext NewContext(Machine m, List<string> log)
    {
        return new ConversationStateMachineContext(m)
        {
            ListeningStartDelay = TimeSpan.Zero,
            OnEnterIdle = async () => { log.Add("enter:Idle"); await Task.Yield(); },
            OnEnterConnecting = async () => { log.Add("enter:Connecting"); await Task.Yield(); },
            OnExitConnecting = async () => { log.Add("exit:Connecting"); await Task.Yield(); },
            OnEnterListening = async () => { log.Add("enter:Listening"); await Task.Yield(); },
            OnExitListening = async () => { log.Add("exit:Listening"); await Task.Yield(); },
            OnEnterSpeaking = async () => { log.Add("enter:Speaking"); await Task.Yield(); },
            OnExitSpeaking = async () => { log.Add("exit:Speaking"); await Task.Yield(); },
        };
    }

    [Fact]
    public async Task ExitAlwaysRunsBeforeEnter_InTransitionOrder()
    {
        var m = NewMachine();
        var log = new List<string>();
        using var ctx = NewContext(m, log);

        m.RequestTransition(ConversationTrigger.StartVoiceChat);
        await ctx.WaitForIdleAsync();
        m.RequestTransition(ConversationTrigger.ServerConnected);
        await ctx.WaitForIdleAsync();
        m.RequestTransition(ConversationTrigger.TtsStarted);
        await ctx.WaitForIdleAsync();
        m.RequestTransition(ConversationTrigger.TtsCompleted);
        await ctx.WaitForIdleAsync();
        m.RequestTransition(ConversationTrigger.StopVoiceChat);
        await ctx.WaitForIdleAsync();

        Assert.Equal(new[]
        {
            "enter:Connecting",
            "exit:Connecting", "enter:Listening",
            "exit:Listening", "enter:Speaking",
            "exit:Speaking", "enter:Listening",
            "exit:Listening", "enter:Idle",
        }, log);
    }

    [Fact]
    public async Task RapidTransitions_DoNotOverlapHandlers()
    {
        var m = NewMachine();
        var running = 0;
        var maxConcurrent = 0;

        async Task Track()
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref maxConcurrent, now);
            await Task.Delay(5);
            Interlocked.Decrement(ref running);
        }

        using var ctx = new ConversationStateMachineContext(m)
        {
            ListeningStartDelay = TimeSpan.Zero,
            OnEnterConnecting = Track,
            OnEnterListening = Track,
            OnExitListening = Track,
            OnEnterIdle = Track,
        };

        for (var i = 0; i < 20; i++)
        {
            m.RequestTransition(ConversationTrigger.StartVoiceChat);
            m.RequestTransition(ConversationTrigger.ServerConnected);
            m.RequestTransition(ConversationTrigger.StopVoiceChat);
        }

        await ctx.WaitForIdleAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(1, maxConcurrent);

        static void InterlockedMax(ref int target, int value)
        {
            int cur;
            while (value > (cur = Volatile.Read(ref target)))
            {
                Interlocked.CompareExchange(ref target, value, cur);
            }
        }
    }

    [Fact]
    public async Task SupersededState_NeverRunsItsEnterAction()
    {
        var m = NewMachine();
        var log = new List<string>();
        var release = new TaskCompletionSource();

        using var ctx = new ConversationStateMachineContext(m)
        {
            ListeningStartDelay = TimeSpan.Zero,
            // 第一个进入动作被卡住，让后面的转换在队列里堆积
            OnEnterConnecting = async () => { log.Add("enter:Connecting"); await release.Task; },
            OnEnterListening = () => { log.Add("enter:Listening"); return Task.CompletedTask; },
            OnExitListening = () => { log.Add("exit:Listening"); return Task.CompletedTask; },
            OnEnterIdle = () => { log.Add("enter:Idle"); return Task.CompletedTask; },
        };

        m.RequestTransition(ConversationTrigger.StartVoiceChat);       // Connecting（卡住）
        await Task.Delay(50);
        m.RequestTransition(ConversationTrigger.ServerConnected);      // Listening（排队）
        m.RequestTransition(ConversationTrigger.StopVoiceChat);        // Idle（排队，取代 Listening）
        release.SetResult();
        await ctx.WaitForIdleAsync();

        Assert.DoesNotContain("enter:Listening", log);
        Assert.DoesNotContain("exit:Listening", log);
        Assert.Equal("enter:Idle", log[^1]);
    }

    [Fact]
    public async Task ListeningStartDelay_IsSkipped_WhenInterruptedDuringTheDelay()
    {
        var m = NewMachine();
        var entered = false;

        using var ctx = new ConversationStateMachineContext(m)
        {
            ListeningStartDelay = TimeSpan.FromMilliseconds(200),
            OnEnterListening = () => { entered = true; return Task.CompletedTask; },
        };

        m.RequestTransition(ConversationTrigger.StartVoiceChat);
        m.RequestTransition(ConversationTrigger.ServerConnected);
        await Task.Delay(40);
        m.RequestTransition(ConversationTrigger.StopVoiceChat);
        await ctx.WaitForIdleAsync();

        Assert.False(entered);
    }

    [Fact]
    public async Task ConnectingTimeout_FiresConnectionFailed_AndReturnsIdle()
    {
        var m = NewMachine();
        using var ctx = new ConversationStateMachineContext(m)
        {
            ConnectingTimeout = TimeSpan.FromMilliseconds(80),
            OnEnterConnecting = () => Task.CompletedTask,
        };

        m.RequestTransition(ConversationTrigger.StartVoiceChat);
        await WaitForState(m, DeviceState.Idle, TimeSpan.FromSeconds(3));

        Assert.Equal(DeviceState.Idle, m.CurrentState);
        Assert.Contains(m.GetHistory(), r => r.Trigger == ConversationTrigger.ConnectionFailed);
    }

    [Fact]
    public async Task ConnectingTimeout_IsCancelled_WhenConnectionSucceeds()
    {
        var m = NewMachine();
        using var ctx = new ConversationStateMachineContext(m)
        {
            ConnectingTimeout = TimeSpan.FromMilliseconds(150),
            ListeningStartDelay = TimeSpan.Zero,
            OnEnterConnecting = () => Task.CompletedTask,
        };

        m.RequestTransition(ConversationTrigger.StartVoiceChat);
        m.RequestTransition(ConversationTrigger.ServerConnected);
        await Task.Delay(400);

        Assert.Equal(DeviceState.Listening, m.CurrentState);
    }

    [Fact]
    public async Task HandlerException_DoesNotStopLaterTransitions()
    {
        var m = NewMachine();
        var idleEntered = 0;

        using var ctx = new ConversationStateMachineContext(m)
        {
            ListeningStartDelay = TimeSpan.Zero,
            OnEnterConnecting = () => throw new InvalidOperationException("connect failed"),
            OnEnterIdle = () => { idleEntered++; return Task.CompletedTask; },
        };

        m.RequestTransition(ConversationTrigger.StartVoiceChat);
        await ctx.WaitForIdleAsync();
        m.RequestTransition(ConversationTrigger.ConnectionFailed);
        await ctx.WaitForIdleAsync();

        Assert.Equal(1, idleEntered);
    }

    [Fact]
    public async Task Dispose_StopsProcessing_WithoutThrowing()
    {
        var m = NewMachine();
        var log = new List<string>();
        var ctx = NewContext(m, log);
        ctx.Dispose();
        ctx.Dispose();

        m.RequestTransition(ConversationTrigger.StartVoiceChat);
        await Task.Delay(50);

        Assert.Empty(log);
    }

    private static async Task WaitForState(Machine m, DeviceState state, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (m.CurrentState != state && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }
}
