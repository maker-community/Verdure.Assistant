namespace Verdure.Assistant.Core.Services.DeepSeek;

/// <summary>
/// DeepSeek API 客户端
/// </summary>
public interface IDeepSeekClient
{
    /// <summary>当前使用的配置</summary>
    DeepSeekOptions Options { get; }

    /// <summary>非流式 POST /chat/completions</summary>
    Task<ChatCompletionResponse> CreateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>流式（SSE）POST /chat/completions，枚举结束或释放时关闭连接</summary>
    IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>GET /models</summary>
    Task<IReadOnlyList<DeepSeekModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>GET /user/balance</summary>
    Task<DeepSeekBalance> GetBalanceAsync(CancellationToken cancellationToken = default);

    /// <summary>POST /beta/completions（FIM 补全）</summary>
    Task<FimCompletionResponse> CreateFimCompletionAsync(FimCompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 对话前缀续写（Beta）：以 <paramref name="prefix"/> 作为最后一条 assistant 消息让模型继续生成。
    /// 默认关闭思考模式。
    /// </summary>
    Task<ChatCompletionResponse> CreatePrefixCompletionAsync(
        IEnumerable<DeepSeekMessage> messages,
        string prefix,
        IEnumerable<string>? stop = null,
        bool enableThinking = false,
        int? maxTokens = null,
        CancellationToken cancellationToken = default);
}
