namespace Verdure.Assistant.Core.Services.DeepSeek;

/// <summary>
/// <see cref="DeepSeekChatSession.SendAsync"/> 产生的事件
/// </summary>
public abstract record DeepSeekChatEvent
{
    /// <summary>思考内容增量</summary>
    public sealed record ReasoningDelta(string Text) : DeepSeekChatEvent;

    /// <summary>回答内容增量</summary>
    public sealed record ContentDelta(string Text) : DeepSeekChatEvent;

    /// <summary>模型请求调用本地工具（开始执行前）</summary>
    public sealed record ToolCallRequested(string CallId, string Name, string ArgumentsJson) : DeepSeekChatEvent;

    /// <summary>本地工具执行结束；<paramref name="IsError"/> 为 true 时 Result 为错误 JSON</summary>
    public sealed record ToolCallCompleted(string CallId, string Name, string Result, bool IsError) : DeepSeekChatEvent;

    /// <summary>一次模型请求的 token 用量</summary>
    public sealed record UsageReported(DeepSeekUsage Usage) : DeepSeekChatEvent;

    /// <summary>本轮对话完成；Usage 为本轮所有请求的累计用量</summary>
    public sealed record Completed(string Content, string? ReasoningContent, DeepSeekUsage? Usage, string? FinishReason)
        : DeepSeekChatEvent;

    /// <summary>
    /// 本轮失败（API 错误、工具循环超限或取消）。历史已回滚到本轮开始之前。
    /// </summary>
    public sealed record Failed(Exception Exception) : DeepSeekChatEvent
    {
        /// <summary>是否由取消（调用方 token 或 CancelCurrent）引起</summary>
        public bool IsCancelled => Exception is OperationCanceledException;
    }
}
