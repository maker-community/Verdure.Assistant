using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Verdure.Assistant.Core.Services.DeepSeek;
using Xunit;

namespace DeepSeek.Tests;

public class WavEncoderTests
{
    [Fact]
    public void FromPcm16_WritesStandardHeaderAndPayload()
    {
        var pcm = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();

        var wav = WavEncoder.FromPcm16(pcm, 16000);

        Assert.Equal(44 + pcm.Length, wav.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(36 + pcm.Length, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4)));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(16)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)));
        Assert.Equal(16000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        Assert.Equal(32000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(28)));
        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(32)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34)));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(pcm.Length, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
        Assert.Equal(pcm, wav.AsSpan(44).ToArray());
    }

    [Fact]
    public void FromPcm16_Stereo_UsesChannelAwareRates()
    {
        var wav = WavEncoder.FromPcm16(new byte[8], 24000, channels: 2);

        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)));
        Assert.Equal(24000 * 4, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(28)));
        Assert.Equal(4, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(32)));
    }

    [Fact]
    public void FromPcm16_EmptyPcm_ProducesHeaderOnly()
    {
        var wav = WavEncoder.FromPcm16(ReadOnlySpan<byte>.Empty, 16000);
        Assert.Equal(44, wav.Length);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
    }
}

public class DeepSeekAsrTests
{
    private sealed record Captured(HttpMethod Method, Uri Uri, string? Authorization, string ContentType, byte[] Body);

    private sealed class AsrHandler : HttpMessageHandler
    {
        private readonly Queue<Func<Captured, CancellationToken, Task<HttpResponseMessage>>> _responders = new();
        public List<Captured> Requests { get; } = new();

        public AsrHandler Enqueue(Func<Captured, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responders.Enqueue(responder);
            return this;
        }

        public AsrHandler EnqueueJson(string body, HttpStatusCode status = HttpStatusCode.OK,
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

        public AsrHandler EnqueueOk(string text = "你好", int durationMs = 1200, string requestId = "req-1") =>
            EnqueueJson(JsonSerializer.Serialize(new { request_id = requestId, text, duration_ms = durationMs }));

        public AsrHandler EnqueueError(HttpStatusCode status, string message = "boom") =>
            EnqueueJson(JsonSerializer.Serialize(new { error = new { message, type = "error_type" } }), status);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var captured = new Captured(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.ToString() ?? string.Empty,
                body);
            Requests.Add(captured);

            if (_responders.Count == 0)
            {
                throw new InvalidOperationException($"No response queued for request #{Requests.Count}");
            }

            return await _responders.Dequeue()(captured, cancellationToken);
        }
    }

    private static (DeepSeekSpeechClient Client, AsrHandler Handler, List<TimeSpan> Delays, DeepSeekOptions Options) Create(
        Action<DeepSeekOptions>? configure = null)
    {
        var handler = new AsrHandler();
        var delays = new List<TimeSpan>();
        var options = TestData.Options(configure);
        var client = new DeepSeekSpeechClient(new HttpClient(handler), options, delay: (t, _) =>
        {
            delays.Add(t);
            return Task.CompletedTask;
        });
        return (client, handler, delays, options);
    }

    private static byte[] ExtractWav(Captured request)
    {
        var contentType = request.ContentType;
        var boundary = contentType.Split("boundary=")[1].Trim('"', ' ');
        var start = request.Body.AsSpan().IndexOf("RIFF"u8);
        var end = request.Body.AsSpan().LastIndexOf(Encoding.ASCII.GetBytes("\r\n--" + boundary + "--"));
        Assert.True(start > 0 && end > start);
        return request.Body.AsSpan(start, end - start).ToArray();
    }

    [Fact]
    public async Task Transcribe_SendsMultipartWavWithBearer_AndParsesResult()
    {
        var (client, handler, _, _) = Create();
        handler.EnqueueOk("你好，世界", 3200, "abc-123");
        var pcm = Enumerable.Range(0, 640).Select(i => (byte)(i % 251)).ToArray();

        var result = await client.TranscribeAsync(pcm, 16000);

        Assert.Equal("你好，世界", result.Text);
        Assert.Equal(3200, result.DurationMs);
        Assert.Equal("abc-123", result.RequestId);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.deepseek.com/v1/tools/asr", request.Uri.ToString());
        Assert.Equal("Bearer sk-test-key", request.Authorization);
        Assert.StartsWith("multipart/form-data", request.ContentType);

        var bodyText = Encoding.UTF8.GetString(request.Body);
        Assert.Contains("name=file", bodyText);
        Assert.Contains("audio/wav", bodyText);

        var wav = ExtractWav(request);
        Assert.Equal(WavEncoder.FromPcm16(pcm, 16000), wav);
    }

    [Fact]
    public async Task Transcribe_UsesBaseUrlAndApiKeyAtCallTime()
    {
        var (client, handler, _, options) = Create();
        handler.EnqueueOk().EnqueueOk();

        await client.TranscribeAsync(new byte[320]);
        options.ApiKey = "sk-rotated";
        options.BaseUrl = "http://127.0.0.1:9999/";
        await client.TranscribeAsync(new byte[320]);

        Assert.Equal("Bearer sk-test-key", handler.Requests[0].Authorization);
        Assert.Equal("Bearer sk-rotated", handler.Requests[1].Authorization);
        Assert.Equal("http://127.0.0.1:9999/v1/tools/asr", handler.Requests[1].Uri.ToString());
    }

    [Fact]
    public async Task Transcribe_MissingFields_DefaultsGracefully()
    {
        var (client, handler, _, _) = Create();
        handler.EnqueueJson("{}");

        var result = await client.TranscribeAsync(new byte[320]);

        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(0, result.DurationMs);
        Assert.Null(result.RequestId);
    }

    [Fact]
    public async Task Transcribe_InvalidJsonBody_ThrowsApiException()
    {
        var (client, handler, _, _) = Create();
        handler.EnqueueJson("not json");

        await Assert.ThrowsAsync<DeepSeekApiException>(() => client.TranscribeAsync(new byte[320]));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(408)]
    [InlineData(413)]
    [InlineData(504)]
    public async Task Transcribe_NonRetriedErrors_MapToApiExceptionImmediately(int status)
    {
        var (client, handler, delays, _) = Create();
        handler.EnqueueError((HttpStatusCode)status, "bad audio");

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => client.TranscribeAsync(new byte[320]));

        Assert.Equal((HttpStatusCode)status, ex.StatusCode);
        Assert.Contains("bad audio", ex.Message);
        Assert.Equal("error_type", ex.ErrorType);
        Assert.Single(handler.Requests);
        Assert.Empty(delays);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Transcribe_RetriesTransientStatus_ThenSucceeds(int status)
    {
        var (client, handler, delays, _) = Create();
        handler.EnqueueError((HttpStatusCode)status).EnqueueError((HttpStatusCode)status).EnqueueOk("好了");

        var result = await client.TranscribeAsync(new byte[320]);

        Assert.Equal("好了", result.Text);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, delays.Count);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Transcribe_RetriesExhausted_ThrowsLastError(int status)
    {
        var (client, handler, delays, _) = Create(o => o.MaxRetries = 2);
        for (var i = 0; i < 3; i++)
        {
            handler.EnqueueError((HttpStatusCode)status);
        }

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => client.TranscribeAsync(new byte[320]));

        Assert.Equal((HttpStatusCode)status, ex.StatusCode);
        Assert.True(ex.IsRetryable);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task Transcribe_HonoursRetryAfter_CappedByMaxDelay()
    {
        var (client, handler, delays, _) = Create(o => o.RetryMaxDelaySeconds = 5);
        handler.EnqueueJson("{}", HttpStatusCode.TooManyRequests,
            r => r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60)));
        handler.EnqueueOk();

        await client.TranscribeAsync(new byte[320]);

        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(delays));
    }

    [Fact]
    public async Task Transcribe_RetriesTransientNetworkError()
    {
        var (client, handler, delays, _) = Create();
        handler.Enqueue((_, _) => throw new HttpRequestException("connection reset"));
        handler.EnqueueOk("恢复");

        var result = await client.TranscribeAsync(new byte[320]);

        Assert.Equal("恢复", result.Text);
        Assert.Single(delays);
    }

    [Fact]
    public async Task Transcribe_NetworkErrorExhausted_ThrowsApiException()
    {
        var (client, handler, _, _) = Create(o => o.MaxRetries = 1);
        handler.Enqueue((_, _) => throw new HttpRequestException("down"));
        handler.Enqueue((_, _) => throw new HttpRequestException("down"));

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => client.TranscribeAsync(new byte[320]));

        Assert.Null(ex.StatusCode);
        Assert.True(ex.IsRetryable);
    }

    [Fact]
    public async Task Transcribe_EmptyPcm_ThrowsArgumentException()
    {
        var (client, handler, _, _) = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => client.TranscribeAsync(ReadOnlyMemory<byte>.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => client.TranscribeAsync(new byte[1]));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Transcribe_OversizedPcm_ThrowsArgumentException()
    {
        var (client, handler, _, _) = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => client.TranscribeAsync(new byte[8 * 1024 * 1024]));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Transcribe_UnsupportedSampleRate_ThrowsArgumentException()
    {
        var (client, _, _, _) = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => client.TranscribeAsync(new byte[320], 12345));
    }

    [Fact]
    public async Task Transcribe_MissingApiKey_ThrowsInvalidOperation()
    {
        var (client, _, _, _) = Create(o => o.ApiKey = "");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.TranscribeAsync(new byte[320]));
    }

    [Fact]
    public async Task Transcribe_CancellationIsHonoured()
    {
        var (client, handler, _, _) = Create();
        handler.Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cts = new CancellationTokenSource();

        var task = client.TranscribeAsync(new byte[320], ct: cts.Token);
        cts.CancelAfter(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Transcribe_AlreadyCancelled_DoesNotSend()
    {
        var (client, handler, _, _) = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.TranscribeAsync(new byte[320], ct: cts.Token));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Transcribe_ApiKeyNeverAppearsInExceptionMessages()
    {
        var (client, handler, _, options) = Create(o =>
        {
            o.ApiKey = "sk-super-secret-value";
            o.MaxRetries = 0;
        });

        foreach (var status in new[] { 400, 413, 429, 503, 504 })
        {
            handler.EnqueueError((HttpStatusCode)status);
            var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => client.TranscribeAsync(new byte[320]));
            Assert.DoesNotContain(options.ApiKey, ex.Message);
            Assert.DoesNotContain(options.ApiKey, ex.ResponseBody ?? string.Empty);
            Assert.DoesNotContain(options.ApiKey, ex.ToString());
        }

        handler.Enqueue((_, _) => throw new HttpRequestException("down"));
        var net = await Assert.ThrowsAsync<DeepSeekApiException>(() => client.TranscribeAsync(new byte[320]));
        Assert.DoesNotContain(options.ApiKey, net.ToString());
    }
}

public class DeepSeekTtsTests
{
    private const string Key = "sk-test-key";

    /// <summary>用 HttpListener 实现的本地假 TTS WebSocket 服务</summary>
    private sealed class FakeTtsServer : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private readonly Func<WebSocket, FakeTtsServer, Task> _script;

        public FakeTtsServer(Func<WebSocket, FakeTtsServer, Task> script, int? rejectStatus = null)
        {
            _script = script;
            RejectStatus = rejectStatus;

            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            BaseUrl = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _loop = Task.Run(AcceptLoopAsync);
        }

        public string BaseUrl { get; }
        public int? RejectStatus { get; }
        public string? Path { get; private set; }
        public string? Query { get; private set; }
        public string? Authorization { get; private set; }
        public List<string> Received { get; } = new();
        public Exception? ScriptError { get; private set; }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            Path = context.Request.Url!.AbsolutePath;
            Query = context.Request.Url.Query;
            Authorization = context.Request.Headers["Authorization"];

            if (RejectStatus.HasValue)
            {
                context.Response.StatusCode = RejectStatus.Value;
                context.Response.Close();
                return;
            }

            WebSocket ws;
            try
            {
                ws = (await context.AcceptWebSocketAsync(null)).WebSocket;
            }
            catch
            {
                return;
            }

            try
            {
                await _script(ws, this);
                // 等客户端先关闭，避免 RST 丢掉尚未读取的数据
                var drain = Task.Run(async () =>
                {
                    var buffer = new byte[1024];
                    while (ws.State is WebSocketState.Open or WebSocketState.CloseSent)
                    {
                        var r = await ws.ReceiveAsync(buffer, _cts.Token);
                        if (r.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }
                    }
                });
                await Task.WhenAny(drain, Task.Delay(5000, _cts.Token));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ScriptError ??= ex;
            }
            finally
            {
                ws.Dispose();
            }
        }

        public async Task<string?> ReadTextAsync(WebSocket ws)
        {
            var buffer = new byte[64 * 1024];
            using var message = new MemoryStream();
            while (true)
            {
                var result = await ws.ReceiveAsync(buffer, _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    var text = Encoding.UTF8.GetString(message.ToArray());
                    lock (Received)
                    {
                        Received.Add(text);
                    }

                    return text;
                }
            }
        }

        public async Task ReadUntilFinishTextAsync(WebSocket ws)
        {
            while (true)
            {
                var text = await ReadTextAsync(ws);
                if (text == null || JsonDocument.Parse(text).RootElement.GetProperty("event").GetString() == "finish_text")
                {
                    return;
                }
            }
        }

        public Task SendAudioAsync(WebSocket ws, byte[] data) =>
            ws.SendAsync(data, WebSocketMessageType.Binary, true, _cts.Token);

        public Task SendFinishAsync(WebSocket ws, string code = "success", string message = "") =>
            ws.SendAsync(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { @event = "finish", code, message })),
                WebSocketMessageType.Text, true, _cts.Token);

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Abort();
            try
            {
                await _loop;
            }
            catch
            {
            }

            _cts.Dispose();
        }
    }

    private static DeepSeekSpeechClient CreateClient(FakeTtsServer server, Action<DeepSeekOptions>? configure = null) =>
        new(new HttpClient(), TestData.Options(o =>
        {
            o.BaseUrl = server.BaseUrl;
            o.ApiKey = Key;
            configure?.Invoke(o);
        }));

    private static async Task<(List<byte[]> Chunks, Exception? Error)> CollectAsync(IDeepSeekTtsSession session)
    {
        var chunks = new List<byte[]>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await foreach (var chunk in session.ReceiveAudioAsync(cts.Token))
            {
                chunks.Add(chunk);
            }

            return (chunks, null);
        }
        catch (Exception ex)
        {
            return (chunks, ex);
        }
    }

    private static byte[] Sequence(int count, int start = 0) =>
        Enumerable.Range(start, count).Select(i => (byte)(i % 256)).ToArray();

    [Fact]
    public async Task OpenTts_SendsPathQueryAndBearerHeader()
    {
        await using var server = new FakeTtsServer(async (ws, s) =>
        {
            await s.ReadUntilFinishTextAsync(ws);
            await s.SendFinishAsync(ws);
        });
        var client = CreateClient(server);

        await using var session = await client.OpenTtsAsync(DeepSeekTtsVoices.Tide);
        await session.FinishTextAsync();
        var (chunks, error) = await CollectAsync(session);

        Assert.Null(error);
        Assert.Empty(chunks);
        Assert.Equal("/v1/tools/tts", server.Path);
        var query = System.Web.HttpUtility.ParseQueryString(server.Query!);
        Assert.Equal("tide", query["voice_id"]);
        Assert.Equal("pcm", query["format"]);
        Assert.Equal("true", query["filter_markdown"]);
        Assert.Equal("Bearer " + Key, server.Authorization);
    }

    [Fact]
    public async Task OpenTts_DefaultVoiceIsStella()
    {
        await using var server = new FakeTtsServer(async (ws, s) =>
        {
            await s.ReadUntilFinishTextAsync(ws);
            await s.SendFinishAsync(ws);
        });
        var client = CreateClient(server);

        await using var session = await client.OpenTtsAsync();
        await session.FinishTextAsync();
        await CollectAsync(session);

        Assert.Equal("stella", System.Web.HttpUtility.ParseQueryString(server.Query!)["voice_id"]);
    }

    [Fact]
    public async Task Tts_SendsTextEventsInOrder_IgnoresBlank_AndReturnsAudio()
    {
        var audio = Sequence(48);
        await using var server = new FakeTtsServer(async (ws, s) =>
        {
            await s.ReadUntilFinishTextAsync(ws);
            await s.SendAudioAsync(ws, audio[..24]);
            await s.SendAudioAsync(ws, audio[24..]);
            await s.SendFinishAsync(ws);
        });
        var client = CreateClient(server);

        await using var session = await client.OpenTtsAsync();
        await session.SendTextAsync("你好，");
        await session.SendTextAsync("");
        await session.SendTextAsync("   \n ");
        await session.SendTextAsync("世界");
        await session.FinishTextAsync();
        var (chunks, error) = await CollectAsync(session);

        Assert.Null(error);
        Assert.Equal(audio, chunks.SelectMany(c => c).ToArray());

        var events = server.Received.Select(r => JsonDocument.Parse(r).RootElement).ToList();
        Assert.Equal(3, events.Count);
        Assert.Equal("text", events[0].GetProperty("event").GetString());
        Assert.Equal("你好，", events[0].GetProperty("text").GetString());
        Assert.Equal("text", events[1].GetProperty("event").GetString());
        Assert.Equal("世界", events[1].GetProperty("text").GetString());
        Assert.Equal("finish_text", events[2].GetProperty("event").GetString());
        Assert.Null(server.ScriptError);
    }

    [Fact]
    public async Task Tts_LargeText_IsSplitWithoutLoss()
    {
        var text = new string('啊', 10000);
        await using var server = new FakeTtsServer(async (ws, s) =>
        {
            await s.ReadUntilFinishTextAsync(ws);
            await s.SendFinishAsync(ws);
        });
        var client = CreateClient(server);

        await using var session = await client.OpenTtsAsync();
        await session.SendTextAsync(text);
        await session.FinishTextAsync();
        var (_, error) = await CollectAsync(session);

        Assert.Null(error);
        var frames = server.Received.Select(r => JsonDocument.Parse(r).RootElement).ToList();
        var textFrames = frames.Where(f => f.GetProperty("event").GetString() == "text").ToList();
        Assert.True(textFrames.Count > 1);
        Assert.All(textFrames, f => Assert.True(Encoding.UTF8.GetByteCount(f.GetProperty("text").GetString()!) <= 16 * 1024));
        Assert.Equal(text, string.Concat(textFrames.Select(f => f.GetProperty("text").GetString())));
    }

    [Fact]
    public async Task Tts_OddLengthFrames_AreRealigned_AndBytesPreserved()
    {
        var audio = Sequence(16, 7);
        var sizes = new[] { 3, 4, 1, 2, 6 };
        await using var server = new FakeTtsServer(async (ws, s) =>
        {
            await s.ReadUntilFinishTextAsync(ws);
            var offset = 0;
            foreach (var size in sizes)
            {
                await s.SendAudioAsync(ws, audio[offset..(offset + size)]);
                offset += size;
            }

            await s.SendFinishAsync(ws);
        });
        var client = CreateClient(server);

        await using var session = await client.OpenTtsAsync();
        await session.SendTextAsync("hi");
        await session.FinishTextAsync();
        var (chunks, error) = await CollectAsync(session);

        Assert.Null(error);
        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Equal(0, c.Length % 2));
        Assert.Equal(audio, chunks.SelectMany(c => c).ToArray());
    }

    [Fact]
    public async Task Tts_ContentFilter_YieldsAudioThenThrowsSpeechException()
    {
        var audio = Sequence(20);
        await using var server = new FakeTtsServer(async (ws, s) =>
        {
            await s.ReadUntilFinishTextAsync(ws);
            await s.SendAudioAsync(ws, audio);
            await s.SendFinishAsync(ws, "content_filter", "blocked");
        });
        var client = CreateClient(server);

        await using var session = await client.OpenTtsAsync();
        await session.SendTextAsync("something");
        await session.FinishTextAsync();
        var (chunks, error) = await CollectAsync(session);

        var ex = Assert.IsType<DeepSeekSpeechException>(error);
        Assert.Equal("content_filter", ex.Code);
        Assert.Contains("blocked", ex.Message);
        Assert.Equal(audio, chunks.SelectMany(c => c).ToArray());
    }

    [Theory]
    [InlineData("invalid_input")]
    [InlineData("input_timeout")]
    [InlineData("service_error")]
    public async Task Tts_OtherFailureCodes_AreSurfaced(string code)
    {
        await using var server = new FakeTtsServer(async (ws, s) =>
        {
            await s.ReadUntilFinishTextAsync(ws);
            await s.SendFinishAsync(ws, code, "oops");
        });
        var client = CreateClient(server);

        await using var session = await client.OpenTtsAsync();
        await session.FinishTextAsync();
        var (_, error) = await CollectAsync(session);

        Assert.Equal(code, Assert.IsType<DeepSeekSpeechException>(error).Code);
    }

    [Fact]
    public async Task Tts_ServerClosesWithoutFinish_ThrowsConnectionClosed()
    {
        var audio = Sequence(10);
        await using var server = new FakeTtsServer(async (ws, s) =>
        {
            await s.ReadUntilFinishTextAsync(ws);
            await s.SendAudioAsync(ws, audio);
            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        });
        var client = CreateClient(server);

        await using var session = await client.OpenTtsAsync();
        await session.FinishTextAsync();
        var (chunks, error) = await CollectAsync(session);

        Assert.Equal("connection_closed", Assert.IsType<DeepSeekSpeechException>(error).Code);
        Assert.Equal(audio, chunks.SelectMany(c => c).ToArray());
    }

    [Fact]
    public async Task Tts_AbortEndsPendingReceiveQuietly_AndIsIdempotent()
    {
        var release = new TaskCompletionSource();
        await using var server = new FakeTtsServer(async (ws, s) =>
        {
            await s.SendAudioAsync(ws, Sequence(8));
            await Task.WhenAny(release.Task, Task.Delay(10000));
        });
        var client = CreateClient(server);

        await using var session = await client.OpenTtsAsync();
        var received = new List<byte[]>();
        var firstChunk = new TaskCompletionSource();
        var consumer = Task.Run(async () =>
        {
            await foreach (var chunk in session.ReceiveAudioAsync())
            {
                received.Add(chunk);
                firstChunk.TrySetResult();
            }
        });

        await firstChunk.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(50);
        await session.AbortAsync();
        await session.AbortAsync();

        await consumer.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(received);

        await session.SendTextAsync("ignored after abort");
        await session.FinishTextAsync();
        await session.AbortAsync();
        release.SetResult();
    }

    [Fact]
    public async Task Tts_AbortBeforeAnyAudio_EndsReceiveWithoutException()
    {
        var release = new TaskCompletionSource();
        await using var server = new FakeTtsServer(async (ws, s) =>
        {
            await Task.WhenAny(release.Task, Task.Delay(10000));
        });
        var client = CreateClient(server);

        await using var session = await client.OpenTtsAsync();
        var consumer = Task.Run(() => CollectAsync(session));
        await Task.Delay(100);
        await session.AbortAsync();

        var (chunks, error) = await consumer.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(error);
        Assert.Empty(chunks);
        release.SetResult();
    }

    [Fact]
    public async Task Tts_ReceiveAudioCanOnlyBeConsumedOnce()
    {
        await using var server = new FakeTtsServer((ws, s) => Task.CompletedTask);
        var client = CreateClient(server);

        await using var session = await client.OpenTtsAsync();
        _ = session.ReceiveAudioAsync();

        Assert.Throws<InvalidOperationException>(() => session.ReceiveAudioAsync());
    }

    [Fact]
    public async Task Tts_DisposeAsync_IsIdempotent()
    {
        await using var server = new FakeTtsServer((ws, s) => Task.CompletedTask);
        var client = CreateClient(server);

        var session = await client.OpenTtsAsync();
        await session.DisposeAsync();
        await session.DisposeAsync();
        await session.AbortAsync();
    }

    [Fact]
    public async Task OpenTts_InvalidKey_MapsToAuthenticationError()
    {
        await using var server = new FakeTtsServer((ws, s) => Task.CompletedTask, rejectStatus: 401);
        var client = CreateClient(server, o => o.ApiKey = "sk-secret-rejected");

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => client.OpenTtsAsync());

        Assert.True(ex.IsAuthenticationError);
        Assert.DoesNotContain("sk-secret-rejected", ex.ToString());
    }

    [Fact]
    public async Task OpenTts_Rejected429_IsRetriedThenExhausted()
    {
        await using var server = new FakeTtsServer((ws, s) => Task.CompletedTask, rejectStatus: 429);
        var delays = new List<TimeSpan>();
        var client = new DeepSeekSpeechClient(
            new HttpClient(),
            TestData.Options(o =>
            {
                o.BaseUrl = server.BaseUrl;
                o.MaxRetries = 2;
            }),
            delay: (t, _) =>
            {
                delays.Add(t);
                return Task.CompletedTask;
            });

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() => client.OpenTtsAsync());

        Assert.True(ex.IsRateLimited);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task OpenTts_MissingApiKey_ThrowsInvalidOperation()
    {
        await using var server = new FakeTtsServer((ws, s) => Task.CompletedTask);
        var client = CreateClient(server, o => o.ApiKey = "");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.OpenTtsAsync());
    }

    [Fact]
    public async Task OpenTts_Cancelled_Throws()
    {
        await using var server = new FakeTtsServer((ws, s) => Task.CompletedTask);
        var client = CreateClient(server);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.OpenTtsAsync(ct: cts.Token));
    }
}

/// <summary>
/// 访问真实 DeepSeek 语音服务的冒烟测试，默认不运行。
/// 需要环境变量 DEEPSEEK_LIVE_TESTS=1 与 DEEPSEEK_API_KEY。
/// </summary>
public class DeepSeekSpeechLiveSmokeTests
{
    [Fact]
    public async Task RealKey_TtsThenAsr_RoundTrips()
    {
        var key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        if (Environment.GetEnvironmentVariable("DEEPSEEK_LIVE_TESTS") != "1" || string.IsNullOrWhiteSpace(key)) return;

        var client = new DeepSeekSpeechClient(
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            new DeepSeekOptions { ApiKey = key, MaxRetries = 1, TimeoutSeconds = 60 });

        using var pcm = new MemoryStream();
        await using (var session = await client.OpenTtsAsync())
        {
            await session.SendTextAsync("你好");
            await session.FinishTextAsync();
            await foreach (var chunk in session.ReceiveAudioAsync())
            {
                pcm.Write(chunk);
            }
        }

        Assert.True(pcm.Length > 0);

        var result = await client.TranscribeAsync(pcm.ToArray(), DeepSeekTts.SampleRate);
        Assert.Contains("你好", result.Text);
    }
}
