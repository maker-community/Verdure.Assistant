using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Services.DeepSeek;

/// <summary>
/// 应用设置 → DeepSeek 选项 的映射，以及可开关的工具执行器
/// </summary>
public static class DeepSeekSettingsExtensions
{
    /// <summary>
    /// 把应用设置应用到（共享的）选项实例上。客户端与会话每次请求都会读取该实例，因此对运行中的会话立即生效。
    /// </summary>
    public static void ApplySettings(this DeepSeekOptions options, AppSettings settings, string? apiKey)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);

        options.ApiKey = apiKey?.Trim() ?? string.Empty;
        options.BaseUrl = string.IsNullOrWhiteSpace(settings.DeepSeekBaseUrl)
            ? DeepSeekOptions.DefaultBaseUrl
            : settings.DeepSeekBaseUrl.Trim();
        options.Model = string.IsNullOrWhiteSpace(settings.DeepSeekModel) ? DeepSeekModelNames.Flash : settings.DeepSeekModel.Trim();
        options.ThinkingEnabled = settings.DeepSeekThinkingEnabled;
        options.ReasoningEffort = string.IsNullOrWhiteSpace(settings.DeepSeekReasoningEffort)
            ? DeepSeekReasoningEffort.High
            : settings.DeepSeekReasoningEffort.Trim();
        options.SystemPrompt = string.IsNullOrWhiteSpace(settings.DeepSeekSystemPrompt)
            ? DeepSeekOptions.DefaultSystemPrompt
            : settings.DeepSeekSystemPrompt;
        options.EnableWebSearch = settings.DeepSeekEnableWebSearch;
    }
}

/// <summary>
/// 包装另一个工具执行器，允许在运行时整体启用/禁用工具（禁用后不再向模型声明任何工具）
/// </summary>
public sealed class SwitchableToolExecutor : IDeepSeekToolExecutor
{
    private static readonly IReadOnlyList<DeepSeekToolDefinition> NoTools = Array.Empty<DeepSeekToolDefinition>();

    private readonly IDeepSeekToolExecutor? _inner;

    public SwitchableToolExecutor(IDeepSeekToolExecutor? inner)
    {
        _inner = inner;
    }

    /// <summary>
    /// 是否向模型暴露工具
    /// </summary>
    public volatile bool Enabled = true;

    public IReadOnlyList<DeepSeekToolDefinition> GetTools()
        => Enabled && _inner != null ? _inner.GetTools() : NoTools;

    public Task<string> ExecuteAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        if (!Enabled || _inner == null)
        {
            throw new DeepSeekToolException("工具已被禁用");
        }

        return _inner.ExecuteAsync(name, argumentsJson, cancellationToken);
    }
}
