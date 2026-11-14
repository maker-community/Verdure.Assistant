namespace Verdure.Assistant.Core.Services.DeepSeek;

/// <summary>
/// 本地工具执行器：向模型声明可用工具，并在模型发起调用时执行
/// </summary>
public interface IDeepSeekToolExecutor
{
    /// <summary>当前可用的工具定义（名称须匹配 [a-zA-Z0-9_-]{1,128}）</summary>
    IReadOnlyList<DeepSeekToolDefinition> GetTools();

    /// <summary>
    /// 执行工具并返回结果文本（通常为 JSON）。抛出异常表示失败，
    /// <see cref="DeepSeekChatSession"/> 会把异常转换为错误 JSON 回传给模型。
    /// </summary>
    /// <param name="name">DeepSeek 侧的工具名称</param>
    /// <param name="argumentsJson">模型生成的 JSON 参数</param>
    Task<string> ExecuteAsync(string name, string argumentsJson, CancellationToken cancellationToken);
}

/// <summary>工具执行失败（例如 MCP 工具返回 isError）</summary>
public sealed class DeepSeekToolException : Exception
{
    public DeepSeekToolException(string message) : base(message)
    {
    }

    public DeepSeekToolException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
