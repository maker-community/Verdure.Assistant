using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services.DeepSeek;

/// <summary>
/// 基于 HttpClient 的 DeepSeek API 客户端：应用默认配置、校验思考模式限制、
/// 对 429/500/503/网络错误做指数退避重试（流式仅在收到响应头之前重试）。
/// </summary>
public sealed class DeepSeekClient : IDeepSeekClient
{
    private const int MaxStopSequences = 16;

    private readonly HttpClient _http;
    private readonly ILogger<DeepSeekClient>? _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="http">HttpClient；建议 Timeout 设为 Timeout.InfiniteTimeSpan，由 <see cref="DeepSeekOptions.TimeoutSeconds"/> 控制超时</param>
    /// <param name="options">配置（保存引用，运行时修改会生效）</param>
    /// <param name="logger">可选日志，不会记录 API Key 或消息内容</param>
    /// <param name="delay">重试等待函数，默认 Task.Delay；主要用于测试</param>
    public DeepSeekClient(
        HttpClient http,
        DeepSeekOptions options,
        ILogger<DeepSeekClient>? logger = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _delay = delay ?? ((t, ct) => Task.Delay(t, ct));
    }

    public DeepSeekOptions Options { get; }

    public async Task<ChatCompletionResponse> CreateChatCompletionAsync(
        ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var (prepared, beta) = PrepareChatRequest(request, stream: false);
        var json = JsonSerializer.Serialize(prepared, DeepSeekJson.Options);
        var uri = BuildUri("chat/completions", beta);

        var response = await SendWithRetryAsync(
            () => CreatePost(uri, json, accept: "application/json"),
            (resp, ct) => ReadJsonAsync<ChatCompletionResponse>(resp, ct),
            cancellationToken).ConfigureAwait(false);

        ThrowIfBodyError(response.Error, HttpStatusCode.OK);
        return response;
    }

    public IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(
        ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        // 同步校验，使参数错误在调用时就抛出
        var (prepared, beta) = PrepareChatRequest(request, stream: true);
        return StreamCoreAsync(prepared, beta, cancellationToken);
    }

    public async Task<IReadOnlyList<DeepSeekModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var uri = BuildUri("models", beta: false);
        var list = await SendWithRetryAsync(
            () => CreateGet(uri),
            (resp, ct) => ReadJsonAsync<DeepSeekModelList>(resp, ct),
            cancellationToken).ConfigureAwait(false);
        return list.Data;
    }

    public Task<DeepSeekBalance> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        var uri = BuildUri("user/balance", beta: false);
        return SendWithRetryAsync(
            () => CreateGet(uri),
            (resp, ct) => ReadJsonAsync<DeepSeekBalance>(resp, ct),
            cancellationToken);
    }

    public async Task<FimCompletionResponse> CreateFimCompletionAsync(
        FimCompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Prompt == null)
        {
            throw new ArgumentException("FIM prompt 不能为空", nameof(request));
        }

        if (request.Echo == true && (request.Suffix != null || request.Logprobs != null))
        {
            throw new ArgumentException("echo 不能与 suffix 或 logprobs 同时使用", nameof(request));
        }

        ValidateStop(request.Stop);

        var prepared = request.Clone();
        prepared.Model = string.IsNullOrWhiteSpace(prepared.Model) ? Options.Model : prepared.Model;
        prepared.MaxTokens ??= Options.MaxTokens;
        prepared.Temperature ??= Options.Temperature;
        prepared.TopP ??= Options.TopP;

        var json = JsonSerializer.Serialize(prepared, DeepSeekJson.Options);
        var uri = BuildUri("completions", beta: true);

        var response = await SendWithRetryAsync(
            () => CreatePost(uri, json, accept: "application/json"),
            (resp, ct) => ReadJsonAsync<FimCompletionResponse>(resp, ct),
            cancellationToken).ConfigureAwait(false);

        ThrowIfBodyError(response.Error, HttpStatusCode.OK);
        return response;
    }

    public Task<ChatCompletionResponse> CreatePrefixCompletionAsync(
        IEnumerable<DeepSeekMessage> messages,
        string prefix,
        IEnumerable<string>? stop = null,
        bool enableThinking = false,
        int? maxTokens = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(prefix);

        var request = new ChatCompletionRequest
        {
            Thinking = enableThinking ? DeepSeekThinking.On : DeepSeekThinking.Off,
            Stop = stop?.ToList(),
            MaxTokens = maxTokens
        };
        request.Messages.AddRange(messages);
        request.Messages.Add(DeepSeekMessage.AssistantPrefix(prefix));
        return CreateChatCompletionAsync(request, cancellationToken);
    }

    #region 请求准备

    private (ChatCompletionRequest Request, bool Beta) PrepareChatRequest(ChatCompletionRequest request, bool stream)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Messages == null || request.Messages.Count == 0)
        {
            throw new ArgumentException("messages 至少需要 1 条", nameof(request));
        }

        ValidateStop(request.Stop);

        var r = request.Clone();
        r.Model = string.IsNullOrWhiteSpace(r.Model) ? Options.Model : r.Model;

        var (thinkingEnabled, effort) = ResolveThinking(r);
        r.Thinking = thinkingEnabled ? DeepSeekThinking.On : DeepSeekThinking.Off;
        r.ReasoningEffort = thinkingEnabled ? effort : null;

        if (thinkingEnabled && r.ToolChoice is { IsForced: true })
        {
            throw new ArgumentException("思考模式下不支持 tool_choice=required 或指定函数，请改用 auto/none 或关闭思考模式", nameof(request));
        }

        r.Temperature = thinkingEnabled ? null : r.Temperature ?? Options.Temperature;
        r.TopP ??= Options.TopP;
        r.MaxTokens ??= Options.MaxTokens;
        r.UserId ??= Options.UserId;

        r.Tools = BuildTools(r);
        if (r.Tools == null)
        {
            r.ToolChoice = null;
        }
        else if (r.ToolChoice?.FunctionName is { } forced)
        {
            var target = r.Tools.FirstOrDefault(t => t.ToolName == forced);
            if (target == null || target.IsServerTool)
            {
                throw new ArgumentException($"tool_choice 指定的函数不存在或为服务端工具: {forced}", nameof(request));
            }
        }

        var hasServerTools = r.Tools?.Any(t => t.IsServerTool) == true;
        if (hasServerTools && r.ResponseFormat?.IsJsonObject == true)
        {
            throw new ArgumentException("声明联网工具时 response_format 只能为 text", nameof(request));
        }

        var strictTools = r.Tools?.Where(t => t.IsFunction).ToList() ?? new List<DeepSeekToolDefinition>();
        var useStrict = strictTools.Any(t => t.Function?.Strict == true);
        if (useStrict && strictTools.Any(t => t.Function?.Strict != true))
        {
            throw new ArgumentException("strict 模式要求所有 function 工具都设置 strict=true", nameof(request));
        }

        // 携带 tools 时必须回传 reasoning_content；否则剔除以节省 token
        var includeReasoning = r.Tools != null;
        r.Messages = r.Messages.Select(m => m.ToRequestMessage(includeReasoning)).ToList();

        for (var i = 0; i < r.Messages.Count - 1; i++)
        {
            if (r.Messages[i].Prefix == true)
            {
                throw new ArgumentException("只有最后一条 assistant 消息可以设置 prefix", nameof(request));
            }
        }

        var last = r.Messages[^1];
        if (last.Prefix == true && last.Role != DeepSeekRoles.Assistant)
        {
            throw new ArgumentException("prefix 只能用于 assistant 消息", nameof(request));
        }

        r.Stream = stream ? true : null;
        r.StreamOptions = stream ? r.StreamOptions ?? new DeepSeekStreamOptions { IncludeUsage = true } : null;

        return (r, last.Prefix == true || useStrict);
    }

    private (bool Enabled, string? Effort) ResolveThinking(ChatCompletionRequest r)
    {
        string? requested = null;
        if (r.ReasoningEffort != null)
        {
            requested = DeepSeekReasoningEffort.Normalize(r.ReasoningEffort)
                ?? throw new ArgumentException($"无效的 reasoning_effort: {r.ReasoningEffort}");
        }

        var optionEffort = DeepSeekReasoningEffort.Normalize(Options.ReasoningEffort) ?? DeepSeekReasoningEffort.High;

        bool enabled;
        if (r.Thinking != null)
        {
            enabled = r.Thinking.IsEnabled;
        }
        else if (requested != null)
        {
            enabled = requested != DeepSeekReasoningEffort.None;
        }
        else
        {
            enabled = Options.ThinkingEnabled && optionEffort != DeepSeekReasoningEffort.None;
        }

        if (!enabled)
        {
            return (false, null);
        }

        var effort = requested;
        if (effort == null || effort == DeepSeekReasoningEffort.None)
        {
            effort = optionEffort == DeepSeekReasoningEffort.None ? DeepSeekReasoningEffort.High : optionEffort;
        }

        return (true, effort);
    }

    private List<DeepSeekToolDefinition>? BuildTools(ChatCompletionRequest r)
    {
        var tools = r.Tools?.ToList() ?? new List<DeepSeekToolDefinition>();

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (tool.IsFunction)
            {
                if (!DeepSeekToolDefinition.IsValidFunctionName(tool.Function?.Name))
                {
                    throw new ArgumentException($"无效的工具名称（需匹配 [a-zA-Z0-9_-]{{1,128}}）: {tool.Function?.Name}");
                }
            }
            else if (!tool.IsServerTool)
            {
                throw new ArgumentException($"不支持的工具类型: {tool.Type}");
            }

            if (!names.Add(tool.ToolName!))
            {
                throw new ArgumentException($"工具名称必须唯一: {tool.ToolName}");
            }
        }

        var toolsDisabled = r.ToolChoice?.Mode == DeepSeekToolChoice.NoneMode;
        var jsonMode = r.ResponseFormat?.IsJsonObject == true;
        if (!toolsDisabled && !jsonMode)
        {
            if (Options.EnableWebSearch && names.Add(DeepSeekToolDefinition.WebSearchType))
            {
                tools.Add(DeepSeekToolDefinition.ForWebSearch());
            }

            if (Options.EnableReadUrl && names.Add(DeepSeekToolDefinition.ReadUrlType))
            {
                tools.Add(DeepSeekToolDefinition.ForReadUrl());
            }
        }

        return tools.Count > 0 ? tools : null;
    }

    private static void ValidateStop(List<string>? stop)
    {
        if (stop is { Count: > MaxStopSequences })
        {
            throw new ArgumentException($"stop 最多包含 {MaxStopSequences} 个字符串");
        }
    }

    private Uri BuildUri(string path, bool beta)
    {
        var baseUrl = Options.BaseUrl.TrimEnd('/');
        if (beta && !baseUrl.EndsWith("/beta", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl += "/beta";
        }

        return new Uri($"{baseUrl}/{path}");
    }

    private HttpRequestMessage CreatePost(Uri uri, string json, string accept)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        Authorize(message, accept);
        return message;
    }

    private HttpRequestMessage CreateGet(Uri uri)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, uri);
        Authorize(message, "application/json");
        return message;
    }

    private void Authorize(HttpRequestMessage message, string accept)
    {
        if (string.IsNullOrWhiteSpace(Options.ApiKey))
        {
            throw new InvalidOperationException("DeepSeek ApiKey 未配置");
        }

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.ApiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
    }

    #endregion

    #region 发送、重试与响应解析

    private async Task<T> SendWithRetryAsync<T>(
        Func<HttpRequestMessage> requestFactory,
        Func<HttpResponseMessage, CancellationToken, Task<T>> onSuccess,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, Options.TimeoutSeconds));

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DeepSeekApiException failure;
            HttpResponseMessage? response = null;
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(timeout);

            try
            {
                using var httpRequest = requestFactory();
                response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    // onSuccess 返回后 response 的所有权由调用方决定（流式场景会继续持有）
                    var result = await onSuccess(response, attemptCts.Token).ConfigureAwait(false);
                    if (result is not HttpResponseMessage)
                    {
                        response.Dispose();
                    }

                    response = null;
                    return result;
                }

                failure = await CreateExceptionAsync(response, attemptCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                failure = new DeepSeekApiException($"DeepSeek 请求超时（{timeout.TotalSeconds:0}s）", isRetryable: true, innerException: ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                failure = new DeepSeekApiException($"DeepSeek 网络错误: {ex.Message}", isRetryable: true, innerException: ex);
            }
            finally
            {
                response?.Dispose();
            }

            if (!failure.IsRetryable || attempt >= Options.MaxRetries)
            {
                throw failure;
            }

            var wait = ComputeDelay(attempt, failure.RetryAfter);
            _logger?.LogWarning("DeepSeek request failed (status {Status}), retry {Attempt}/{Max} in {Delay}ms",
                failure.StatusCode?.ToString() ?? "network", attempt + 1, Options.MaxRetries, (int)wait.TotalMilliseconds);
            await _delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan ComputeDelay(int attempt, TimeSpan? retryAfter)
    {
        var max = TimeSpan.FromSeconds(Math.Max(0, Options.RetryMaxDelaySeconds));
        if (retryAfter.HasValue)
        {
            return retryAfter.Value > max ? max : retryAfter.Value;
        }

        var exponential = Options.RetryBaseDelayMs * Math.Pow(2, Math.Min(attempt, 20));
        var jittered = exponential * (0.5 + Random.Shared.NextDouble() * 0.5);
        var delay = TimeSpan.FromMilliseconds(jittered);
        return delay > max ? max : delay;
    }

    private static async Task<DeepSeekApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            body = string.Empty;
        }

        string? message = null, type = null, code = null, param = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object)
                {
                    message = GetString(error, "message");
                    type = GetString(error, "type");
                    code = GetString(error, "code");
                    param = GetString(error, "param");
                }
                else if (error.ValueKind == JsonValueKind.String)
                {
                    message = error.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // 非 JSON 响应体：使用状态码描述
        }

        var status = response.StatusCode;
        var retryAfter = ParseRetryAfter(response);
        var text = $"DeepSeek API 错误 {(int)status} ({DeepSeekApiException.Describe(status)})" +
                   (string.IsNullOrWhiteSpace(message) ? string.Empty : $": {message}");

        return new DeepSeekApiException(
            text, status, type, code, param,
            DeepSeekApiException.IsRetryableStatus(status), retryAfter, Truncate(body));
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.ToString()
        };
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header == null)
        {
            return null;
        }

        if (header.Delta.HasValue)
        {
            return header.Delta.Value < TimeSpan.Zero ? TimeSpan.Zero : header.Delta.Value;
        }

        if (header.Date.HasValue)
        {
            var delta = header.Date.Value - DateTimeOffset.UtcNow;
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        return null;
    }

    private static string Truncate(string text) => text.Length <= 4096 ? text : text[..4096];

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct) where T : class
    {
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<T>(text, DeepSeekJson.Options)
                ?? throw new JsonException("响应为空");
        }
        catch (JsonException ex)
        {
            throw new DeepSeekApiException($"无法解析 DeepSeek 响应: {ex.Message}", response.StatusCode,
                responseBody: Truncate(text), innerException: ex);
        }
    }

    private static void ThrowIfBodyError(DeepSeekErrorBody? error, HttpStatusCode status)
    {
        if (error != null)
        {
            throw FromBodyError(error, status);
        }
    }

    private static DeepSeekApiException FromBodyError(DeepSeekErrorBody error, HttpStatusCode status) =>
        new($"DeepSeek 生成失败: {error.Message ?? error.Type ?? "unknown error"}", status,
            error.Type, error.Code?.ToString(), error.Param?.ToString());

    #endregion

    #region SSE 流式

    private async IAsyncEnumerable<ChatCompletionChunk> StreamCoreAsync(
        ChatCompletionRequest prepared,
        bool beta,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(prepared, DeepSeekJson.Options);
        var uri = BuildUri("chat/completions", beta);

        using var response = await SendWithRetryAsync(
            () => CreatePost(uri, json, accept: "text/event-stream"),
            (resp, _) => Task.FromResult(resp),
            cancellationToken).ConfigureAwait(false);

        var idleTimeout = TimeSpan.FromSeconds(Math.Max(1, Options.TimeoutSeconds));
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var done = false;
        var sawFinish = false;

        while (true)
        {
            string? line;
            try
            {
                idleCts.CancelAfter(idleTimeout);
                line = await reader.ReadLineAsync(idleCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new DeepSeekApiException($"DeepSeek 流式响应空闲超时（{idleTimeout.TotalSeconds:0}s）", innerException: ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                throw new DeepSeekApiException($"DeepSeek 流式读取失败: {ex.Message}", innerException: ex);
            }

            if (line == null)
            {
                break;
            }

            // 空行与以 ":" 开头的注释（如 keep-alive）、以及 event/id/retry 字段都忽略
            if (line.Length == 0 || line[0] == ':' || !line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line.AsSpan(5).TrimStart().ToString();
            if (payload == "[DONE]")
            {
                done = true;
                break;
            }

            if (payload.Length == 0)
            {
                continue;
            }

            ChatCompletionChunk chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(payload, DeepSeekJson.Options)
                    ?? throw new JsonException("空 chunk");
            }
            catch (JsonException ex)
            {
                throw new DeepSeekApiException($"无法解析流式 chunk: {ex.Message}", response.StatusCode,
                    responseBody: Truncate(payload), innerException: ex);
            }

            if (chunk.Error != null)
            {
                throw FromBodyError(chunk.Error, response.StatusCode);
            }

            if (!string.IsNullOrEmpty(chunk.FinishReason))
            {
                sawFinish = true;
            }

            yield return chunk;
        }

        if (!done && !sawFinish)
        {
            throw new DeepSeekApiException("DeepSeek 流式响应在结束前中断", response.StatusCode);
        }
    }

    #endregion
}
