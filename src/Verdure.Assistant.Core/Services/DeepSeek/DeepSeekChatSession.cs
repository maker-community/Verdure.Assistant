using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services.DeepSeek;

/// <summary>
/// 有状态的多轮对话：维护历史、运行工具调用循环、裁剪历史。
/// 每一轮对话先在局部缓冲区中构建，成功完成才提交到历史，因此失败/取消会自然回滚。
/// 同一时间只允许一个进行中的轮次。
/// </summary>
public sealed class DeepSeekChatSession
{
    /// <summary>单轮对话中最多向模型发起的请求次数（含工具循环）</summary>
    public const int MaxToolRounds = 8;

    /// <summary>附了图片但没写问题时使用的默认提问</summary>
    public const string DefaultImageQuestion = "请描述这张图片的内容。";

    private const string JsonInstruction = "请严格只输出一个合法的 JSON（json）对象，不要输出任何其它文字。";

    private readonly IDeepSeekClient _client;
    private readonly DeepSeekOptions _options;
    private readonly IDeepSeekToolExecutor? _tools;
    private readonly ILogger<DeepSeekChatSession>? _logger;
    private readonly object _gate = new();
    private readonly List<DeepSeekMessage> _history = new();
    private string? _systemPrompt;
    private CancellationTokenSource? _currentCts;
    private int _epoch;
    private int _busy;

    public DeepSeekChatSession(
        IDeepSeekClient client,
        DeepSeekOptions? options = null,
        IDeepSeekToolExecutor? toolExecutor = null,
        ILogger<DeepSeekChatSession>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? client.Options;
        _tools = toolExecutor;
        _logger = logger;
        _systemPrompt = _options.SystemPrompt;
    }

    /// <summary>历史消息快照（不含 system prompt，不含进行中的轮次）</summary>
    public IReadOnlyList<DeepSeekMessage> History
    {
        get
        {
            lock (_gate)
            {
                return _history.ToArray();
            }
        }
    }

    public string? SystemPrompt
    {
        get
        {
            lock (_gate)
            {
                return _systemPrompt;
            }
        }
    }

    /// <summary>是否有进行中的轮次</summary>
    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    /// <summary>
    /// 覆盖本会话后续请求的思考模式：true 开启、false 关闭、null 跟随 <see cref="DeepSeekOptions.ThinkingEnabled"/>。
    /// 语音对话用它关闭思考以缩短首字延迟。
    /// </summary>
    public bool? ThinkingOverride { get; set; }

    /// <summary>设置 system prompt；null 或空白表示不发送 system 消息。对下一次请求生效。</summary>
    public void SetSystemPrompt(string? prompt)
    {
        lock (_gate)
        {
            _systemPrompt = string.IsNullOrWhiteSpace(prompt) ? null : prompt;
        }
    }

    /// <summary>取消进行中的轮次（若有）并清空历史</summary>
    public void Reset()
    {
        CancelCurrent();
        lock (_gate)
        {
            _epoch++;
            _history.Clear();
        }
    }

    /// <summary>取消进行中的轮次；历史保持在该轮开始之前的状态。没有进行中的轮次时无副作用。</summary>
    public void CancelCurrent()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _currentCts;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 轮次恰好刚结束
        }
    }

    /// <summary>
    /// 发送用户消息并以流式事件返回结果（含工具循环）。运行期错误与取消以 <see cref="DeepSeekChatEvent.Failed"/> 事件结束，不抛异常；
    /// 若已有轮次进行中则抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    public async IAsyncEnumerable<DeepSeekChatEvent> SendAsync(
        string userText, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userText);

        await foreach (var ev in RunTurnAsync(userText, null, stream: true, cancellationToken).ConfigureAwait(false))
        {
            yield return ev;
        }
    }

    /// <summary>
    /// 发送带图片的用户消息（图片理解）。文字可为空，此时默认让模型描述图片。图片会保留在后续上下文中，
    /// 因此可以继续追问；"新对话"（<see cref="Reset"/>）会一并清除。
    /// </summary>
    public async IAsyncEnumerable<DeepSeekChatEvent> SendAsync(
        string userText, IReadOnlyList<DeepSeekImage> images, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userText);
        }

        await foreach (var ev in RunTurnAsync(userText, images, stream: true, cancellationToken).ConfigureAwait(false))
        {
            yield return ev;
        }
    }

    /// <summary>
    /// 非流式提问，返回完整回答（同样使用历史并运行工具循环）。失败时抛出异常，取消时抛出 <see cref="OperationCanceledException"/>。
    /// </summary>
    public async Task<string> AskAsync(string userText, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userText);

        await foreach (var ev in RunTurnAsync(userText, null, stream: false, cancellationToken).ConfigureAwait(false))
        {
            switch (ev)
            {
                case DeepSeekChatEvent.Completed completed:
                    return completed.Content;
                case DeepSeekChatEvent.Failed { IsCancelled: true }:
                    throw new OperationCanceledException(cancellationToken);
                case DeepSeekChatEvent.Failed failed:
                    throw failed.Exception;
            }
        }

        throw new InvalidOperationException("对话未产生结果");
    }

    /// <summary>
    /// 使用 json_object 模式提问并反序列化结果。不使用也不修改对话历史，不声明任何工具；
    /// 会自动在 system prompt 中补充要求输出 json 的指令。内容为空时返回 default。
    /// </summary>
    public async Task<T?> AskJsonAsync<T>(string userText, JsonSerializerOptions? jsonOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userText);

        var system = SystemPrompt;
        var combined = string.IsNullOrEmpty(system) ? JsonInstruction : $"{system}\n\n{JsonInstruction}";

        var request = new ChatCompletionRequest
        {
            ResponseFormat = DeepSeekResponseFormat.JsonObject,
            Messages =
            {
                DeepSeekMessage.System(combined),
                DeepSeekMessage.User(userText)
            }
        };

        var response = await _client.CreateChatCompletionAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.FinishReason == "length")
        {
            throw new JsonException("模型输出因长度限制被截断，无法解析为 JSON");
        }

        var content = response.Content?.Trim();
        if (string.IsNullOrEmpty(content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content, jsonOptions ?? DeepSeekJson.ResultOptions);
    }

    private async IAsyncEnumerable<DeepSeekChatEvent> RunTurnAsync(
        string userText, IReadOnlyList<DeepSeekImage>? images, bool stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new InvalidOperationException("已有对话轮次正在进行，请先调用 CancelCurrent()");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        List<DeepSeekMessage> baseHistory;
        string? system;
        int epoch;
        lock (_gate)
        {
            _currentCts = cts;
            epoch = _epoch;
            baseHistory = _history.ToList();
            system = _systemPrompt;
        }

        var turn = new List<DeepSeekMessage>
        {
            images is { Count: > 0 }
                ? DeepSeekMessage.User(string.IsNullOrWhiteSpace(userText) ? DefaultImageQuestion : userText, images)
                : DeepSeekMessage.User(userText)
        };

        try
        {
            var inner = RunRoundsAsync(system, baseHistory, turn, stream, cts.Token);
            await using var enumerator = inner.GetAsyncEnumerator(cts.Token);

            while (true)
            {
                DeepSeekChatEvent? current = null;
                Exception? error = null;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    current = enumerator.Current;
                    if (current is DeepSeekChatEvent.Completed)
                    {
                        CommitTurn(turn, epoch);
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                if (error != null)
                {
                    if (error is not OperationCanceledException)
                    {
                        _logger?.LogWarning(error, "DeepSeek chat turn failed");
                    }

                    yield return new DeepSeekChatEvent.Failed(error);
                    yield break;
                }

                yield return current!;
            }
        }
        finally
        {
            lock (_gate)
            {
                _currentCts = null;
            }

            Volatile.Write(ref _busy, 0);
        }
    }

    private void CommitTurn(List<DeepSeekMessage> turn, int epoch)
    {
        lock (_gate)
        {
            if (epoch != _epoch)
            {
                return;
            }

            _history.AddRange(turn);
            TrimHistoryLocked();
        }
    }

    /// <summary>在回合边界（user 消息）处丢弃最旧的完整轮次，保证不会留下孤立的 tool 消息</summary>
    private void TrimHistoryLocked()
    {
        var max = _options.MaxHistoryMessages;
        if (max <= 0 || _history.Count <= max)
        {
            return;
        }

        var starts = new List<int>();
        for (var i = 0; i < _history.Count; i++)
        {
            if (_history[i].Role == DeepSeekRoles.User)
            {
                starts.Add(i);
            }
        }

        if (starts.Count == 0)
        {
            return;
        }

        var cut = -1;
        foreach (var start in starts)
        {
            if (start > 0 && _history.Count - start <= max)
            {
                cut = start;
                break;
            }
        }

        // 即使最新一轮本身超限，也至少保留它
        if (cut < 0)
        {
            cut = starts[^1];
        }

        if (cut > 0)
        {
            _history.RemoveRange(0, cut);
        }
    }

    private async IAsyncEnumerable<DeepSeekChatEvent> RunRoundsAsync(
        string? system,
        List<DeepSeekMessage> baseHistory,
        List<DeepSeekMessage> turn,
        bool stream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        DeepSeekUsage? totalUsage = null;
        var toolDefinitions = _tools?.GetTools();

        for (var round = 1; round <= MaxToolRounds; round++)
        {
            ct.ThrowIfCancellationRequested();

            var request = new ChatCompletionRequest
            {
                Tools = toolDefinitions is { Count: > 0 } ? toolDefinitions.ToList() : null,
                Thinking = ThinkingOverride is { } thinking ? (thinking ? DeepSeekThinking.On : DeepSeekThinking.Off) : null
            };
            if (system != null)
            {
                request.Messages.Add(DeepSeekMessage.System(system));
            }

            request.Messages.AddRange(baseHistory);
            request.Messages.AddRange(turn);

            DeepSeekMessage assistant;
            string? finishReason;
            DeepSeekUsage? usage;

            if (stream)
            {
                var accumulator = new ChatCompletionStreamAccumulator();
                await foreach (var chunk in _client.StreamChatCompletionAsync(request, ct).ConfigureAwait(false))
                {
                    accumulator.Add(chunk);
                    var delta = chunk.Delta;
                    if (!string.IsNullOrEmpty(delta?.ReasoningContent))
                    {
                        yield return new DeepSeekChatEvent.ReasoningDelta(delta.ReasoningContent);
                    }

                    if (!string.IsNullOrEmpty(delta?.Content))
                    {
                        yield return new DeepSeekChatEvent.ContentDelta(delta.Content);
                    }
                }

                assistant = accumulator.BuildMessage();
                finishReason = accumulator.FinishReason;
                usage = accumulator.Usage;
            }
            else
            {
                var response = await _client.CreateChatCompletionAsync(request, ct).ConfigureAwait(false);
                var choice = response.Choices.FirstOrDefault();
                if (choice?.Message == null)
                {
                    throw new DeepSeekApiException("DeepSeek 响应不包含 choices", isRetryable: false);
                }

                assistant = DeepSeekMessage.Assistant(
                    choice.Message.Content ?? string.Empty,
                    choice.Message.ReasoningContent,
                    choice.Message.ToolCalls);
                finishReason = choice.FinishReason;
                usage = response.Usage;

                if (!string.IsNullOrEmpty(assistant.ReasoningContent))
                {
                    yield return new DeepSeekChatEvent.ReasoningDelta(assistant.ReasoningContent);
                }

                if (!string.IsNullOrEmpty(assistant.Content))
                {
                    yield return new DeepSeekChatEvent.ContentDelta(assistant.Content);
                }
            }

            if (usage != null)
            {
                totalUsage = totalUsage == null ? usage : totalUsage.Plus(usage);
                yield return new DeepSeekChatEvent.UsageReported(usage);
            }

            if (finishReason == "insufficient_system_resource")
            {
                throw new DeepSeekApiException("DeepSeek 服务资源不足，生成被中断", isRetryable: true);
            }

            var calls = NormalizeToolCalls(assistant.ToolCalls, round);
            if (calls.Count == 0)
            {
                assistant.Content ??= string.Empty;
                turn.Add(assistant);
                yield return new DeepSeekChatEvent.Completed(assistant.Content, assistant.ReasoningContent, totalUsage, finishReason);
                yield break;
            }

            if (_tools == null)
            {
                throw new InvalidOperationException("模型请求了工具调用，但会话未配置工具执行器");
            }

            if (finishReason == "length")
            {
                throw new InvalidOperationException("模型输出因长度限制被截断，工具调用参数不完整");
            }

            assistant.ToolCalls = calls;
            turn.Add(assistant);

            foreach (var call in calls)
            {
                yield return new DeepSeekChatEvent.ToolCallRequested(call.Id!, call.Function!.Name!, call.Function.Arguments!);
            }

            var pending = calls.Select((call, index) => ExecuteToolAsync(call, index, ct)).ToList();
            var results = new string[calls.Count];
            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(finished);
                var outcome = await finished.ConfigureAwait(false);
                results[outcome.Index] = outcome.Result;
                var call = calls[outcome.Index];
                yield return new DeepSeekChatEvent.ToolCallCompleted(call.Id!, call.Function!.Name!, outcome.Result, outcome.IsError);
            }

            for (var i = 0; i < calls.Count; i++)
            {
                turn.Add(DeepSeekMessage.Tool(calls[i].Id!, results[i]));
            }
        }

        throw new InvalidOperationException($"工具调用循环超过最大轮数（{MaxToolRounds}）");
    }

    private static List<DeepSeekToolCall> NormalizeToolCalls(List<DeepSeekToolCall>? calls, int round)
    {
        var result = new List<DeepSeekToolCall>();
        if (calls == null)
        {
            return result;
        }

        for (var i = 0; i < calls.Count; i++)
        {
            var call = calls[i];
            var args = call.Function?.Arguments;
            result.Add(new DeepSeekToolCall
            {
                Index = i,
                Id = string.IsNullOrEmpty(call.Id) ? $"call_{round}_{i}" : call.Id,
                Type = "function",
                Function = new DeepSeekFunctionCall
                {
                    Name = call.Function?.Name ?? string.Empty,
                    Arguments = string.IsNullOrWhiteSpace(args) ? "{}" : args
                }
            });
        }

        return result;
    }

    private readonly record struct ToolOutcome(int Index, string Result, bool IsError);

    private async Task<ToolOutcome> ExecuteToolAsync(DeepSeekToolCall call, int index, CancellationToken ct)
    {
        var name = call.Function!.Name!;
        try
        {
            var result = await _tools!.ExecuteAsync(name, call.Function.Arguments!, ct).ConfigureAwait(false);
            return new ToolOutcome(index, string.IsNullOrEmpty(result) ? "OK" : result, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "DeepSeek tool {Tool} failed", name);
            var error = JsonSerializer.Serialize(new { error = ex.Message, tool = name }, DeepSeekJson.Options);
            return new ToolOutcome(index, error, true);
        }
    }
}
