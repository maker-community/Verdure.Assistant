using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Verdure.Assistant.Core.Services.DeepSeek;

/// <summary>
/// DeepSeek 使用的 JSON 序列化选项（反射方式，无源生成）
/// </summary>
public static class DeepSeekJson
{
    /// <summary>请求/响应序列化选项</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>用于反序列化模型返回的 JSON 结果（忽略大小写）</summary>
    internal static JsonSerializerOptions ResultOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}

/// <summary>消息角色常量</summary>
public static class DeepSeekRoles
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";
}

/// <summary>
/// 对话消息（请求与响应共用）
/// </summary>
public sealed class DeepSeekMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = DeepSeekRoles.User;

    /// <summary>
    /// 文本内容（响应消息或纯文本请求）。带图片的用户消息此处保存其文字部分，实际发送的是 <see cref="Parts"/>。
    /// </summary>
    [JsonIgnore]
    public string? Content { get; set; }

    /// <summary>多模态内容块（文本 + 图片）；非 null 时请求中的 content 以块数组发送</summary>
    [JsonIgnore]
    public List<DeepSeekContentPart>? Parts { get; set; }

    /// <summary>是否携带图片</summary>
    [JsonIgnore]
    public bool HasImages => Parts?.Any(p => p.Type == DeepSeekContentPart.ImageUrlType) == true;

    /// <summary>
    /// content 的线路格式：字符串或内容块数组。读取响应时字符串/数组都能解析。
    /// </summary>
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? WireContent
    {
        get => (object?)Parts ?? Content;
        set
        {
            switch (value)
            {
                case null:
                    Content = null;
                    Parts = null;
                    break;
                case string text:
                    Content = text;
                    Parts = null;
                    break;
                case JsonElement { ValueKind: JsonValueKind.String } element:
                    Content = element.GetString();
                    Parts = null;
                    break;
                case JsonElement { ValueKind: JsonValueKind.Array } array:
                    var parts = new List<DeepSeekContentPart>();
                    foreach (var item in array.EnumerateArray())
                    {
                        var part = item.Deserialize<DeepSeekContentPart>(DeepSeekJson.Options);
                        if (part != null)
                        {
                            parts.Add(part);
                        }
                    }

                    Parts = parts;
                    Content = string.Concat(parts.Where(p => p.Type == DeepSeekContentPart.TextType).Select(p => p.Text));
                    break;
                default:
                    Content = value.ToString();
                    Parts = null;
                    break;
            }
        }
    }

    /// <summary>思考内容；携带 tools 的请求必须原样回传，未携带 tools 时客户端会自动剔除</summary>
    [JsonPropertyName("reasoning_content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningContent { get; set; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DeepSeekToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>对话前缀续写（Beta）：仅最后一条 assistant 消息可设为 true</summary>
    [JsonPropertyName("prefix")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Prefix { get; set; }

    /// <summary>响应扩展字段：服务端联网工具调用记录（不会随请求发送）</summary>
    [JsonPropertyName("server_tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? ServerToolCalls { get; set; }

    /// <summary>响应扩展字段：联网搜索引用（不会随请求发送）</summary>
    [JsonPropertyName("annotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Annotations { get; set; }

    public static DeepSeekMessage System(string content) =>
        new() { Role = DeepSeekRoles.System, Content = content };

    public static DeepSeekMessage User(string content) =>
        new() { Role = DeepSeekRoles.User, Content = content };

    /// <summary>带图片的用户消息：先放文字，再依次放图片（图片以 data URL 内联）</summary>
    public static DeepSeekMessage User(string text, IEnumerable<DeepSeekImage> images, string? detail = null)
    {
        var parts = new List<DeepSeekContentPart>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            parts.Add(DeepSeekContentPart.FromText(text));
        }

        parts.AddRange(images.Select(i => DeepSeekContentPart.FromImage(i, detail)));
        return parts.Any(p => p.Type == DeepSeekContentPart.ImageUrlType)
            ? new DeepSeekMessage { Role = DeepSeekRoles.User, Content = text, Parts = parts }
            : User(text);
    }

    public static DeepSeekMessage Assistant(string? content, string? reasoningContent = null,
        IEnumerable<DeepSeekToolCall>? toolCalls = null)
    {
        var calls = toolCalls?.ToList();
        return new DeepSeekMessage
        {
            Role = DeepSeekRoles.Assistant,
            Content = content,
            ReasoningContent = reasoningContent,
            ToolCalls = calls is { Count: > 0 } ? calls : null
        };
    }

    /// <summary>构造前缀续写用的 assistant 消息（需配合 beta 接口，客户端会自动切换）</summary>
    public static DeepSeekMessage AssistantPrefix(string prefix) =>
        new() { Role = DeepSeekRoles.Assistant, Content = prefix, Prefix = true };

    public static DeepSeekMessage Tool(string toolCallId, string content) =>
        new() { Role = DeepSeekRoles.Tool, ToolCallId = toolCallId, Content = content };

    /// <summary>生成可安全发送的请求消息副本（不含响应扩展字段与流式 index）</summary>
    internal DeepSeekMessage ToRequestMessage(bool includeReasoning)
    {
        var isAssistant = Role == DeepSeekRoles.Assistant;
        return new DeepSeekMessage
        {
            Role = Role,
            Content = isAssistant ? Content ?? string.Empty : Content,
            Parts = isAssistant ? null : Parts,
            ReasoningContent = isAssistant && includeReasoning ? ReasoningContent : null,
            ToolCalls = ToolCalls is { Count: > 0 }
                ? ToolCalls.Select(c => new DeepSeekToolCall
                {
                    Id = c.Id,
                    Type = c.Type,
                    Function = new DeepSeekFunctionCall { Name = c.Function?.Name, Arguments = c.Function?.Arguments }
                }).ToList()
                : null,
            ToolCallId = ToolCallId,
            Name = Name,
            Prefix = Prefix
        };
    }
}

/// <summary>函数调用（名称与 JSON 字符串形式的参数）</summary>
public sealed class DeepSeekFunctionCall
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; set; }
}

/// <summary>模型发起的工具调用（流式时为按 index 的增量片段）</summary>
public sealed class DeepSeekToolCall
{
    /// <summary>流式增量中的序号，用于拼接分片</summary>
    [JsonPropertyName("index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Index { get; set; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; } = "function";

    [JsonPropertyName("function")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeepSeekFunctionCall? Function { get; set; }

    public static DeepSeekToolCall Create(string id, string name, string argumentsJson) => new()
    {
        Id = id,
        Function = new DeepSeekFunctionCall { Name = name, Arguments = argumentsJson }
    };
}

/// <summary>函数工具定义</summary>
public sealed class DeepSeekFunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>JSON Schema；省略表示无参数</summary>
    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Parameters { get; set; }

    /// <summary>strict 模式（Beta，需要 beta 接口，客户端会自动切换）</summary>
    [JsonPropertyName("strict")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Strict { get; set; }
}

/// <summary>
/// 工具定义：function 工具或服务端工具（deepseek_web_search / deepseek_read_url）
/// </summary>
public sealed class DeepSeekToolDefinition
{
    public const string FunctionType = "function";
    public const string WebSearchType = "deepseek_web_search";
    public const string ReadUrlType = "deepseek_read_url";

    [JsonPropertyName("type")]
    public string Type { get; set; } = FunctionType;

    [JsonPropertyName("function")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeepSeekFunctionDefinition? Function { get; set; }

    /// <summary>服务端工具的名称</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonIgnore]
    public bool IsFunction => Type == FunctionType;

    [JsonIgnore]
    public bool IsServerTool => Type is WebSearchType or ReadUrlType;

    /// <summary>工具名称（function 或服务端工具）</summary>
    [JsonIgnore]
    public string? ToolName => IsFunction ? Function?.Name : Name;

    public static DeepSeekToolDefinition ForFunction(string name, string? description,
        JsonElement? parameters = null, bool? strict = null) => new()
    {
        Type = FunctionType,
        Function = new DeepSeekFunctionDefinition
        {
            Name = name,
            Description = description,
            Parameters = parameters?.Clone(),
            Strict = strict
        }
    };

    public static DeepSeekToolDefinition ForFunction(string name, string? description,
        JsonNode? parameters, bool? strict = null) =>
        ForFunction(name, description, ToElement(parameters), strict);

    /// <summary>以 JSON 字符串形式给出 JSON Schema</summary>
    public static DeepSeekToolDefinition ForFunction(string name, string? description,
        string parametersJson, bool? strict = null)
    {
        using var doc = JsonDocument.Parse(parametersJson);
        return ForFunction(name, description, doc.RootElement.Clone(), strict);
    }

    public static DeepSeekToolDefinition ForWebSearch() =>
        new() { Type = WebSearchType, Name = WebSearchType };

    public static DeepSeekToolDefinition ForReadUrl() =>
        new() { Type = ReadUrlType, Name = ReadUrlType };

    private static JsonElement? ToElement(JsonNode? node) =>
        node == null ? null : JsonSerializer.SerializeToElement(node);

    /// <summary>验证 function 名称是否满足 [a-zA-Z0-9_-]{1,128}</summary>
    public static bool IsValidFunctionName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 128)
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>tool_choice：none/auto/required 或指定函数</summary>
[JsonConverter(typeof(DeepSeekToolChoiceConverter))]
public sealed class DeepSeekToolChoice
{
    public const string NoneMode = "none";
    public const string AutoMode = "auto";
    public const string RequiredMode = "required";

    /// <summary>none/auto/required；指定函数时为 null</summary>
    public string? Mode { get; init; }

    /// <summary>指定的函数名</summary>
    public string? FunctionName { get; init; }

    public static DeepSeekToolChoice None { get; } = new() { Mode = NoneMode };
    public static DeepSeekToolChoice Auto { get; } = new() { Mode = AutoMode };
    public static DeepSeekToolChoice Required { get; } = new() { Mode = RequiredMode };

    public static DeepSeekToolChoice ForFunction(string name) => new() { FunctionName = name };

    /// <summary>是否强制调用（required 或指定函数），思考模式下不被支持</summary>
    public bool IsForced => Mode == RequiredMode || FunctionName != null;
}

internal sealed class DeepSeekToolChoiceConverter : JsonConverter<DeepSeekToolChoice>
{
    public override DeepSeekToolChoice? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return new DeepSeekToolChoice { Mode = reader.GetString() };
            case JsonTokenType.StartObject:
                using (var doc = JsonDocument.ParseValue(ref reader))
                {
                    if (doc.RootElement.TryGetProperty("function", out var fn) &&
                        fn.TryGetProperty("name", out var name))
                    {
                        return DeepSeekToolChoice.ForFunction(name.GetString() ?? string.Empty);
                    }
                }

                return new DeepSeekToolChoice { Mode = DeepSeekToolChoice.AutoMode };
            default:
                throw new JsonException("Unexpected tool_choice value");
        }
    }

    public override void Write(Utf8JsonWriter writer, DeepSeekToolChoice value, JsonSerializerOptions options)
    {
        if (value.FunctionName != null)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "function");
            writer.WriteStartObject("function");
            writer.WriteString("name", value.FunctionName);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteStringValue(value.Mode ?? DeepSeekToolChoice.AutoMode);
        }
    }
}

/// <summary>thinking 参数</summary>
public sealed class DeepSeekThinking
{
    public const string Enabled = "enabled";
    public const string Disabled = "disabled";

    [JsonPropertyName("type")]
    public string Type { get; set; } = Enabled;

    public static DeepSeekThinking On => new() { Type = Enabled };
    public static DeepSeekThinking Off => new() { Type = Disabled };

    [JsonIgnore]
    public bool IsEnabled => Type == Enabled;
}

/// <summary>response_format 参数</summary>
public sealed class DeepSeekResponseFormat
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    public static DeepSeekResponseFormat Text => new() { Type = "text" };
    public static DeepSeekResponseFormat JsonObject => new() { Type = "json_object" };

    [JsonIgnore]
    public bool IsJsonObject => Type == "json_object";
}

public sealed class DeepSeekStreamOptions
{
    [JsonPropertyName("include_usage")]
    public bool IncludeUsage { get; set; } = true;
}

public sealed class DeepSeekSafetyCheck
{
    /// <summary>low 或 high</summary>
    [JsonPropertyName("level")]
    public string Level { get; set; } = "high";
}

public sealed class DeepSeekTitleOptions
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// POST /chat/completions 请求。未设置的字段由 <see cref="DeepSeekOptions"/> 补全默认值。
/// </summary>
public sealed class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    [JsonPropertyName("messages")]
    public List<DeepSeekMessage> Messages { get; set; } = new();

    [JsonPropertyName("thinking")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeepSeekThinking? Thinking { get; set; }

    /// <summary>none/low/high/max（亦接受 minimal/medium/xhigh/ultra，发送前归一化）</summary>
    [JsonPropertyName("reasoning_effort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningEffort { get; set; }

    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("response_format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeepSeekResponseFormat? ResponseFormat { get; set; }

    [JsonPropertyName("stop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Stop { get; set; }

    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Stream { get; set; }

    [JsonPropertyName("stream_options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeepSeekStreamOptions? StreamOptions { get; set; }

    /// <summary>思考模式下不生效，客户端不会发送</summary>
    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopP { get; set; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DeepSeekToolDefinition>? Tools { get; set; }

    /// <summary>思考模式下不支持 required 与指定函数</summary>
    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeepSeekToolChoice? ToolChoice { get; set; }

    [JsonPropertyName("logprobs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Logprobs { get; set; }

    [JsonPropertyName("top_logprobs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TopLogprobs { get; set; }

    [JsonPropertyName("user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; set; }

    [JsonPropertyName("safety_check")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeepSeekSafetyCheck? SafetyCheck { get; set; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeepSeekTitleOptions? Title { get; set; }

    /// <summary>浅拷贝（Messages/Tools 等集合引用共享，客户端发送前会重新构造）</summary>
    public ChatCompletionRequest Clone() => (ChatCompletionRequest)MemberwiseClone();
}

/// <summary>completion_tokens_details</summary>
public sealed class DeepSeekCompletionTokensDetails
{
    [JsonPropertyName("reasoning_tokens")]
    public int ReasoningTokens { get; set; }
}

/// <summary>Token 用量</summary>
public sealed class DeepSeekUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }

    /// <summary>命中上下文缓存的输入 token</summary>
    [JsonPropertyName("prompt_cache_hit_tokens")]
    public int PromptCacheHitTokens { get; set; }

    /// <summary>未命中上下文缓存的输入 token</summary>
    [JsonPropertyName("prompt_cache_miss_tokens")]
    public int PromptCacheMissTokens { get; set; }

    [JsonPropertyName("completion_tokens_details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeepSeekCompletionTokensDetails? CompletionTokensDetails { get; set; }

    [JsonIgnore]
    public int ReasoningTokens => CompletionTokensDetails?.ReasoningTokens ?? 0;

    /// <summary>累加两次用量（用于工具循环的多轮请求）</summary>
    public DeepSeekUsage Plus(DeepSeekUsage? other)
    {
        if (other == null)
        {
            return this;
        }

        return new DeepSeekUsage
        {
            PromptTokens = PromptTokens + other.PromptTokens,
            CompletionTokens = CompletionTokens + other.CompletionTokens,
            TotalTokens = TotalTokens + other.TotalTokens,
            PromptCacheHitTokens = PromptCacheHitTokens + other.PromptCacheHitTokens,
            PromptCacheMissTokens = PromptCacheMissTokens + other.PromptCacheMissTokens,
            CompletionTokensDetails = new DeepSeekCompletionTokensDetails
            {
                ReasoningTokens = ReasoningTokens + other.ReasoningTokens
            }
        };
    }
}

/// <summary>API 错误体：{"error": {...}}</summary>
public sealed class DeepSeekErrorBody
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("param")]
    public JsonElement? Param { get; set; }

    [JsonPropertyName("code")]
    public JsonElement? Code { get; set; }
}

public sealed class ChatCompletionChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public DeepSeekMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }

    [JsonPropertyName("logprobs")]
    public JsonElement? Logprobs { get; set; }
}

/// <summary>非流式 chat completion 响应</summary>
public sealed class ChatCompletionResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("system_fingerprint")]
    public string? SystemFingerprint { get; set; }

    [JsonPropertyName("choices")]
    public List<ChatCompletionChoice> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    public DeepSeekUsage? Usage { get; set; }

    /// <summary>生成失败时 HTTP 状态仍为 200，错误在此字段</summary>
    [JsonPropertyName("error")]
    public DeepSeekErrorBody? Error { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonIgnore]
    public DeepSeekMessage? Message => Choices.FirstOrDefault()?.Message;

    [JsonIgnore]
    public string? Content => Message?.Content;

    [JsonIgnore]
    public string? ReasoningContent => Message?.ReasoningContent;

    [JsonIgnore]
    public IReadOnlyList<DeepSeekToolCall> ToolCalls =>
        (IReadOnlyList<DeepSeekToolCall>?)Message?.ToolCalls ?? Array.Empty<DeepSeekToolCall>();

    [JsonIgnore]
    public string? FinishReason => Choices.FirstOrDefault()?.FinishReason;
}

/// <summary>流式增量</summary>
public sealed class DeepSeekMessageDelta
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<DeepSeekToolCall>? ToolCalls { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ChatCompletionChunkChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("delta")]
    public DeepSeekMessageDelta? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }

    /// <summary>联网工具过程事件（扩展字段）</summary>
    [JsonPropertyName("server_tool_event")]
    public JsonElement? ServerToolEvent { get; set; }

    [JsonPropertyName("logprobs")]
    public JsonElement? Logprobs { get; set; }
}

/// <summary>流式 chat completion 的一个 SSE chunk</summary>
public sealed class ChatCompletionChunk
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("system_fingerprint")]
    public string? SystemFingerprint { get; set; }

    [JsonPropertyName("choices")]
    public List<ChatCompletionChunkChoice> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    public DeepSeekUsage? Usage { get; set; }

    /// <summary>流中途失败时最后一个 chunk 带 error</summary>
    [JsonPropertyName("error")]
    public DeepSeekErrorBody? Error { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonIgnore]
    public DeepSeekMessageDelta? Delta => Choices.FirstOrDefault()?.Delta;

    [JsonIgnore]
    public string? FinishReason => Choices.FirstOrDefault()?.FinishReason;
}

/// <summary>
/// 把流式 chunk 拼接为完整的 assistant 消息（按 index 拼接 tool_calls 分片）
/// </summary>
public sealed class ChatCompletionStreamAccumulator
{
    private readonly StringBuilder _content = new();
    private readonly StringBuilder _reasoning = new();
    private readonly SortedDictionary<int, ToolCallBuilder> _toolCalls = new();
    private int _lastIndex = -1;

    public string? Id { get; private set; }
    public string? Model { get; private set; }
    public string? FinishReason { get; private set; }
    public DeepSeekUsage? Usage { get; private set; }

    public string Content => _content.ToString();
    public string ReasoningContent => _reasoning.ToString();
    public bool HasToolCalls => _toolCalls.Count > 0;

    public void Add(ChatCompletionChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        Id ??= chunk.Id;
        Model ??= chunk.Model;
        if (chunk.Usage != null)
        {
            Usage = chunk.Usage;
        }

        var choice = chunk.Choices.FirstOrDefault();
        if (choice == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(choice.FinishReason))
        {
            FinishReason = choice.FinishReason;
        }

        var delta = choice.Delta;
        if (delta == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(delta.Content))
        {
            _content.Append(delta.Content);
        }

        if (!string.IsNullOrEmpty(delta.ReasoningContent))
        {
            _reasoning.Append(delta.ReasoningContent);
        }

        if (delta.ToolCalls == null)
        {
            return;
        }

        foreach (var fragment in delta.ToolCalls)
        {
            var index = fragment.Index ?? (fragment.Id != null ? _lastIndex + 1 : Math.Max(_lastIndex, 0));
            _lastIndex = index;

            if (!_toolCalls.TryGetValue(index, out var builder))
            {
                builder = new ToolCallBuilder();
                _toolCalls[index] = builder;
            }

            if (!string.IsNullOrEmpty(fragment.Id))
            {
                builder.Id = fragment.Id;
            }

            if (!string.IsNullOrEmpty(fragment.Type))
            {
                builder.Type = fragment.Type;
            }

            if (!string.IsNullOrEmpty(fragment.Function?.Name))
            {
                builder.Name.Append(fragment.Function.Name);
            }

            if (!string.IsNullOrEmpty(fragment.Function?.Arguments))
            {
                builder.Arguments.Append(fragment.Function.Arguments);
            }
        }
    }

    /// <summary>已拼接完成的工具调用（按 index 升序）；参数为空时规范为 {}</summary>
    public IReadOnlyList<DeepSeekToolCall> BuildToolCalls()
    {
        var result = new List<DeepSeekToolCall>(_toolCalls.Count);
        foreach (var (index, builder) in _toolCalls)
        {
            var args = builder.Arguments.ToString();
            result.Add(new DeepSeekToolCall
            {
                Index = index,
                Id = builder.Id,
                Type = builder.Type,
                Function = new DeepSeekFunctionCall
                {
                    Name = builder.Name.ToString(),
                    Arguments = string.IsNullOrWhiteSpace(args) ? "{}" : args
                }
            });
        }

        return result;
    }

    /// <summary>构造完整的 assistant 消息</summary>
    public DeepSeekMessage BuildMessage() => DeepSeekMessage.Assistant(
        Content,
        _reasoning.Length > 0 ? ReasoningContent : null,
        BuildToolCalls());

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }
        public string? Type { get; set; } = "function";
        public StringBuilder Name { get; } = new();
        public StringBuilder Arguments { get; } = new();
    }
}

/// <summary>GET /models 返回的模型信息</summary>
public sealed class DeepSeekModelInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("owned_by")]
    public string? OwnedBy { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("context_window")]
    public int? ContextWindow { get; set; }

    [JsonPropertyName("max_output_tokens")]
    public int? MaxOutputTokens { get; set; }

    [JsonPropertyName("input_modalities")]
    public List<string>? InputModalities { get; set; }

    [JsonPropertyName("output_modalities")]
    public List<string>? OutputModalities { get; set; }

    [JsonPropertyName("effort")]
    public DeepSeekModelEffort? Effort { get; set; }

    [JsonPropertyName("api_capabilities")]
    public JsonElement? ApiCapabilities { get; set; }
}

public sealed class DeepSeekModelEffort
{
    [JsonPropertyName("supported_levels")]
    public List<string> SupportedLevels { get; set; } = new();

    [JsonPropertyName("default_level")]
    public string? DefaultLevel { get; set; }
}

public sealed class DeepSeekModelList
{
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("data")]
    public List<DeepSeekModelInfo> Data { get; set; } = new();
}

/// <summary>单一币种的余额（金额以字符串返回）</summary>
public sealed class DeepSeekBalanceInfo
{
    /// <summary>CNY 或 USD</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("total_balance")]
    public string TotalBalance { get; set; } = "0";

    [JsonPropertyName("granted_balance")]
    public string GrantedBalance { get; set; } = "0";

    [JsonPropertyName("topped_up_balance")]
    public string ToppedUpBalance { get; set; } = "0";

    [JsonIgnore]
    public decimal Total => ParseAmount(TotalBalance);

    [JsonIgnore]
    public decimal Granted => ParseAmount(GrantedBalance);

    [JsonIgnore]
    public decimal ToppedUp => ParseAmount(ToppedUpBalance);

    private static decimal ParseAmount(string? value) =>
        decimal.TryParse(value, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;
}

/// <summary>GET /user/balance 响应</summary>
public sealed class DeepSeekBalance
{
    [JsonPropertyName("is_available")]
    public bool IsAvailable { get; set; }

    [JsonPropertyName("balance_infos")]
    public List<DeepSeekBalanceInfo> BalanceInfos { get; set; } = new();
}

/// <summary>FIM 补全请求（Beta，使用 /beta/completions）</summary>
public sealed class FimCompletionRequest
{
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("suffix")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Suffix { get; set; }

    /// <summary>不能与 suffix 或 logprobs 同时使用</summary>
    [JsonPropertyName("echo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Echo { get; set; }

    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("stop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Stop { get; set; }

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopP { get; set; }

    [JsonPropertyName("logprobs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Logprobs { get; set; }

    public FimCompletionRequest Clone() => (FimCompletionRequest)MemberwiseClone();
}

public sealed class FimCompletionChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }

    [JsonPropertyName("logprobs")]
    public JsonElement? Logprobs { get; set; }
}

public sealed class FimCompletionResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("choices")]
    public List<FimCompletionChoice> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    public DeepSeekUsage? Usage { get; set; }

    [JsonPropertyName("error")]
    public DeepSeekErrorBody? Error { get; set; }

    [JsonIgnore]
    public string? Text => Choices.FirstOrDefault()?.Text;
}
