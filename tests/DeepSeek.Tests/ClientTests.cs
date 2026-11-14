using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Verdure.Assistant.Core.Services.DeepSeek;
using Xunit;
using static DeepSeek.Tests.TestData;

namespace DeepSeek.Tests;

public class OptionsAndModelsTests
{
    [Fact]
    public void Defaults_MatchSpec()
    {
        var o = new DeepSeekOptions();
        Assert.Equal("https://api.deepseek.com", o.BaseUrl);
        Assert.Equal("deepseek-flash", o.Model);
        Assert.True(o.ThinkingEnabled);
        Assert.Equal("high", o.ReasoningEffort);
        Assert.Equal(120, o.TimeoutSeconds);
        Assert.Equal(3, o.MaxRetries);
        Assert.Equal(40, o.MaxHistoryMessages);
        Assert.False(o.EnableWebSearch);
        Assert.False(o.EnableReadUrl);
        Assert.Contains("绿荫助手", o.SystemPrompt);
    }

    [Fact]
    public void Validate_ReportsProblems()
    {
        var o = new DeepSeekOptions
        {
            ApiKey = " ",
            BaseUrl = "not a url",
            Model = "",
            ReasoningEffort = "turbo",
            MaxTokens = 0,
            Temperature = 3,
            TopP = 0,
            TimeoutSeconds = 0,
            MaxRetries = 11,
            UserId = new string('x', 513)
        };

        var problems = o.Validate();
        Assert.Equal(10, problems.Count);
        Assert.Empty(TestData.Options().Validate());
    }

    [Theory]
    [InlineData("minimal", "low")]
    [InlineData("low", "low")]
    [InlineData("medium", "high")]
    [InlineData("high", "high")]
    [InlineData("xhigh", "high")]
    [InlineData("max", "max")]
    [InlineData("ultra", "max")]
    [InlineData("NONE", "none")]
    [InlineData("bogus", null)]
    [InlineData(null, null)]
    public void ReasoningEffort_Normalize(string? input, string? expected) =>
        Assert.Equal(expected, DeepSeekReasoningEffort.Normalize(input));

    [Fact]
    public void ModelNames_Helpers()
    {
        Assert.True(DeepSeekModelNames.IsCurrent("deepseek-flash"));
        Assert.True(DeepSeekModelNames.IsLegacy("deepseek-v4-flash"));
        Assert.True(DeepSeekModelNames.IsPro("deepseek-v4-pro"));
        Assert.Equal("deepseek-flash", DeepSeekModelNames.Normalize("deepseek-chat"));
        Assert.Equal("deepseek-v4-pro", DeepSeekModelNames.Normalize("deepseek-v4-pro"));
        Assert.Equal("deepseek-flash", DeepSeekModelNames.Normalize(null));
    }

    [Fact]
    public void ToolDefinition_NameValidation()
    {
        Assert.True(DeepSeekToolDefinition.IsValidFunctionName("self_music-play1"));
        Assert.False(DeepSeekToolDefinition.IsValidFunctionName("self.music.play"));
        Assert.False(DeepSeekToolDefinition.IsValidFunctionName(""));
        Assert.False(DeepSeekToolDefinition.IsValidFunctionName(new string('a', 129)));
    }

    [Fact]
    public void ToolChoice_RoundTrips()
    {
        var named = JsonSerializer.Serialize(DeepSeekToolChoice.ForFunction("f"), DeepSeekJson.Options);
        Assert.Equal("""{"type":"function","function":{"name":"f"}}""", named);
        Assert.Equal("\"required\"", JsonSerializer.Serialize(DeepSeekToolChoice.Required, DeepSeekJson.Options));

        var back = JsonSerializer.Deserialize<DeepSeekToolChoice>(named, DeepSeekJson.Options);
        Assert.Equal("f", back!.FunctionName);
        Assert.True(back.IsForced);
    }
}

public class ClientRequestTests
{
    private static ChatCompletionRequest Simple() => new()
    {
        Messages = { DeepSeekMessage.User("你好") }
    };

    private static async Task<RecordedRequest> SendAsync(Action<DeepSeekOptions>? configure, ChatCompletionRequest request)
    {
        var (client, handler, _) = CreateClient(configure);
        handler.EnqueueJson(Completion("ok"));
        await client.CreateChatCompletionAsync(request);
        return Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Default_ThinkingEnabled_OmitsTemperature_AndSetsAuth()
    {
        var req = await SendAsync(o => { o.Temperature = 0.7; o.TopP = 0.9; o.UserId = "u-1"; }, Simple());

        Assert.Equal("https://api.deepseek.com/chat/completions", req.Uri.ToString());
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("Bearer", req.Authorization!.Scheme);
        Assert.Equal("sk-test-key", req.Authorization.Parameter);

        var json = req.Json;
        Assert.Equal("deepseek-flash", (string?)json["model"]);
        Assert.Equal("enabled", (string?)json["thinking"]!["type"]);
        Assert.Equal("high", (string?)json["reasoning_effort"]);
        Assert.Null(json["temperature"]);
        Assert.Equal(0.9, (double?)json["top_p"]);
        Assert.Equal("u-1", (string?)json["user_id"]);
        Assert.Null(json["stream"]);
        Assert.Null(json["stream_options"]);
        Assert.Null(json["tools"]);
        Assert.Equal("你好", (string?)json["messages"]![0]!["content"]);
        Assert.Contains("你好", req.Body); // 不转义中文
    }

    [Fact]
    public async Task ThinkingDisabled_SendsTemperature_AndNoEffort()
    {
        var req = await SendAsync(o => { o.ThinkingEnabled = false; o.Temperature = 0.3; o.MaxTokens = 256; }, Simple());

        var json = req.Json;
        Assert.Equal("disabled", (string?)json["thinking"]!["type"]);
        Assert.Null(json["reasoning_effort"]);
        Assert.Equal(0.3, (double?)json["temperature"]);
        Assert.Equal(256, (int?)json["max_tokens"]);
    }

    [Theory]
    [InlineData("minimal", "enabled", "low")]
    [InlineData("medium", "enabled", "high")]
    [InlineData("xhigh", "enabled", "high")]
    [InlineData("max", "enabled", "max")]
    [InlineData("ultra", "enabled", "max")]
    [InlineData("none", "disabled", null)]
    public async Task RequestReasoningEffort_IsMapped(string effort, string thinking, string? expectedEffort)
    {
        var request = Simple();
        request.ReasoningEffort = effort;
        var req = await SendAsync(o => o.Temperature = 0.5, request);

        var json = req.Json;
        Assert.Equal(thinking, (string?)json["thinking"]!["type"]);
        Assert.Equal(expectedEffort, (string?)json["reasoning_effort"]);
        Assert.Equal(thinking == "disabled", json["temperature"] != null);
    }

    [Fact]
    public async Task ExplicitThinkingOff_WinsOverEffort()
    {
        var request = Simple();
        request.Thinking = DeepSeekThinking.Off;
        request.ReasoningEffort = "max";
        var json = (await SendAsync(null, request)).Json;

        Assert.Equal("disabled", (string?)json["thinking"]!["type"]);
        Assert.Null(json["reasoning_effort"]);
    }

    [Fact]
    public async Task OptionsEffortNone_DisablesThinking()
    {
        var json = (await SendAsync(o => o.ReasoningEffort = "none", Simple())).Json;
        Assert.Equal("disabled", (string?)json["thinking"]!["type"]);
    }

    [Fact]
    public async Task InvalidReasoningEffort_Throws()
    {
        var (client, _, _) = CreateClient();
        var request = Simple();
        request.ReasoningEffort = "turbo";
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateChatCompletionAsync(request));
    }

    [Fact]
    public async Task Tools_AreSerialized_WithServerTools()
    {
        var request = Simple();
        request.Tools = new()
        {
            DeepSeekToolDefinition.ForFunction("get_weather", "天气",
                """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}"""),
            DeepSeekToolDefinition.ForWebSearch()
        };
        var json = (await SendAsync(null, request)).Json;

        var tools = json["tools"]!.AsArray();
        Assert.Equal(2, tools.Count);
        Assert.Equal("function", (string?)tools[0]!["type"]);
        Assert.Equal("get_weather", (string?)tools[0]!["function"]!["name"]);
        Assert.Equal("object", (string?)tools[0]!["function"]!["parameters"]!["type"]);
        Assert.Null(tools[0]!["function"]!["strict"]);
        Assert.Null(tools[0]!["name"]);
        Assert.Equal("deepseek_web_search", (string?)tools[1]!["type"]);
        Assert.Equal("deepseek_web_search", (string?)tools[1]!["name"]);
        Assert.Null(tools[1]!["function"]);
    }

    [Fact]
    public async Task OptionsWebTools_AreAddedAutomatically_ButNotInJsonMode()
    {
        var json = (await SendAsync(o => { o.EnableWebSearch = true; o.EnableReadUrl = true; }, Simple())).Json;
        var types = json["tools"]!.AsArray().Select(t => (string?)t!["type"]).ToArray();
        Assert.Equal(new[] { "deepseek_web_search", "deepseek_read_url" }, types);

        var jsonMode = Simple();
        jsonMode.ResponseFormat = DeepSeekResponseFormat.JsonObject;
        var jsonModeBody = (await SendAsync(o => o.EnableWebSearch = true, jsonMode)).Json;
        Assert.Null(jsonModeBody["tools"]);
        Assert.Equal("json_object", (string?)jsonModeBody["response_format"]!["type"]);
    }

    [Fact]
    public async Task ToolChoiceNone_SuppressesAutoWebTools_AndIsDroppedWithoutTools()
    {
        var request = Simple();
        request.ToolChoice = DeepSeekToolChoice.None;
        var json = (await SendAsync(o => o.EnableWebSearch = true, request)).Json;
        Assert.Null(json["tools"]);
        Assert.Null(json["tool_choice"]);
    }

    [Fact]
    public async Task ForcedToolChoice_InThinkingMode_Throws()
    {
        var (client, handler, _) = CreateClient();
        foreach (var choice in new[] { DeepSeekToolChoice.Required, DeepSeekToolChoice.ForFunction("get_weather") })
        {
            var request = Simple();
            request.Tools = new() { DeepSeekToolDefinition.ForFunction("get_weather", null) };
            request.ToolChoice = choice;
            await Assert.ThrowsAsync<ArgumentException>(() => client.CreateChatCompletionAsync(request));
        }

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ForcedToolChoice_WhenThinkingDisabled_IsSent()
    {
        var request = Simple();
        request.Thinking = DeepSeekThinking.Off;
        request.Tools = new() { DeepSeekToolDefinition.ForFunction("get_weather", null) };
        request.ToolChoice = DeepSeekToolChoice.ForFunction("get_weather");
        var json = (await SendAsync(null, request)).Json;

        Assert.Equal("get_weather", (string?)json["tool_choice"]!["function"]!["name"]);

        request.ToolChoice = DeepSeekToolChoice.Required;
        Assert.Equal("required", (string?)(await SendAsync(null, request)).Json["tool_choice"]);
    }

    [Fact]
    public async Task ToolChoiceNamingUnknownFunction_Throws()
    {
        var (client, _, _) = CreateClient();
        var request = Simple();
        request.Thinking = DeepSeekThinking.Off;
        request.Tools = new() { DeepSeekToolDefinition.ForFunction("a", null) };
        request.ToolChoice = DeepSeekToolChoice.ForFunction("missing");
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateChatCompletionAsync(request));
    }

    [Fact]
    public async Task InvalidOrDuplicateToolNames_Throw()
    {
        var (client, _, _) = CreateClient();

        var bad = Simple();
        bad.Tools = new() { DeepSeekToolDefinition.ForFunction("self.music.play", null) };
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateChatCompletionAsync(bad));

        var dup = Simple();
        dup.Tools = new() { DeepSeekToolDefinition.ForFunction("a", null), DeepSeekToolDefinition.ForFunction("a", null) };
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateChatCompletionAsync(dup));
    }

    [Fact]
    public async Task WebTools_WithJsonMode_Throw()
    {
        var (client, _, _) = CreateClient();
        var request = Simple();
        request.ResponseFormat = DeepSeekResponseFormat.JsonObject;
        request.Tools = new() { DeepSeekToolDefinition.ForReadUrl() };
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateChatCompletionAsync(request));
    }

    private static ChatCompletionRequest HistoryWithReasoning(bool withTools)
    {
        var request = new ChatCompletionRequest
        {
            Messages =
            {
                DeepSeekMessage.System("sys"),
                DeepSeekMessage.User("天气？"),
                DeepSeekMessage.Assistant("", "需要查天气", new[] { DeepSeekToolCall.Create("call_1", "get_weather", "{\"city\":\"杭州\"}") }),
                DeepSeekMessage.Tool("call_1", "{\"temp\":20}"),
                DeepSeekMessage.Assistant("20度", "整理结果"),
                DeepSeekMessage.User("谢谢")
            }
        };
        if (withTools)
        {
            request.Tools = new() { DeepSeekToolDefinition.ForFunction("get_weather", null) };
        }

        return request;
    }

    [Fact]
    public async Task ReasoningContent_IsSentBack_WhenToolsPresent()
    {
        var json = (await SendAsync(null, HistoryWithReasoning(true))).Json;
        var messages = json["messages"]!.AsArray();

        Assert.Equal("需要查天气", (string?)messages[2]!["reasoning_content"]);
        Assert.Equal("整理结果", (string?)messages[4]!["reasoning_content"]);

        var call = messages[2]!["tool_calls"]![0]!;
        Assert.Equal("call_1", (string?)call["id"]);
        Assert.Equal("function", (string?)call["type"]);
        Assert.Equal("{\"city\":\"杭州\"}", (string?)call["function"]!["arguments"]);
        Assert.Null(call["index"]);
        Assert.Equal("call_1", (string?)messages[3]!["tool_call_id"]);
        Assert.Equal("", (string?)messages[2]!["content"]);
    }

    [Fact]
    public async Task ReasoningContent_IsStripped_WhenNoTools()
    {
        var json = (await SendAsync(null, HistoryWithReasoning(false))).Json;
        foreach (var message in json["messages"]!.AsArray())
        {
            Assert.Null(message!["reasoning_content"]);
        }
    }

    [Fact]
    public async Task ReasoningContent_IsSentBack_WhenOnlyServerToolsFromOptions()
    {
        var json = (await SendAsync(o => o.EnableWebSearch = true, HistoryWithReasoning(false))).Json;
        Assert.Equal("整理结果", (string?)json["messages"]![4]!["reasoning_content"]);
    }

    [Fact]
    public async Task OriginalRequest_IsNotMutated()
    {
        var request = HistoryWithReasoning(false);
        await SendAsync(o => o.Temperature = 0.2, request);

        Assert.Null(request.Model);
        Assert.Null(request.Thinking);
        Assert.Null(request.Temperature);
        Assert.Equal("整理结果", request.Messages[4].ReasoningContent);
    }

    [Fact]
    public async Task StrictTools_UseBetaEndpoint_AndRequireAllStrict()
    {
        var request = Simple();
        request.Tools = new() { DeepSeekToolDefinition.ForFunction("a", null, strict: true) };
        var req = await SendAsync(null, request);
        Assert.Equal("https://api.deepseek.com/beta/chat/completions", req.Uri.ToString());
        Assert.True((bool?)req.Json["tools"]![0]!["function"]!["strict"]);

        var (client, _, _) = CreateClient();
        var mixed = Simple();
        mixed.Tools = new()
        {
            DeepSeekToolDefinition.ForFunction("a", null, strict: true),
            DeepSeekToolDefinition.ForFunction("b", null)
        };
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateChatCompletionAsync(mixed));
    }

    [Fact]
    public async Task PrefixCompletion_UsesBetaEndpoint_AndPrefixFlag_AndDisablesThinking()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson(Completion("def f(): pass\n```"));

        var response = await client.CreatePrefixCompletionAsync(
            new[] { DeepSeekMessage.User("写快排") }, "```python\n", stop: new[] { "```" });

        var req = Assert.Single(handler.Requests);
        Assert.Equal("https://api.deepseek.com/beta/chat/completions", req.Uri.ToString());
        var messages = req.Json["messages"]!.AsArray();
        Assert.Equal("assistant", (string?)messages[1]!["role"]);
        Assert.Equal("```python\n", (string?)messages[1]!["content"]);
        Assert.True((bool?)messages[1]!["prefix"]);
        Assert.Equal("disabled", (string?)req.Json["thinking"]!["type"]);
        Assert.Equal("```", (string?)req.Json["stop"]![0]);
        Assert.Contains("def f", response.Content);
    }

    [Fact]
    public async Task BetaBaseUrl_IsNotDoubled()
    {
        var request = Simple();
        request.Messages.Add(DeepSeekMessage.AssistantPrefix("好的，"));
        var req = await SendAsync(o => o.BaseUrl = "https://api.deepseek.com/beta/", request);
        Assert.Equal("https://api.deepseek.com/beta/chat/completions", req.Uri.ToString());
    }

    [Fact]
    public async Task PrefixOnNonLastMessage_Throws()
    {
        var (client, _, _) = CreateClient();
        var request = new ChatCompletionRequest
        {
            Messages = { DeepSeekMessage.AssistantPrefix("x"), DeepSeekMessage.User("y") }
        };
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateChatCompletionAsync(request));
    }

    [Fact]
    public async Task EmptyMessagesOrTooManyStops_Throw()
    {
        var (client, _, _) = CreateClient();
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateChatCompletionAsync(new ChatCompletionRequest()));

        var request = Simple();
        request.Stop = Enumerable.Range(0, 17).Select(i => $"s{i}").ToList();
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateChatCompletionAsync(request));
    }

    [Fact]
    public async Task MissingApiKey_Throws_AndNothingIsSent()
    {
        var (client, handler, _) = CreateClient(o => o.ApiKey = "");
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateChatCompletionAsync(Simple()));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ModelOverride_IsRespected_AndLegacyNameIsSentAsIs()
    {
        var request = Simple();
        request.Model = "deepseek-v4-flash";
        Assert.Equal("deepseek-v4-flash", (string?)(await SendAsync(null, request)).Json["model"]);
    }
}

public class ClientResponseTests
{
    [Fact]
    public async Task NonStream_ParsesReasoningUsageAndCacheFields()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson(Completion("答案", "思考过程", usage: new
        {
            prompt_tokens = 100,
            completion_tokens = 50,
            total_tokens = 150,
            prompt_cache_hit_tokens = 60,
            prompt_cache_miss_tokens = 40,
            completion_tokens_details = new { reasoning_tokens = 30 }
        }));

        var response = await client.CreateChatCompletionAsync(new ChatCompletionRequest { Messages = { DeepSeekMessage.User("hi") } });

        Assert.Equal("答案", response.Content);
        Assert.Equal("思考过程", response.ReasoningContent);
        Assert.Equal("stop", response.FinishReason);
        Assert.Equal(100, response.Usage!.PromptTokens);
        Assert.Equal(60, response.Usage.PromptCacheHitTokens);
        Assert.Equal(40, response.Usage.PromptCacheMissTokens);
        Assert.Equal(30, response.Usage.ReasoningTokens);
        Assert.Empty(response.ToolCalls);
    }

    [Fact]
    public async Task NonStream_ParsesToolCalls_AndToleratesLeadingNewlines()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("\n\n" + Completion("", "r", "tool_calls", new[]
        {
            new { id = "call_1", type = "function", function = new { name = "get_weather", arguments = "{\"city\":\"北京\"}" } }
        }));

        var response = await client.CreateChatCompletionAsync(new ChatCompletionRequest { Messages = { DeepSeekMessage.User("hi") } });

        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("get_weather", call.Function!.Name);
        Assert.Equal("{\"city\":\"北京\"}", call.Function.Arguments);
        Assert.Equal("tool_calls", response.FinishReason);
    }

    [Fact]
    public async Task NonStream_ServerToolFields_AreParsedButNeverSentBack()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("""
            {"choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"人口约39万",
             "annotations":[{"type":"url_citation"}],"server_tool_calls":[{"index":0,"calls":[]}]}}]}
            """);

        var response = await client.CreateChatCompletionAsync(new ChatCompletionRequest { Messages = { DeepSeekMessage.User("hi") } });
        Assert.NotNull(response.Message!.Annotations);
        Assert.NotNull(response.Message.ServerToolCalls);

        handler.EnqueueJson(Completion("ok"));
        await client.CreateChatCompletionAsync(new ChatCompletionRequest
        {
            Messages = { DeepSeekMessage.User("hi"), response.Message, DeepSeekMessage.User("again") }
        });
        var sent = handler.Requests[1].Json["messages"]![1]!;
        Assert.Null(sent["annotations"]);
        Assert.Null(sent["server_tool_calls"]);
    }

    [Fact]
    public async Task NonStream_Http200WithErrorBody_Throws()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("""{"error":{"message":"The model failed to generate a complete response.","type":"api_error"},"usage":{}}""");

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() =>
            client.CreateChatCompletionAsync(new ChatCompletionRequest { Messages = { DeepSeekMessage.User("hi") } }));
        Assert.Equal("api_error", ex.ErrorType);
        Assert.False(ex.IsRetryable);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task NonStream_InvalidJson_ThrowsTypedException()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("<html>oops</html>");
        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() =>
            client.CreateChatCompletionAsync(new ChatCompletionRequest { Messages = { DeepSeekMessage.User("hi") } }));
        Assert.False(ex.IsRetryable);
        Assert.Contains("oops", ex.ResponseBody);
    }

    [Fact]
    public async Task ListModels_ParsesMetadata()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("""
            {"object":"list","data":[
              {"id":"deepseek-flash","object":"model","owned_by":"deepseek","name":"DeepSeek Flash","context_window":1000000,
               "max_output_tokens":393216,"input_modalities":["text","image"],"output_modalities":["text"],
               "effort":{"supported_levels":["low","high","max"],"default_level":"high"},
               "api_capabilities":{"anthropic_messages":{"system_prompt_update":"leading-only"}}},
              {"id":"deepseek-v4-pro","object":"model","owned_by":"deepseek"}]}
            """);

        var models = await client.ListModelsAsync();

        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Equal("https://api.deepseek.com/models", req.Uri.ToString());
        Assert.Equal("Bearer", req.Authorization!.Scheme);
        Assert.Equal(2, models.Count);
        Assert.Equal("deepseek-flash", models[0].Id);
        Assert.Equal(1000000, models[0].ContextWindow);
        Assert.Equal(new[] { "low", "high", "max" }, models[0].Effort!.SupportedLevels);
        Assert.Equal("high", models[0].Effort!.DefaultLevel);
        Assert.NotNull(models[0].ApiCapabilities);
        Assert.Null(models[1].ContextWindow);
    }

    [Fact]
    public async Task GetBalance_ParsesCurrencies()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("""
            {"is_available":true,"balance_infos":[
             {"currency":"CNY","total_balance":"110.00","granted_balance":"10.00","topped_up_balance":"100.00"},
             {"currency":"USD","total_balance":"1.5","granted_balance":"0","topped_up_balance":"1.5"}]}
            """);

        var balance = await client.GetBalanceAsync();

        Assert.Equal("https://api.deepseek.com/user/balance", Assert.Single(handler.Requests).Uri.ToString());
        Assert.True(balance.IsAvailable);
        Assert.Equal(2, balance.BalanceInfos.Count);
        Assert.Equal("CNY", balance.BalanceInfos[0].Currency);
        Assert.Equal(110.00m, balance.BalanceInfos[0].Total);
        Assert.Equal(10.00m, balance.BalanceInfos[0].Granted);
        Assert.Equal(100.00m, balance.BalanceInfos[0].ToppedUp);
        Assert.Equal(1.5m, balance.BalanceInfos[1].Total);
    }

    [Fact]
    public async Task Fim_UsesBetaCompletionsEndpoint()
    {
        var (client, handler, _) = CreateClient(o => o.MaxTokens = 128);
        handler.EnqueueJson("""
            {"id":"c1","object":"text_completion","created":1,"model":"deepseek-flash",
             "choices":[{"index":0,"text":"    if a<2: return a\n","finish_reason":"stop"}],
             "usage":{"prompt_tokens":5,"completion_tokens":6,"total_tokens":11}}
            """);

        var response = await client.CreateFimCompletionAsync(new FimCompletionRequest
        {
            Prompt = "def fib(a):",
            Suffix = "    return fib(a-1) + fib(a-2)"
        });

        var req = Assert.Single(handler.Requests);
        Assert.Equal("https://api.deepseek.com/beta/completions", req.Uri.ToString());
        var json = req.Json;
        Assert.Equal("deepseek-flash", (string?)json["model"]);
        Assert.Equal("def fib(a):", (string?)json["prompt"]);
        Assert.Equal("    return fib(a-1) + fib(a-2)", (string?)json["suffix"]);
        Assert.Equal(128, (int?)json["max_tokens"]);
        Assert.Null(json["echo"]);
        Assert.Contains("return a", response.Text);
        Assert.Equal(11, response.Usage!.TotalTokens);
    }

    [Fact]
    public async Task Fim_EchoWithSuffix_Throws()
    {
        var (client, handler, _) = CreateClient();
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateFimCompletionAsync(
            new FimCompletionRequest { Prompt = "a", Suffix = "b", Echo = true }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Fim_Echo_IsSent()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("""{"choices":[{"index":0,"text":"x"}]}""");
        await client.CreateFimCompletionAsync(new FimCompletionRequest { Prompt = "a", Echo = true, Stop = new() { "\n" } });
        var json = handler.Requests[0].Json;
        Assert.True((bool?)json["echo"]);
        Assert.Equal("\n", (string?)json["stop"]![0]);
    }
}

public class ClientErrorAndRetryTests
{
    private static ChatCompletionRequest Req() => new() { Messages = { DeepSeekMessage.User("hi") } };

    [Fact]
    public async Task Unauthorized_IsNotRetried_AndIsTyped()
    {
        var (client, handler, delays) = CreateClient();
        handler.EnqueueError(HttpStatusCode.Unauthorized, "authentication_error", "Authentication Fails");

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => client.CreateChatCompletionAsync(Req()));

        Assert.Single(handler.Requests);
        Assert.Empty(delays);
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.True(ex.IsAuthenticationError);
        Assert.False(ex.IsRetryable);
        Assert.Equal("authentication_error", ex.ErrorType);
        Assert.Equal("authentication_error_code", ex.ErrorCode);
        Assert.Contains("Authentication Fails", ex.Message);
        Assert.DoesNotContain("sk-test-key", ex.Message);
        Assert.DoesNotContain("sk-test-key", ex.ResponseBody ?? "");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.PaymentRequired, false)]
    [InlineData(HttpStatusCode.UnprocessableEntity, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    public async Task StatusMapping_AndRetryability(HttpStatusCode status, bool retryable)
    {
        var (client, handler, _) = CreateClient(o => o.MaxRetries = 0);
        handler.EnqueueError(status);

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => client.CreateChatCompletionAsync(Req()));

        Assert.Equal(status, ex.StatusCode);
        Assert.Equal(retryable, ex.IsRetryable);
        Assert.Equal(status == HttpStatusCode.PaymentRequired, ex.IsInsufficientBalance);
        Assert.Equal(status == HttpStatusCode.TooManyRequests, ex.IsRateLimited);
    }

    [Fact]
    public async Task NonJsonErrorBody_StillProducesTypedException()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("Bad Request", HttpStatusCode.BadRequest);
        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => client.CreateChatCompletionAsync(Req()));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Null(ex.ErrorType);
        Assert.Equal("Bad Request", ex.ResponseBody);
    }

    [Fact]
    public async Task ServerErrors_AreRetried_WithExponentialBackoffAndJitter()
    {
        var (client, handler, delays) = CreateClient(o => { o.RetryBaseDelayMs = 100; o.MaxRetries = 3; });
        handler.EnqueueError(HttpStatusCode.InternalServerError)
            .EnqueueError(HttpStatusCode.ServiceUnavailable)
            .EnqueueError(HttpStatusCode.InternalServerError)
            .EnqueueJson(Completion("finally"));

        var response = await client.CreateChatCompletionAsync(Req());

        Assert.Equal("finally", response.Content);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(3, delays.Count);
        // 第 n 次退避位于 [0.5, 1.0] * base * 2^n
        for (var i = 0; i < 3; i++)
        {
            var full = 100 * Math.Pow(2, i);
            Assert.InRange(delays[i].TotalMilliseconds, full * 0.5 - 0.001, full + 0.001);
        }
    }

    [Fact]
    public async Task RateLimit_HonorsRetryAfter()
    {
        var (client, handler, delays) = CreateClient();
        handler.EnqueueError(HttpStatusCode.TooManyRequests, "rate_limit_reached",
                configure: r => r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2)))
            .EnqueueJson(Completion("ok"));

        await client.CreateChatCompletionAsync(Req());

        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(delays));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task RetryAfter_IsCappedByMaxDelay()
    {
        var (client, handler, delays) = CreateClient(o => o.RetryMaxDelaySeconds = 3);
        handler.EnqueueError(HttpStatusCode.TooManyRequests,
                configure: r => r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120)))
            .EnqueueJson(Completion("ok"));

        await client.CreateChatCompletionAsync(Req());
        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(delays));
    }

    [Fact]
    public async Task RetriesExhausted_ThrowsLastError()
    {
        var (client, handler, delays) = CreateClient(o => o.MaxRetries = 2);
        for (var i = 0; i < 3; i++)
        {
            handler.EnqueueError(HttpStatusCode.ServiceUnavailable, message: $"busy{i}");
        }

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => client.CreateChatCompletionAsync(Req()));

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, delays.Count);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.True(ex.IsRetryable);
        Assert.Contains("busy2", ex.Message);
    }

    [Fact]
    public async Task NetworkErrors_AreRetried()
    {
        var (client, handler, delays) = CreateClient();
        handler.EnqueueException(new HttpRequestException("connection reset"))
            .EnqueueException(new IOException("broken pipe"))
            .EnqueueJson(Completion("recovered"));

        var response = await client.CreateChatCompletionAsync(Req());

        Assert.Equal("recovered", response.Content);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task NetworkError_AfterRetries_IsWrapped()
    {
        var (client, handler, _) = CreateClient(o => o.MaxRetries = 1);
        handler.EnqueueException(new HttpRequestException("down")).EnqueueException(new HttpRequestException("down"));

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => client.CreateChatCompletionAsync(Req()));
        Assert.Null(ex.StatusCode);
        Assert.True(ex.IsRetryable);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task Timeout_IsRetriedThenReported()
    {
        var (client, handler, _) = CreateClient(o => { o.TimeoutSeconds = 1; o.MaxRetries = 1; });
        handler.Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });
        handler.Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => client.CreateChatCompletionAsync(Req()));
        Assert.Contains("超时", ex.Message);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAsOperationCanceled_WithoutRetry()
    {
        var (client, handler, delays) = CreateClient();
        using var cts = new CancellationTokenSource();
        handler.Enqueue(async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CreateChatCompletionAsync(Req(), cts.Token));
        Assert.Single(handler.Requests);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Balance_And_Models_AlsoRetry()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueError(HttpStatusCode.ServiceUnavailable).EnqueueJson("""{"is_available":false,"balance_infos":[]}""");
        var balance = await client.GetBalanceAsync();
        Assert.False(balance.IsAvailable);
        Assert.Equal(2, handler.Requests.Count);
    }
}

public class ClientStreamingTests
{
    private static ChatCompletionRequest Req() => new() { Messages = { DeepSeekMessage.User("hi") } };

    [Fact]
    public async Task Stream_ParsesDeltas_SkipsKeepAliveAndStopsAtDone()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueSse(
            ": keep-alive\n\n" +
            Chunk(reasoning: "想") + Chunk(reasoning: "一想") +
            ": keep-alive\n\n" +
            Chunk(content: "你") + Chunk(content: "好") +
            Chunk(finish: "stop", usage: new { prompt_tokens = 7, completion_tokens = 2, total_tokens = 9, prompt_cache_hit_tokens = 5 }) +
            Done +
            Chunk(content: "SHOULD NOT BE READ"));

        var chunks = await ToListAsync(client.StreamChatCompletionAsync(Req()));

        var req = Assert.Single(handler.Requests);
        Assert.True((bool?)req.Json["stream"]);
        Assert.True((bool?)req.Json["stream_options"]!["include_usage"]);

        Assert.Equal(5, chunks.Count);
        Assert.Equal("想一想", string.Concat(chunks.Select(c => c.Delta?.ReasoningContent)));
        Assert.Equal("你好", string.Concat(chunks.Select(c => c.Delta?.Content)));
        Assert.Equal("stop", chunks[^1].FinishReason);
        Assert.Equal(5, chunks[^1].Usage!.PromptCacheHitTokens);

        var acc = new ChatCompletionStreamAccumulator();
        chunks.ForEach(acc.Add);
        var message = acc.BuildMessage();
        Assert.Equal("你好", message.Content);
        Assert.Equal("想一想", message.ReasoningContent);
        Assert.Equal(9, acc.Usage!.TotalTokens);
    }

    [Fact]
    public async Task Stream_HandlesCrLfAndNoSpaceAfterColon_AndMissingDoneAfterFinish()
    {
        var (client, handler, _) = CreateClient();
        var body = (Chunk(content: "a").Replace("data: ", "data:") + Chunk(finish: "stop")).Replace("\n", "\r\n");
        handler.EnqueueSse(body);

        var chunks = await ToListAsync(client.StreamChatCompletionAsync(Req()));
        Assert.Equal(2, chunks.Count);
    }

    [Fact]
    public async Task Stream_FragmentedToolCallArguments_AreConcatenatedByIndex()
    {
        var (client, handler, _) = CreateClient();
        var sb = new System.Text.StringBuilder();
        sb.Append(Chunk(reasoning: "先查"));
        sb.Append(Chunk(toolCalls: ToolFragment(0, "call_a", "get_weather", "")));
        sb.Append(Chunk(toolCalls: ToolFragment(0, args: "{\"ci")));
        sb.Append(Chunk(toolCalls: new object[]
        {
            new { index = 0, function = new { arguments = "ty\":\"杭" } },
            new { index = 1, id = "call_b", type = "function", function = new { name = "get_date", arguments = "{}" } }
        }));
        sb.Append(Chunk(toolCalls: ToolFragment(0, args: "州\"}")));
        sb.Append(Chunk(finish: "tool_calls"));
        sb.Append(Done);
        handler.EnqueueSse(sb.ToString());

        var acc = new ChatCompletionStreamAccumulator();
        await foreach (var chunk in client.StreamChatCompletionAsync(Req()))
        {
            acc.Add(chunk);
        }

        var calls = acc.BuildToolCalls();
        Assert.Equal(2, calls.Count);
        Assert.Equal("call_a", calls[0].Id);
        Assert.Equal("get_weather", calls[0].Function!.Name);
        Assert.Equal("{\"city\":\"杭州\"}", calls[0].Function!.Arguments);
        Assert.Equal("call_b", calls[1].Id);
        Assert.Equal("get_date", calls[1].Function!.Name);
        Assert.Equal("{}", calls[1].Function!.Arguments);
        Assert.Equal("tool_calls", acc.FinishReason);

        var message = acc.BuildMessage();
        Assert.Equal("", message.Content);
        Assert.Equal("先查", message.ReasoningContent);
        Assert.Equal(2, message.ToolCalls!.Count);
    }

    [Fact]
    public async Task Stream_EmptyToolArguments_AreNormalizedToEmptyObject()
    {
        var acc = new ChatCompletionStreamAccumulator();
        acc.Add(new ChatCompletionChunk
        {
            Choices =
            {
                new ChatCompletionChunkChoice
                {
                    Delta = new DeepSeekMessageDelta
                    {
                        ToolCalls = new() { new DeepSeekToolCall { Index = 0, Id = "c", Function = new DeepSeekFunctionCall { Name = "get_date", Arguments = "" } } }
                    }
                }
            }
        });
        Assert.Equal("{}", Assert.Single(acc.BuildToolCalls()).Function!.Arguments);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Stream_ErrorBeforeHeaders_IsRetried()
    {
        var (client, handler, delays) = CreateClient();
        handler.EnqueueError(HttpStatusCode.TooManyRequests).EnqueueSse(TextSse("ok"));

        var chunks = await ToListAsync(client.StreamChatCompletionAsync(Req()));

        Assert.NotEmpty(chunks);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Single(delays);
    }

    [Fact]
    public async Task Stream_401_IsNotRetried()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueError(HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => ToListAsync(client.StreamChatCompletionAsync(Req())));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Stream_FailureAfterBytesConsumed_IsNotRetried()
    {
        var (client, handler, delays) = CreateClient();
        handler.EnqueueSse(Chunk(content: "部分") +
                           "data: {\"error\":{\"message\":\"The model failed\",\"type\":\"api_error\"},\"choices\":[],\"usage\":{}}\n\n");

        var received = new List<ChatCompletionChunk>();
        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(async () =>
        {
            await foreach (var chunk in client.StreamChatCompletionAsync(Req()))
            {
                received.Add(chunk);
            }
        });

        Assert.Single(received);
        Assert.Equal("api_error", ex.ErrorType);
        Assert.Single(handler.Requests);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Stream_EndsWithoutDoneOrFinish_Throws()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueSse(Chunk(content: "半截"));

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => ToListAsync(client.StreamChatCompletionAsync(Req())));
        Assert.Contains("中断", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Stream_MalformedChunk_Throws()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueSse("data: {not json}\n\n");
        await Assert.ThrowsAsync<DeepSeekApiException>(() => ToListAsync(client.StreamChatCompletionAsync(Req())));
    }

    [Fact]
    public async Task Stream_ArgumentValidation_HappensEagerly()
    {
        var (client, _, _) = CreateClient();
        Assert.Throws<ArgumentException>(() => client.StreamChatCompletionAsync(new ChatCompletionRequest()));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Stream_Cancellation_StopsEnumerationAndDisposesResponse()
    {
        var (client, handler, _) = CreateClient();
        var disposed = new TaskCompletionSource();
        handler.Enqueue((_, _) =>
        {
            var content = new TrackingStreamContent(new StallingStream(Chunk(content: "first")), disposed);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        using var cts = new CancellationTokenSource();
        var received = new List<string?>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var chunk in client.StreamChatCompletionAsync(Req(), cts.Token))
            {
                received.Add(chunk.Delta?.Content);
                await cts.CancelAsync();
            }
        });

        Assert.Equal(new[] { "first" }, received);
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Stream_EarlyBreak_DisposesResponse()
    {
        var (client, handler, _) = CreateClient();
        var disposed = new TaskCompletionSource();
        handler.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new TrackingStreamContent(new StallingStream(Chunk(content: "a") + Chunk(content: "b")), disposed)
        }));

        await foreach (var _ in client.StreamChatCompletionAsync(Req()))
        {
            break;
        }

        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Stream_IdleTimeout_IsReported()
    {
        var (client, handler, _) = CreateClient(o => o.TimeoutSeconds = 1);
        handler.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream(Chunk(content: "a")))
        }));

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => ToListAsync(client.StreamChatCompletionAsync(Req())));
        Assert.Contains("空闲超时", ex.Message);
        Assert.Single(handler.Requests);
    }

    private sealed class TrackingStreamContent : StreamContent
    {
        private readonly TaskCompletionSource _disposed;

        public TrackingStreamContent(Stream stream, TaskCompletionSource disposed) : base(stream) => _disposed = disposed;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _disposed.TrySetResult();
        }
    }
}
