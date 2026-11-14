using Verdure.Assistant.Core.Services.DeepSeek;
using Xunit;

namespace DeepSeek.Tests;

/// <summary>
/// 访问真实 DeepSeek 服务的冒烟测试，默认不运行（避免单元测试依赖网络）。
/// 运行方式：设置环境变量 DEEPSEEK_LIVE_TESTS=1（验证错误映射），
/// 再设置 DEEPSEEK_API_KEY 可额外验证真实对话/思考/余额。
/// </summary>
public class LiveSmokeTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("DEEPSEEK_LIVE_TESTS") == "1";

    private static DeepSeekClient NewClient(string apiKey) => new(
        new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
        new DeepSeekOptions { ApiKey = apiKey, MaxRetries = 1, TimeoutSeconds = 60 });

    [Fact]
    public async Task InvalidKey_IsMappedToAuthenticationError()
    {
        if (!Enabled) return;

        var client = NewClient("sk-invalid-key-for-test");
        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => client.ListModelsAsync());

        Assert.True(ex.IsAuthenticationError);
        Assert.Equal("authentication_error", ex.ErrorType);
        Assert.DoesNotContain("sk-invalid-key-for-test", ex.Message);
    }

    [Fact]
    public async Task RealKey_StreamsThinkingAndAnswer_AndReportsBalance()
    {
        var key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        if (!Enabled || string.IsNullOrWhiteSpace(key)) return;

        var client = NewClient(key);
        var session = new DeepSeekChatSession(client, client.Options);

        var events = await TestData.ToListAsync(session.SendAsync("用一句话回答：1+1 等于几？"));

        Assert.IsType<DeepSeekChatEvent.Completed>(events[^1]);
        Assert.Contains(events, e => e is DeepSeekChatEvent.ContentDelta);
        Assert.Contains(events, e => e is DeepSeekChatEvent.ReasoningDelta);

        var balance = await client.GetBalanceAsync();
        Assert.NotNull(balance.BalanceInfos);
    }
}
