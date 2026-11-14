using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Verdure.Assistant.Core.Services.DeepSeek;

namespace DeepSeek.Tests;

public sealed class RecordedRequest
{
    public HttpMethod Method { get; init; } = HttpMethod.Get;
    public Uri Uri { get; init; } = new("http://localhost");
    public string Body { get; init; } = string.Empty;
    public AuthenticationHeaderValue? Authorization { get; init; }

    public JsonObject Json => (JsonObject)JsonNode.Parse(Body)!;
}

/// <summary>按队列返回预设响应并记录所有请求的假 HttpMessageHandler</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>>> _responders = new();

    public List<RecordedRequest> Requests { get; } = new();

    public FakeHandler Enqueue(Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responders.Enqueue(responder);
        return this;
    }

    public FakeHandler EnqueueJson(string body, HttpStatusCode status = HttpStatusCode.OK,
        Action<HttpResponseMessage>? configure = null) =>
        Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            configure?.Invoke(response);
            return Task.FromResult(response);
        });

    public FakeHandler EnqueueSse(string body) =>
        Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
        }));

    public FakeHandler EnqueueError(HttpStatusCode status, string type = "server_error", string message = "boom",
        Action<HttpResponseMessage>? configure = null) =>
        EnqueueJson(TestData.Json(new { error = new { message, type, param = (string?)null, code = type + "_code" } }),
            status, configure);

    public FakeHandler EnqueueException(Exception ex) =>
        Enqueue((_, _) => throw ex);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest
        {
            Method = request.Method,
            Uri = request.RequestUri!,
            Body = body,
            Authorization = request.Headers.Authorization
        };
        Requests.Add(recorded);

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException($"No response queued for request #{Requests.Count} {request.RequestUri}");
        }

        return await _responders.Dequeue()(recorded, cancellationToken);
    }
}

/// <summary>先给出一段数据，之后一直阻塞直到被取消的流，用于模拟长时间思考</summary>
public sealed class StallingStream : Stream
{
    private readonly byte[] _initial;
    private int _position;

    public StallingStream(string initial) => _initial = Encoding.UTF8.GetBytes(initial);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < _initial.Length)
        {
            var count = Math.Min(buffer.Length, _initial.Length - _position);
            _initial.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

public sealed class FakeToolExecutor : IDeepSeekToolExecutor
{
    public List<DeepSeekToolDefinition> Tools { get; } = new()
    {
        DeepSeekToolDefinition.ForFunction("get_weather", "查询天气",
            """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}""")
    };

    public Func<string, string, CancellationToken, Task<string>> Handler { get; set; } =
        (_, _, _) => Task.FromResult("{\"temp\":20}");

    public List<(string Name, string Args)> Calls { get; } = new();

    public IReadOnlyList<DeepSeekToolDefinition> GetTools() => Tools;

    public Task<string> ExecuteAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        lock (Calls)
        {
            Calls.Add((name, argumentsJson));
        }

        return Handler(name, argumentsJson, cancellationToken);
    }
}

public static class TestData
{
    public static DeepSeekOptions Options(Action<DeepSeekOptions>? configure = null)
    {
        var options = new DeepSeekOptions { ApiKey = "sk-test-key", RetryBaseDelayMs = 10, RetryMaxDelaySeconds = 5 };
        configure?.Invoke(options);
        return options;
    }

    public static (DeepSeekClient Client, FakeHandler Handler, List<TimeSpan> Delays) CreateClient(
        Action<DeepSeekOptions>? configure = null)
    {
        var handler = new FakeHandler();
        var delays = new List<TimeSpan>();
        var client = new DeepSeekClient(
            new HttpClient(handler),
            Options(configure),
            delay: (t, _) =>
            {
                delays.Add(t);
                return Task.CompletedTask;
            });
        return (client, handler, delays);
    }

    public static string Json(object value) => JsonSerializer.Serialize(value);

    /// <summary>构造非流式响应 JSON</summary>
    public static string Completion(string? content, string? reasoning = null, string finish = "stop",
        object? toolCalls = null, object? usage = null) =>
        Json(new
        {
            id = "cmpl-1",
            @object = "chat.completion",
            created = 1,
            model = "deepseek-flash",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content, reasoning_content = reasoning, tool_calls = toolCalls },
                    finish_reason = finish
                }
            },
            usage = usage ?? new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 }
        });

    /// <summary>构造一个 SSE data 行（含结尾空行）</summary>
    public static string Chunk(string? content = null, string? reasoning = null, object? toolCalls = null,
        string? finish = null, object? usage = null) =>
        "data: " + Json(new
        {
            id = "cmpl-1",
            @object = "chat.completion.chunk",
            created = 1,
            model = "deepseek-flash",
            choices = new[]
            {
                new
                {
                    index = 0,
                    delta = new { content, reasoning_content = reasoning, tool_calls = toolCalls },
                    finish_reason = finish
                }
            },
            usage
        }) + "\n\n";

    public const string Done = "data: [DONE]\n\n";

    public static object[] ToolFragment(int index, string? id = null, string? name = null, string? args = null) =>
        new object[]
        {
            new
            {
                index,
                id,
                type = id != null ? "function" : null,
                function = new { name, arguments = args }
            }
        };

    /// <summary>一次工具调用的完整 SSE 响应（参数拆为多个分片）</summary>
    public static string ToolCallSse(string callId, string name, string reasoning, params string[] argFragments)
    {
        var sb = new StringBuilder();
        sb.Append(": keep-alive\n\n");
        sb.Append(Chunk(reasoning: reasoning));
        sb.Append(Chunk(toolCalls: ToolFragment(0, callId, name, "")));
        foreach (var fragment in argFragments)
        {
            sb.Append(Chunk(toolCalls: ToolFragment(0, args: fragment)));
        }

        sb.Append(Chunk(finish: "tool_calls", usage: new { prompt_tokens = 20, completion_tokens = 8, total_tokens = 28 }));
        sb.Append(Done);
        return sb.ToString();
    }

    public static string TextSse(string content, string? reasoning = null, int promptTokens = 12)
    {
        var sb = new StringBuilder();
        if (reasoning != null)
        {
            sb.Append(Chunk(reasoning: reasoning));
        }

        foreach (var piece in content.Select(c => c.ToString()))
        {
            sb.Append(Chunk(content: piece));
        }

        sb.Append(Chunk(finish: "stop", usage: new
        {
            prompt_tokens = promptTokens,
            completion_tokens = 3,
            total_tokens = promptTokens + 3,
            prompt_cache_hit_tokens = 4,
            prompt_cache_miss_tokens = promptTokens - 4,
            completion_tokens_details = new { reasoning_tokens = reasoning == null ? 0 : 2 }
        }));
        sb.Append(Done);
        return sb.ToString();
    }

    public static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }
}
