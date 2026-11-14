using System.Net;
using System.Text.Json;
using Verdure.Assistant.Core.Services.DeepSeek;
using Xunit;
using static DeepSeek.Tests.TestData;

namespace DeepSeek.Tests;

public class ChatSessionTests
{
    private sealed record Weather(string City, int Temp);

    private static (DeepSeekChatSession Session, FakeHandler Handler) Create(
        Action<DeepSeekOptions>? configure = null, IDeepSeekToolExecutor? tools = null)
    {
        var (client, handler, _) = CreateClient(configure);
        return (new DeepSeekChatSession(client, toolExecutor: tools), handler);
    }

    private static Task<List<DeepSeekChatEvent>> Run(DeepSeekChatSession session, string text, CancellationToken ct = default) =>
        ToListAsync(session.SendAsync(text, ct));

    [Fact]
    public async Task SimpleTurn_EmitsOrderedEvents_AndStoresHistory()
    {
        var (session, handler) = Create();
        handler.EnqueueSse(TextSse("你好", reasoning: "想"));

        var events = await Run(session, "嗨");

        Assert.IsType<DeepSeekChatEvent.ReasoningDelta>(events[0]);
        Assert.Equal("想", ((DeepSeekChatEvent.ReasoningDelta)events[0]).Text);
        Assert.Equal(new[] { "你", "好" }, events.OfType<DeepSeekChatEvent.ContentDelta>().Select(e => e.Text));
        var usage = Assert.Single(events.OfType<DeepSeekChatEvent.UsageReported>()).Usage;
        Assert.Equal(15, usage.TotalTokens);
        Assert.Equal(4, usage.PromptCacheHitTokens);
        Assert.Equal(2, usage.ReasoningTokens);

        var completed = Assert.IsType<DeepSeekChatEvent.Completed>(events[^1]);
        Assert.Equal("你好", completed.Content);
        Assert.Equal("想", completed.ReasoningContent);
        Assert.Equal("stop", completed.FinishReason);
        Assert.Equal(15, completed.Usage!.TotalTokens);
        Assert.DoesNotContain(events, e => e is DeepSeekChatEvent.Failed);

        var history = session.History;
        Assert.Equal(2, history.Count);
        Assert.Equal("user", history[0].Role);
        Assert.Equal("嗨", history[0].Content);
        Assert.Equal("assistant", history[1].Role);
        Assert.Equal("你好", history[1].Content);
        Assert.Equal("想", history[1].ReasoningContent);

        var messages = handler.Requests[0].Json["messages"]!.AsArray();
        Assert.Equal("system", (string?)messages[0]!["role"]);
        Assert.Contains("绿荫助手", (string?)messages[0]!["content"]);
        Assert.Equal("嗨", (string?)messages[1]!["content"]);
        Assert.Null(handler.Requests[0].Json["tools"]);
    }

    [Fact]
    public async Task ToolLoop_ExecutesTool_AndRoundTripsReasoning()
    {
        var tools = new FakeToolExecutor();
        var (session, handler) = Create(tools: tools);
        handler.EnqueueSse(ToolCallSse("call_1", "get_weather", "需要查天气", "{\"city\":", "\"杭州\"}"))
            .EnqueueSse(TextSse("杭州 20 度", reasoning: "整理结果"));

        var events = await Run(session, "杭州天气？");

        var call = Assert.Single(tools.Calls);
        Assert.Equal("get_weather", call.Name);
        Assert.Equal("{\"city\":\"杭州\"}", call.Args);

        var requested = Assert.Single(events.OfType<DeepSeekChatEvent.ToolCallRequested>());
        Assert.Equal(("call_1", "get_weather", "{\"city\":\"杭州\"}"), (requested.CallId, requested.Name, requested.ArgumentsJson));
        var done = Assert.Single(events.OfType<DeepSeekChatEvent.ToolCallCompleted>());
        Assert.False(done.IsError);
        Assert.Equal("{\"temp\":20}", done.Result);
        Assert.True(events.IndexOf(requested) < events.IndexOf(done));

        var completed = Assert.IsType<DeepSeekChatEvent.Completed>(events[^1]);
        Assert.Equal("杭州 20 度", completed.Content);
        Assert.Equal(28 + 15, completed.Usage!.TotalTokens);
        Assert.Equal(2, events.OfType<DeepSeekChatEvent.UsageReported>().Count());

        Assert.Equal(2, handler.Requests.Count);
        foreach (var req in handler.Requests)
        {
            Assert.Equal("get_weather", (string?)req.Json["tools"]![0]!["function"]!["name"]);
        }

        var second = handler.Requests[1].Json["messages"]!.AsArray();
        Assert.Equal(4, second.Count);
        Assert.Equal("assistant", (string?)second[2]!["role"]);
        Assert.Equal("", (string?)second[2]!["content"]);
        Assert.Equal("需要查天气", (string?)second[2]!["reasoning_content"]);
        Assert.Equal("call_1", (string?)second[2]!["tool_calls"]![0]!["id"]);
        Assert.Equal("{\"city\":\"杭州\"}", (string?)second[2]!["tool_calls"]![0]!["function"]!["arguments"]);
        Assert.Equal("tool", (string?)second[3]!["role"]);
        Assert.Equal("call_1", (string?)second[3]!["tool_call_id"]);
        Assert.Equal("{\"temp\":20}", (string?)second[3]!["content"]);

        Assert.Equal(new[] { "user", "assistant", "tool", "assistant" }, session.History.Select(m => m.Role));
    }

    [Fact]
    public async Task ReasoningContent_StaysInHistoryAcrossTurns_WhenToolsDeclared()
    {
        var (session, handler) = Create(tools: new FakeToolExecutor());
        handler.EnqueueSse(TextSse("好的", reasoning: "第一轮思考"))
            .EnqueueSse(TextSse("再见", reasoning: "第二轮思考"));

        await Run(session, "你好");
        await Run(session, "再见");

        var messages = handler.Requests[1].Json["messages"]!.AsArray();
        Assert.Equal("assistant", (string?)messages[2]!["role"]);
        Assert.Equal("第一轮思考", (string?)messages[2]!["reasoning_content"]);
    }

    [Fact]
    public async Task ReasoningContent_IsStrippedFromRequests_WhenNoTools()
    {
        var (session, handler) = Create();
        handler.EnqueueSse(TextSse("好的", reasoning: "第一轮思考")).EnqueueSse(TextSse("再见"));

        await Run(session, "你好");
        await Run(session, "再见");

        var messages = handler.Requests[1].Json["messages"]!.AsArray();
        Assert.Equal(4, messages.Count);
        Assert.All(messages, m => Assert.Null(m!["reasoning_content"]));
        // 历史里仍保留，便于之后声明工具时回传
        Assert.Equal("第一轮思考", session.History[1].ReasoningContent);
    }

    [Fact]
    public async Task ToolFailure_IsReturnedToModelAsErrorJson()
    {
        var tools = new FakeToolExecutor
        {
            Handler = (_, _, _) => throw new InvalidOperationException("device offline")
        };
        var (session, handler) = Create(tools: tools);
        handler.EnqueueSse(ToolCallSse("call_1", "get_weather", "r", "{}"))
            .EnqueueSse(TextSse("抱歉，设备离线"));

        var events = await Run(session, "查天气");

        var done = Assert.Single(events.OfType<DeepSeekChatEvent.ToolCallCompleted>());
        Assert.True(done.IsError);
        Assert.Contains("device offline", done.Result);
        Assert.IsType<DeepSeekChatEvent.Completed>(events[^1]);

        var toolMessage = handler.Requests[1].Json["messages"]!.AsArray()[3]!;
        var payload = JsonDocument.Parse((string)toolMessage["content"]!);
        Assert.Equal("device offline", payload.RootElement.GetProperty("error").GetString());
        Assert.Equal("get_weather", payload.RootElement.GetProperty("tool").GetString());
    }

    [Fact]
    public async Task ParallelToolCalls_RunConcurrently_AndResultsKeepCallOrder()
    {
        var started = 0;
        var barrier = new TaskCompletionSource();
        var tools = new FakeToolExecutor
        {
            Handler = async (name, _, ct) =>
            {
                if (Interlocked.Increment(ref started) == 2)
                {
                    barrier.SetResult();
                }

                // 若串行执行，第二个调用永远不会开始，这里会超时
                await barrier.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
                return $"{name}-result";
            }
        };
        tools.Tools.Add(DeepSeekToolDefinition.ForFunction("get_date", null));
        var (session, handler) = Create(tools: tools);

        var sb = new System.Text.StringBuilder();
        sb.Append(Chunk(reasoning: "并行"));
        sb.Append(Chunk(toolCalls: ToolFragment(0, "call_a", "get_weather", "{\"city\":\"京\"}")));
        sb.Append(Chunk(toolCalls: ToolFragment(1, "call_b", "get_date", "{}")));
        sb.Append(Chunk(finish: "tool_calls"));
        sb.Append(Done);
        handler.EnqueueSse(sb.ToString()).EnqueueSse(TextSse("完成"));

        var events = await Run(session, "同时查");

        Assert.Equal(2, events.OfType<DeepSeekChatEvent.ToolCallRequested>().Count());
        Assert.All(events.OfType<DeepSeekChatEvent.ToolCallCompleted>(), e => Assert.False(e.IsError));
        Assert.IsType<DeepSeekChatEvent.Completed>(events[^1]);

        var second = handler.Requests[1].Json["messages"]!.AsArray();
        Assert.Equal(2, second[2]!["tool_calls"]!.AsArray().Count);
        Assert.Equal("call_a", (string?)second[3]!["tool_call_id"]);
        Assert.Equal("get_weather-result", (string?)second[3]!["content"]);
        Assert.Equal("call_b", (string?)second[4]!["tool_call_id"]);
        Assert.Equal("get_date-result", (string?)second[4]!["content"]);
    }

    [Fact]
    public async Task ToolLoop_ExceedingMaxRounds_FailsAndRollsBack()
    {
        var (session, handler) = Create(tools: new FakeToolExecutor());
        for (var i = 0; i < DeepSeekChatSession.MaxToolRounds; i++)
        {
            handler.EnqueueSse(ToolCallSse($"call_{i}", "get_weather", "r", "{}"));
        }

        var events = await Run(session, "无限循环");

        var failed = Assert.IsType<DeepSeekChatEvent.Failed>(events[^1]);
        Assert.IsType<InvalidOperationException>(failed.Exception);
        Assert.False(failed.IsCancelled);
        Assert.Equal(8, handler.Requests.Count);
        Assert.Empty(session.History);
        Assert.False(session.IsBusy);
    }

    [Fact]
    public async Task ModelRequestsToolWithoutExecutor_Fails()
    {
        var (session, handler) = Create();
        handler.EnqueueSse(ToolCallSse("call_1", "x", "r", "{}"));

        var events = await Run(session, "hi");
        Assert.IsType<InvalidOperationException>(Assert.IsType<DeepSeekChatEvent.Failed>(events[^1]).Exception);
        Assert.Empty(session.History);
    }

    [Fact]
    public async Task ApiError_YieldsFailed_AndLeavesHistoryUntouched_AndSessionStaysUsable()
    {
        var (session, handler) = Create();
        handler.EnqueueError(HttpStatusCode.Unauthorized).EnqueueSse(TextSse("ok"));

        var events = await Run(session, "hi");
        var failed = Assert.IsType<DeepSeekChatEvent.Failed>(Assert.Single(events));
        Assert.Equal(HttpStatusCode.Unauthorized, Assert.IsType<DeepSeekApiException>(failed.Exception).StatusCode);
        Assert.Empty(session.History);

        var again = await Run(session, "hi");
        Assert.IsType<DeepSeekChatEvent.Completed>(again[^1]);
        Assert.Equal(2, session.History.Count);
    }

    [Fact]
    public async Task MidStreamFailure_RollsBackPartialTurn()
    {
        var tools = new FakeToolExecutor();
        var (session, handler) = Create(tools: tools);
        handler.EnqueueSse(ToolCallSse("call_1", "get_weather", "r", "{}"))
            .EnqueueError(HttpStatusCode.BadRequest);

        var events = await Run(session, "hi");

        Assert.IsType<DeepSeekChatEvent.Failed>(events[^1]);
        Assert.Single(tools.Calls);
        Assert.Empty(session.History);
    }

    [Fact]
    public async Task CancelCurrent_DuringStreaming_RollsBackAndSessionRecovers()
    {
        var (session, handler) = Create();
        handler.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream(Chunk(content: "半句")))
        }));
        handler.EnqueueSse(TextSse("新的回答"));

        var events = new List<DeepSeekChatEvent>();
        await foreach (var ev in session.SendAsync("第一个问题"))
        {
            events.Add(ev);
            if (ev is DeepSeekChatEvent.ContentDelta)
            {
                Assert.True(session.IsBusy);
                session.CancelCurrent();
            }
        }

        var failed = Assert.IsType<DeepSeekChatEvent.Failed>(events[^1]);
        Assert.True(failed.IsCancelled);
        Assert.Empty(session.History);
        Assert.False(session.IsBusy);

        var next = await Run(session, "第二个问题");
        Assert.IsType<DeepSeekChatEvent.Completed>(next[^1]);
        Assert.Equal(new[] { "第二个问题", "新的回答" }, session.History.Select(m => m.Content));
        var secondRequestMessages = handler.Requests[1].Json["messages"]!.AsArray();
        Assert.DoesNotContain(secondRequestMessages, m => (string?)m!["content"] == "第一个问题");
    }

    [Fact]
    public async Task CallerCancellation_RollsBack()
    {
        var (session, handler) = Create();
        handler.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream(Chunk(content: "x")))
        }));

        using var cts = new CancellationTokenSource();
        var events = new List<DeepSeekChatEvent>();
        await foreach (var ev in session.SendAsync("q", cts.Token))
        {
            events.Add(ev);
            if (ev is DeepSeekChatEvent.ContentDelta)
            {
                await cts.CancelAsync();
            }
        }

        Assert.True(Assert.IsType<DeepSeekChatEvent.Failed>(events[^1]).IsCancelled);
        Assert.Empty(session.History);
    }

    [Fact]
    public async Task CancelCurrent_DuringToolExecution_RollsBack()
    {
        var executing = new TaskCompletionSource();
        var tools = new FakeToolExecutor
        {
            Handler = async (_, _, ct) =>
            {
                executing.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return "never";
            }
        };
        var (session, handler) = Create(tools: tools);
        handler.EnqueueSse(ToolCallSse("call_1", "get_weather", "r", "{}"));

        var events = new List<DeepSeekChatEvent>();
        await foreach (var ev in session.SendAsync("q"))
        {
            events.Add(ev);
            if (ev is DeepSeekChatEvent.ToolCallRequested)
            {
                // 工具在消费者处理完 Requested 事件、迭代器恢复后才开始执行
                _ = Task.Run(async () =>
                {
                    await executing.Task;
                    session.CancelCurrent();
                });
            }
        }

        Assert.True(Assert.IsType<DeepSeekChatEvent.Failed>(events[^1]).IsCancelled);
        Assert.DoesNotContain(events, e => e is DeepSeekChatEvent.ToolCallCompleted);
        Assert.Empty(session.History);
    }

    [Fact]
    public void CancelCurrent_WithoutTurn_IsHarmless()
    {
        var (session, _) = Create();
        session.CancelCurrent();
        session.CancelCurrent();
        Assert.False(session.IsBusy);
    }

    [Fact]
    public async Task SecondConcurrentTurn_IsRejected_UntilFirstEnds()
    {
        var (session, handler) = Create();
        handler.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream(Chunk(content: "x")))
        }));

        await using var first = session.SendAsync("a").GetAsyncEnumerator();
        Assert.True(await first.MoveNextAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in session.SendAsync("b"))
            {
            }
        });

        session.CancelCurrent();
        while (await first.MoveNextAsync())
        {
        }

        Assert.False(session.IsBusy);
        Assert.Empty(session.History);
    }

    [Fact]
    public async Task AbandonedEnumeration_ReleasesTheSession()
    {
        var (session, handler) = Create();
        handler.EnqueueSse(TextSse("abc")).EnqueueSse(TextSse("def"));

        await foreach (var _ in session.SendAsync("a"))
        {
            break;
        }

        Assert.False(session.IsBusy);
        Assert.Empty(session.History);
        var events = await Run(session, "b");
        Assert.IsType<DeepSeekChatEvent.Completed>(events[^1]);
    }

    [Fact]
    public async Task EmptyUserText_Throws()
    {
        var (session, _) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() => Run(session, "  "));
        await Assert.ThrowsAsync<ArgumentException>(() => session.AskAsync(""));
    }

    private static async Task<DeepSeekChatSession> SessionWithTurns(int plainTurns, bool firstWithTool, int maxHistory)
    {
        var tools = new FakeToolExecutor();
        var (session, handler) = Create(o => o.MaxHistoryMessages = maxHistory, tools);
        if (firstWithTool)
        {
            handler.EnqueueSse(ToolCallSse("call_0", "get_weather", "r", "{}")).EnqueueSse(TextSse("t0"));
            await Run(session, "u0");
        }

        for (var i = 1; i <= plainTurns; i++)
        {
            handler.EnqueueSse(TextSse($"a{i}"));
            await Run(session, $"u{i}");
        }

        return session;
    }

    private static void AssertConsistent(IReadOnlyList<DeepSeekMessage> history)
    {
        Assert.NotEmpty(history);
        Assert.Equal("user", history[0].Role);
        for (var i = 0; i < history.Count; i++)
        {
            if (history[i].Role != "tool")
            {
                continue;
            }

            var j = i;
            while (j > 0 && history[j].Role == "tool")
            {
                j--;
            }

            Assert.Equal("assistant", history[j].Role);
            Assert.NotEmpty(history[j].ToolCalls!);
        }
    }

    [Fact]
    public async Task Trimming_DropsWholeOldestTurns_NeverOrphansToolMessages()
    {
        // 第 1 轮含工具（4 条），第 2 轮 2 条，上限 5 => 总数 6 超限，丢弃整个第 1 轮
        var session = await SessionWithTurns(plainTurns: 1, firstWithTool: true, maxHistory: 5);
        var history = session.History;
        Assert.Equal(new[] { "u1", "a1" }, history.Select(m => m.Content));
        AssertConsistent(history);

        // 继续增长：上限 5 时最多保留 2 个普通轮次（4 条）
        var more = await SessionWithTurns(plainTurns: 3, firstWithTool: true, maxHistory: 5);
        Assert.Equal(new[] { "u2", "a2", "u3", "a3" }, more.History.Select(m => m.Content));
        AssertConsistent(more.History);
    }

    [Fact]
    public async Task Trimming_KeepsLatestTurnEvenWhenItExceedsLimit()
    {
        var session = await SessionWithTurns(plainTurns: 0, firstWithTool: true, maxHistory: 2);
        Assert.Equal(4, session.History.Count);
        AssertConsistent(session.History);
    }

    [Fact]
    public async Task Trimming_Disabled_WhenMaxIsZero()
    {
        var session = await SessionWithTurns(plainTurns: 6, firstWithTool: true, maxHistory: 0);
        Assert.Equal(4 + 12, session.History.Count);
    }

    [Fact]
    public async Task Trimming_AffectsNextRequest()
    {
        var (session, handler) = Create(o => o.MaxHistoryMessages = 2);
        handler.EnqueueSse(TextSse("a1")).EnqueueSse(TextSse("a2")).EnqueueSse(TextSse("a3"));
        await Run(session, "u1");
        await Run(session, "u2");
        await Run(session, "u3");

        var messages = handler.Requests[2].Json["messages"]!.AsArray();
        Assert.Equal(new[] { "system", "user", "assistant", "user" }, messages.Select(m => (string?)m!["role"]));
        Assert.Equal("u2", (string?)messages[1]!["content"]);
    }

    [Fact]
    public async Task Reset_ClearsHistory()
    {
        var (session, handler) = Create();
        handler.EnqueueSse(TextSse("a")).EnqueueSse(TextSse("b"));
        await Run(session, "u");
        Assert.NotEmpty(session.History);

        session.Reset();
        Assert.Empty(session.History);

        await Run(session, "u2");
        Assert.Equal(2, handler.Requests[1].Json["messages"]!.AsArray().Count);
    }

    [Fact]
    public async Task Reset_DuringTurn_CancelsAndDiscardsTheTurn()
    {
        var (session, handler) = Create();
        handler.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream(Chunk(content: "x")))
        }));

        var events = new List<DeepSeekChatEvent>();
        await foreach (var ev in session.SendAsync("q"))
        {
            events.Add(ev);
            if (ev is DeepSeekChatEvent.ContentDelta)
            {
                session.Reset();
            }
        }

        Assert.True(Assert.IsType<DeepSeekChatEvent.Failed>(events[^1]).IsCancelled);
        Assert.Empty(session.History);
    }

    [Fact]
    public async Task SetSystemPrompt_ChangesNextRequest_AndNullRemovesIt()
    {
        var (session, handler) = Create();
        handler.EnqueueSse(TextSse("a")).EnqueueSse(TextSse("b"));

        session.SetSystemPrompt("你是海盗");
        await Run(session, "u1");
        Assert.Equal("你是海盗", (string?)handler.Requests[0].Json["messages"]![0]!["content"]);
        Assert.Equal("你是海盗", session.SystemPrompt);

        session.SetSystemPrompt(null);
        await Run(session, "u2");
        Assert.Equal("user", (string?)handler.Requests[1].Json["messages"]![0]!["role"]);
        Assert.Null(session.SystemPrompt);
    }

    [Fact]
    public async Task History_IsASnapshot()
    {
        var (session, handler) = Create();
        handler.EnqueueSse(TextSse("a"));
        var before = session.History;
        await Run(session, "u");
        Assert.Empty(before);
        Assert.Equal(2, session.History.Count);
    }

    [Fact]
    public async Task AskAsync_UsesNonStreamingEndpoint_AndKeepsHistory()
    {
        var (session, handler) = Create();
        handler.EnqueueJson(Completion("你好呀", "想"));

        var answer = await session.AskAsync("嗨");

        Assert.Equal("你好呀", answer);
        Assert.Null(handler.Requests[0].Json["stream"]);
        Assert.Equal(new[] { "嗨", "你好呀" }, session.History.Select(m => m.Content));
        Assert.Equal("想", session.History[1].ReasoningContent);
    }

    [Fact]
    public async Task AskAsync_RunsToolLoop_WithoutStreaming()
    {
        var tools = new FakeToolExecutor();
        var (session, handler) = Create(tools: tools);
        handler.EnqueueJson(Completion("", "查一下", "tool_calls", new[]
            {
                new { id = "call_1", type = "function", function = new { name = "get_weather", arguments = "{\"city\":\"京\"}" } }
            }))
            .EnqueueJson(Completion("北京 20 度"));

        var answer = await session.AskAsync("北京天气");

        Assert.Equal("北京 20 度", answer);
        Assert.Single(tools.Calls);
        Assert.Equal("查一下", (string?)handler.Requests[1].Json["messages"]![2]!["reasoning_content"]);
        Assert.Equal(4, session.History.Count);
    }

    [Fact]
    public async Task AskAsync_Failure_Throws_AndRollsBack()
    {
        var (session, handler) = Create(o => o.MaxRetries = 0);
        handler.EnqueueError(HttpStatusCode.PaymentRequired);

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => session.AskAsync("hi"));
        Assert.True(ex.IsInsufficientBalance);
        Assert.Empty(session.History);
    }

    [Fact]
    public async Task AskAsync_Cancellation_ThrowsOperationCanceled()
    {
        var (session, handler) = Create();
        handler.Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.AskAsync("hi", cts.Token));
        Assert.Empty(session.History);
    }

    [Fact]
    public async Task AskJsonAsync_UsesJsonMode_AddsJsonInstruction_AndLeavesHistoryAlone()
    {
        var (session, handler) = Create();
        handler.EnqueueJson(Completion("{\"city\":\"杭州\",\"temp\":20}"));

        var result = await session.AskJsonAsync<Weather>("杭州现在多少度？");

        Assert.Equal(new Weather("杭州", 20), result);
        var json = handler.Requests[0].Json;
        Assert.Equal("json_object", (string?)json["response_format"]!["type"]);
        Assert.Contains("json", (string?)json["messages"]![0]!["content"], StringComparison.OrdinalIgnoreCase);
        Assert.Null(json["tools"]);
        Assert.Empty(session.History);
    }

    [Fact]
    public async Task AskJsonAsync_WithWebToolsEnabled_StillSendsNoTools()
    {
        var (session, handler) = Create(o => o.EnableWebSearch = true);
        handler.EnqueueJson(Completion("{\"city\":\"a\",\"temp\":1}"));
        await session.AskJsonAsync<Weather>("x");
        Assert.Null(handler.Requests[0].Json["tools"]);
    }

    [Fact]
    public async Task AskJsonAsync_EmptyContent_ReturnsDefault_AndTruncationThrows()
    {
        var (session, handler) = Create();
        handler.EnqueueJson(Completion("      "));
        Assert.Null(await session.AskJsonAsync<Weather>("x"));

        handler.EnqueueJson(Completion("{\"city\":", finish: "length"));
        await Assert.ThrowsAsync<JsonException>(() => session.AskJsonAsync<Weather>("x"));

        handler.EnqueueJson(Completion("not json"));
        await Assert.ThrowsAsync<JsonException>(() => session.AskJsonAsync<Weather>("x"));
    }

    [Fact]
    public async Task EndToEnd_WithMcpExecutor_CallsRegisteredTool()
    {
        var server = new Verdure.Assistant.Core.Services.MCP.McpServer();
        string? played = null;
        server.AddTool("self.music.play", "播放音乐",
            new Verdure.Assistant.Core.Services.MCP.McpPropertyList
            {
                new Verdure.Assistant.Core.Services.MCP.McpProperty("song", Verdure.Assistant.Core.Services.MCP.McpPropertyType.String, "歌曲名")
            },
            props =>
            {
                played = props["song"].GetValue<string>();
                return Task.FromResult<Verdure.Assistant.Core.Services.MCP.McpReturnValue>("已播放");
            });

        var (session, handler) = Create(tools: new McpDeepSeekToolExecutor(server));
        handler.EnqueueSse(ToolCallSse("call_1", "self_music_play", "用户要听歌", "{\"song\":\"晴天\"}"))
            .EnqueueSse(TextSse("好的，正在播放晴天"));

        var events = await Run(session, "放首晴天");

        Assert.Equal("晴天", played);
        Assert.Equal("self_music_play", (string?)handler.Requests[0].Json["tools"]![0]!["function"]!["name"]);
        Assert.IsType<DeepSeekChatEvent.Completed>(events[^1]);
        Assert.Equal("已播放", (string?)handler.Requests[1].Json["messages"]![3]!["content"]);
    }
}
