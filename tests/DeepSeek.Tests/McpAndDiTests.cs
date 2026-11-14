using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Verdure.Assistant.Core.Services.DeepSeek;
using Verdure.Assistant.Core.Services.MCP;
using Xunit;

namespace DeepSeek.Tests;

public class McpToolExecutorTests
{
    private sealed class Probe
    {
        public string? Song;
        public int? Volume;
        public bool? Loop;
    }

    private static (McpServer Server, McpDeepSeekToolExecutor Executor, Probe Probe) Create(
        Func<McpTool, bool>? filter = null)
    {
        var server = new McpServer();
        var probe = new Probe();
        server.AddTool("self.music.play", "播放音乐",
            new McpPropertyList
            {
                new McpProperty("song", McpPropertyType.String, "歌曲名"),
                new McpProperty("volume", 50, 0, 100, "音量"),
                new McpProperty("loop", McpPropertyType.Boolean, false, "循环")
            },
            props =>
            {
                probe.Song = props["song"].GetValue<string>();
                probe.Volume = props["volume"].GetValue<int>();
                probe.Loop = props["loop"].GetValue<bool>();
                return Task.FromResult<McpReturnValue>($"playing {probe.Song}");
            });
        return (server, new McpDeepSeekToolExecutor(server, filter: filter), probe);
    }

    [Fact]
    public void GetTools_SanitizesNames_AndBuildsSchema()
    {
        var (_, executor, _) = Create();

        var tool = Assert.Single(executor.GetTools());

        Assert.Equal("self_music_play", tool.Function!.Name);
        Assert.True(DeepSeekToolDefinition.IsValidFunctionName(tool.Function.Name));
        Assert.Equal("播放音乐", tool.Function.Description);
        var schema = tool.Function.Parameters!.Value;
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("song").GetProperty("type").GetString());
        Assert.Equal("integer", schema.GetProperty("properties").GetProperty("volume").GetProperty("type").GetString());
        Assert.Equal(100, schema.GetProperty("properties").GetProperty("volume").GetProperty("maximum").GetInt32());
        Assert.Equal(new[] { "song" }, schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void NameMapping_IsReversible()
    {
        var (_, executor, _) = Create();
        executor.GetTools();

        Assert.Equal("self_music_play", executor.ToDeepSeekName("self.music.play"));
        Assert.True(executor.TryGetMcpName("self_music_play", out var original));
        Assert.Equal("self.music.play", original);
        Assert.False(executor.TryGetMcpName("nope", out _));
    }

    [Fact]
    public async Task Execute_ConvertsJsonArguments_AndReturnsText()
    {
        var (_, executor, probe) = Create();

        var result = await executor.ExecuteAsync("self_music_play",
            """{"song":"晴天","volume":80,"loop":true}""", CancellationToken.None);

        Assert.Equal("playing 晴天", result);
        Assert.Equal("晴天", probe.Song);
        Assert.Equal(80, probe.Volume);
        Assert.True(probe.Loop);
    }

    [Fact]
    public async Task Execute_AppliesDefaults_AndCoercesLooseTypes()
    {
        var (_, executor, probe) = Create();

        await executor.ExecuteAsync("self_music_play", """{"song":"A"}""", CancellationToken.None);
        Assert.Equal(50, probe.Volume);
        Assert.False(probe.Loop);

        await executor.ExecuteAsync("self_music_play", """{"song":123,"volume":"70","loop":"true"}""", CancellationToken.None);
        Assert.Equal("123", probe.Song);
        Assert.Equal(70, probe.Volume);
        Assert.True(probe.Loop);

        await executor.ExecuteAsync("self_music_play", """{"song":"B","volume":60.0,"extra":null}""", CancellationToken.None);
        Assert.Equal(60, probe.Volume);
    }

    [Theory]
    [InlineData("{}", "Missing required parameter")]
    [InlineData("""{"song":"x","volume":150}""", "exceeds maximum")]
    [InlineData("{not json", "Invalid arguments JSON")]
    [InlineData("[1,2]", "must be a JSON object")]
    public async Task Execute_Failures_ThrowToolException(string args, string expected)
    {
        var (_, executor, _) = Create();
        var ex = await Assert.ThrowsAsync<DeepSeekToolException>(() =>
            executor.ExecuteAsync("self_music_play", args, CancellationToken.None));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Execute_EmptyArguments_AreTreatedAsEmptyObject()
    {
        var server = new McpServer();
        server.AddTool("self.ping", "ping", new McpPropertyList(), _ => Task.FromResult<McpReturnValue>("pong"));
        var executor = new McpDeepSeekToolExecutor(server);

        Assert.Equal("pong", await executor.ExecuteAsync("self_ping", "", CancellationToken.None));
        Assert.Equal("pong", await executor.ExecuteAsync("self_ping", "null", CancellationToken.None));
    }

    [Fact]
    public async Task Execute_HandlerException_BecomesToolException()
    {
        var server = new McpServer();
        server.AddTool("self.boom", "boom", new McpPropertyList(),
            _ => throw new InvalidOperationException("kaboom"));
        var executor = new McpDeepSeekToolExecutor(server);

        var ex = await Assert.ThrowsAsync<DeepSeekToolException>(() =>
            executor.ExecuteAsync("self_boom", "{}", CancellationToken.None));
        Assert.Contains("kaboom", ex.Message);
    }

    [Fact]
    public async Task Execute_UnknownTool_Throws()
    {
        var (_, executor, _) = Create();
        await Assert.ThrowsAsync<DeepSeekToolException>(() =>
            executor.ExecuteAsync("missing_tool", "{}", CancellationToken.None));
    }

    [Fact]
    public async Task Execute_Cancellation_IsObserved()
    {
        var server = new McpServer();
        server.AddTool("self.slow", "slow", new McpPropertyList(), async _ =>
        {
            await Task.Delay(Timeout.Infinite);
            return "never";
        });
        var executor = new McpDeepSeekToolExecutor(server);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync("self_slow", "{}", cts.Token));
    }

    [Fact]
    public void NameCollisions_ResolveDeterministically_AndAreReversible()
    {
        var server = new McpServer();
        foreach (var name in new[] { "a.b", "a_b", "a b", "设备.灯" })
        {
            server.AddTool(name, name, new McpPropertyList(), _ => Task.FromResult<McpReturnValue>(name));
        }

        var executor = new McpDeepSeekToolExecutor(server);
        var names = executor.GetTools().Select(t => t.Function!.Name).ToList();

        Assert.Equal(4, names.Distinct().Count());
        Assert.All(names, n => Assert.True(DeepSeekToolDefinition.IsValidFunctionName(n), n));
        Assert.Equal("a_b", executor.ToDeepSeekName("a_b")); // 本身合法的名称保持不变
        foreach (var original in new[] { "a.b", "a_b", "a b", "设备.灯" })
        {
            Assert.True(executor.TryGetMcpName(executor.ToDeepSeekName(original), out var back));
            Assert.Equal(original, back);
        }

        // 同一份工具集合重建后映射保持一致
        var again = new McpDeepSeekToolExecutor(server).GetTools().Select(t => t.Function!.Name).ToList();
        Assert.Equal(names, again);
    }

    [Fact]
    public void LongNames_AreTruncatedTo128()
    {
        var server = new McpServer();
        var longName = "self." + new string('x', 200);
        server.AddTool(longName, "d", new McpPropertyList(), _ => Task.FromResult<McpReturnValue>("ok"));
        var executor = new McpDeepSeekToolExecutor(server);

        var name = Assert.Single(executor.GetTools()).Function!.Name;
        Assert.Equal(128, name.Length);
        Assert.True(executor.TryGetMcpName(name, out var original));
        Assert.Equal(longName, original);
    }

    [Fact]
    public async Task ToolsAddedLater_AreExecutableWithoutRefresh()
    {
        var (server, executor, _) = Create();
        executor.GetTools();

        server.AddTool("self.late.tool", "late", new McpPropertyList(), _ => Task.FromResult<McpReturnValue>("late-ok"));

        Assert.Equal("late-ok", await executor.ExecuteAsync("self_late_tool", "{}", CancellationToken.None));
        Assert.Equal(2, executor.GetTools().Count);
    }

    [Fact]
    public void Filter_HidesTools()
    {
        var (_, executor, _) = Create(filter: t => !t.Name.StartsWith("self.music"));
        Assert.Empty(executor.GetTools());
    }

    [Fact]
    public void ToolWithoutInputSchema_FallsBackToEmptyObjectSchema()
    {
        var server = new McpServer();
        server.AddTool(new McpTool
        {
            Name = "bare",
            Description = "",
            Handler = _ => Task.FromResult<McpReturnValue>("x")
        });

        var tool = Assert.Single(new McpDeepSeekToolExecutor(server).GetTools());
        Assert.Null(tool.Function!.Description);
        var schema = tool.Function.Parameters!.Value;
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Object, schema.GetProperty("properties").ValueKind);
    }

    [Theory]
    [InlineData("{\"v\":42}", McpPropertyType.Integer, 42)]
    [InlineData("{\"v\":3000000000}", McpPropertyType.Integer, 3000000000L)]
    [InlineData("{\"v\":2.5}", McpPropertyType.Integer, 2.5)]
    [InlineData("{\"v\":true}", McpPropertyType.Boolean, true)]
    [InlineData("{\"v\":\"hi\"}", McpPropertyType.String, "hi")]
    [InlineData("{\"v\":7}", McpPropertyType.String, "7")]
    public void ConvertValue_ProducesClrValues(string json, McpPropertyType type, object expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, McpDeepSeekToolExecutor.ConvertValue(doc.RootElement.GetProperty("v"), type));
    }

    [Fact]
    public void ConvertValue_NestedStructures_AreKeptAsRawJson()
    {
        using var doc = JsonDocument.Parse("""{"v":{"a":[1,2]}}""");
        Assert.Equal("""{"a":[1,2]}""", McpDeepSeekToolExecutor.ConvertValue(doc.RootElement.GetProperty("v"), null));
    }
}

public class DependencyInjectionTests
{
    [Fact]
    public void AddDeepSeek_RegistersSingletonClient_AndTransientSessions()
    {
        var services = new ServiceCollection();
        services.AddDeepSeek(o =>
        {
            o.ApiKey = "sk-di";
            o.Model = DeepSeekModelNames.Pro;
        });

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<DeepSeekOptions>();
        Assert.Equal("sk-di", options.ApiKey);
        Assert.Same(provider.GetRequiredService<IDeepSeekClient>(), provider.GetRequiredService<IDeepSeekClient>());
        Assert.Same(options, provider.GetRequiredService<IDeepSeekClient>().Options);

        var s1 = provider.GetRequiredService<DeepSeekChatSession>();
        var s2 = provider.GetRequiredService<DeepSeekChatSession>();
        Assert.NotSame(s1, s2);
        Assert.Equal(options.SystemPrompt, s1.SystemPrompt);
    }

    [Fact]
    public void AddDeepSeekMcpTools_ExposesMcpServerToSessions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<McpServer>();
        services.AddDeepSeek(o => o.ApiKey = "k").AddDeepSeekMcpTools();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<McpDeepSeekToolExecutor>(provider.GetRequiredService<IDeepSeekToolExecutor>());
        Assert.NotNull(provider.GetRequiredService<DeepSeekChatSession>());
    }

    [Fact]
    public void AddDeepSeek_WithoutConfigure_UsesDefaults()
    {
        using var provider = new ServiceCollection().AddDeepSeek().BuildServiceProvider();
        var options = provider.GetRequiredService<DeepSeekOptions>();
        Assert.Equal("https://api.deepseek.com", options.BaseUrl);
        Assert.NotEmpty(options.Validate()); // 缺少 ApiKey
    }
}
