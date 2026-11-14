using System.Text;

namespace Verdure.Assistant.Core.Services.DeepSeek;

/// <summary>
/// DeepSeek 模型名称常量与辅助方法
/// </summary>
public static class DeepSeekModelNames
{
    /// <summary>默认模型 deepseek-flash</summary>
    public const string Flash = "deepseek-flash";

    /// <summary>高性能模型 deepseek-v4-pro</summary>
    public const string Pro = "deepseek-v4-pro";

    /// <summary>旧名称（仍可用）：deepseek-v4-flash</summary>
    public const string LegacyV4Flash = "deepseek-v4-flash";

    /// <summary>旧名称（仍可用）：deepseek-chat</summary>
    public const string LegacyChat = "deepseek-chat";

    /// <summary>旧名称（仍可用）：deepseek-reasoner</summary>
    public const string LegacyReasoner = "deepseek-reasoner";

    private static readonly string[] LegacyFlashNames = { LegacyV4Flash, LegacyChat, LegacyReasoner };

    /// <summary>是否为当前推荐的模型名称</summary>
    public static bool IsCurrent(string? model) =>
        string.Equals(model, Flash, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(model, Pro, StringComparison.OrdinalIgnoreCase);

    /// <summary>是否为仍被接受的旧模型名称</summary>
    public static bool IsLegacy(string? model) =>
        model != null && LegacyFlashNames.Contains(model, StringComparer.OrdinalIgnoreCase);

    /// <summary>是否为 deepseek-v4-pro（其 top_p 行为与 flash 不同）</summary>
    public static bool IsPro(string? model) =>
        string.Equals(model, Pro, StringComparison.OrdinalIgnoreCase);

    /// <summary>把旧名称映射到当前名称；未知名称原样返回</summary>
    public static string Normalize(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return Flash;
        }

        return IsLegacy(model) ? Flash : model.Trim();
    }
}

/// <summary>
/// reasoning_effort 取值与归一化
/// </summary>
public static class DeepSeekReasoningEffort
{
    public const string None = "none";
    public const string Low = "low";
    public const string High = "high";
    public const string Max = "max";

    /// <summary>
    /// 归一化为 none/low/high/max；兼容 minimal→low、medium/xhigh→high、ultra→max。无法识别时返回 null。
    /// </summary>
    public static string? Normalize(string? effort)
    {
        if (string.IsNullOrWhiteSpace(effort))
        {
            return null;
        }

        return effort.Trim().ToLowerInvariant() switch
        {
            "none" => None,
            "minimal" or "low" => Low,
            "medium" or "high" or "xhigh" => High,
            "max" or "ultra" => Max,
            _ => null
        };
    }
}

/// <summary>
/// DeepSeek 客户端与会话配置
/// </summary>
public class DeepSeekOptions
{
    public const string DefaultBaseUrl = "https://api.deepseek.com";

    public const string DefaultSystemPrompt =
        "你是“绿荫助手”，一个运行在语音设备上的智能语音助手。" +
        "请用简洁、自然、口语化的中文回答，一般不超过三句话；回答会被直接朗读，所以不要使用 Markdown、列表、表情符号或代码块。" +
        "需要控制设备或获取实时信息时，优先调用可用的工具；不确定时请坦诚说明，不要编造。";

    /// <summary>API Key（不会被记录到日志）</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>API 根地址，不含 /beta</summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>默认模型</summary>
    public string Model { get; set; } = DeepSeekModelNames.Flash;

    /// <summary>是否默认开启思考模式</summary>
    public bool ThinkingEnabled { get; set; } = true;

    /// <summary>思考强度：none/low/high/max（none 等价于关闭思考）</summary>
    public string ReasoningEffort { get; set; } = DeepSeekReasoningEffort.High;

    /// <summary>默认 max_tokens；null 表示使用服务端默认值</summary>
    public int? MaxTokens { get; set; }

    /// <summary>默认 temperature（思考模式下不会发送）</summary>
    public double? Temperature { get; set; }

    /// <summary>默认 top_p</summary>
    public double? TopP { get; set; }

    /// <summary>单次请求（流式为等待响应头/单次读取空闲）超时秒数</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>429/500/503/网络错误的最大重试次数</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>重试退避基准时长（毫秒），按 2^n 指数增长并加入抖动</summary>
    public int RetryBaseDelayMs { get; set; } = 500;

    /// <summary>单次重试等待的上限（秒），同样用于限制 Retry-After</summary>
    public int RetryMaxDelaySeconds { get; set; } = 30;

    /// <summary>会话的 system prompt</summary>
    public string SystemPrompt { get; set; } = DefaultSystemPrompt;

    /// <summary>是否自动声明服务端联网搜索工具 deepseek_web_search</summary>
    public bool EnableWebSearch { get; set; }

    /// <summary>是否自动声明服务端阅读链接工具 deepseek_read_url</summary>
    public bool EnableReadUrl { get; set; }

    /// <summary>会话保留的最大历史消息数（不含 system）；&lt;=0 表示不裁剪</summary>
    public int MaxHistoryMessages { get; set; } = 40;

    /// <summary>可选的 user_id，用于内容安全、缓存隔离与调度隔离</summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 校验配置，返回问题列表；空列表表示配置有效。
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            problems.Add("ApiKey 不能为空");
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            problems.Add("BaseUrl 必须是有效的 http(s) 绝对地址");
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            problems.Add("Model 不能为空");
        }

        if (DeepSeekReasoningEffort.Normalize(ReasoningEffort) == null)
        {
            problems.Add("ReasoningEffort 必须是 none/low/high/max");
        }

        if (MaxTokens is < 1 or > 393216)
        {
            problems.Add("MaxTokens 必须在 1 到 393216 之间");
        }

        if (Temperature is < 0 or > 2)
        {
            problems.Add("Temperature 必须在 0 到 2 之间");
        }

        if (TopP is not null && (TopP <= 0 || TopP > 1))
        {
            problems.Add("TopP 必须大于 0 且不超过 1");
        }

        if (TimeoutSeconds <= 0)
        {
            problems.Add("TimeoutSeconds 必须大于 0");
        }

        if (MaxRetries < 0 || MaxRetries > 10)
        {
            problems.Add("MaxRetries 必须在 0 到 10 之间");
        }

        if (RetryBaseDelayMs < 0)
        {
            problems.Add("RetryBaseDelayMs 不能为负数");
        }

        if (RetryMaxDelaySeconds < 0)
        {
            problems.Add("RetryMaxDelaySeconds 不能为负数");
        }

        if (UserId != null && Encoding.UTF8.GetByteCount(UserId) > 512)
        {
            problems.Add("UserId 编码后不得超过 512 字节");
        }

        return problems;
    }
}
